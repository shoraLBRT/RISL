using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RISL.Application.Abstractions;
using RISL.Application.Import;
using RISL.Domain;
using RISL.Domain.Entities;
using RISL.Infrastructure.Catalog;
using RISL.Infrastructure.Persistence;

namespace RISL.Infrastructure.Admin;

/// <summary>
/// Массовый импорт словаря.
/// </summary>
/// <remarks>
/// Разбор и применение разделены: админ сначала видит отчёт с номерами строк
/// и только потом подтверждает запись. Три тысячи слов иначе пришлось бы вносить руками.
/// </remarks>
public sealed class ImportService(
    RislDbContext database,
    IMediaStorage storage,
    IVideoProcessingQueue queue,
    SearchIndexMaintainer searchIndex,
    ILogger<ImportService> logger) : IImportService
{
    public async Task<int> PrepareAsync(ImportSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var read = ImportCsvReader.Read(source.CsvContent);

        if (read.IsFatal)
        {
            return await SaveJobAsync(source.FileName, new ImportReport([], new Dictionary<string, string>(), read.FatalError), ImportPlan.Empty, cancellationToken);
        }

        var files = source.VideoArchive is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await ExtractArchiveAsync(source.VideoArchive, cancellationToken);

        var existing = await database.Words
            .AsNoTracking()
            .Select(word => word.NormalizedText)
            .ToListAsync(cancellationToken);

        var plan = ImportPlanner.Plan(
            read,
            new HashSet<string>(existing, StringComparer.Ordinal),
            new HashSet<string>(files.Keys, StringComparer.OrdinalIgnoreCase));

        var report = new ImportReport(plan.Items, files, null);

        return await SaveJobAsync(source.FileName, report, plan, cancellationToken);
    }

    public async Task<ImportJobView?> GetAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await database.ImportJobs.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == jobId, cancellationToken);

        return job is null ? null : ToView(job);
    }

    public async Task<IReadOnlyList<ImportJobView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await database.ImportJobs
            .AsNoTracking()
            .OrderByDescending(job => job.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        // Построчный отчёт в списке не нужен — показываем только сводку.
        return [.. jobs.Select(job => ToView(job, includeItems: false))];
    }

    public async Task<ImportJobView?> ApplyAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await database.ImportJobs.FirstOrDefaultAsync(entity => entity.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.Status != ImportJobStatus.AwaitingConfirmation)
        {
            return ToView(job);
        }

        var report = ImportReport.FromJson(job.ReportJson);

        job.MarkApplying();
        await database.SaveChangesAsync(cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var pendingVideos = await ApplyItemsAsync(report, cancellationToken);

            job.MarkCompleted();
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Очередь наполняем только после успешной фиксации: иначе обработчик
            // погнался бы за словами, которых в базе так и не появилось.
            foreach (var request in pendingVideos)
            {
                await queue.EnqueueAsync(request, cancellationToken);
            }

            await searchIndex.RefreshAsync(cancellationToken);

            logger.LogInformation(
                "Импорт {JobId} применён: создано {Created}, обновлено {Updated}",
                job.Id,
                job.ToCreate,
                job.ToUpdate);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);

            logger.LogError(exception, "Импорт {JobId} сорвался, изменения откачены", job.Id);

            job.MarkFailed();
            await database.SaveChangesAsync(cancellationToken);
        }

        return ToView(job);
    }

    public async Task<bool> CancelAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await database.ImportJobs.FirstOrDefaultAsync(entity => entity.Id == jobId, cancellationToken);
        if (job is null || job.Status != ImportJobStatus.AwaitingConfirmation)
        {
            return false;
        }

        var report = ImportReport.FromJson(job.ReportJson);
        foreach (var storedName in report.Files.Values)
        {
            TryDeleteIncoming(storedName);
        }

        job.MarkCancelled();
        await database.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>Применяет план и возвращает задания на обработку приложенных видео.</summary>
    private async Task<List<VideoProcessingRequest>> ApplyItemsAsync(ImportReport report, CancellationToken cancellationToken)
    {
        var applicable = report.Items.Where(item => item.IsApplicable).ToList();
        var pendingVideos = new List<VideoProcessingRequest>();

        var categories = await LoadOrCreateCategoriesAsync(applicable, cancellationToken);

        var normalizedWords = applicable.Select(item => TextNormalizer.Normalize(item.Word)).ToHashSet(StringComparer.Ordinal);
        var existing = await database.Words
            .Include(word => word.Categories)
            .Where(word => normalizedWords.Contains(word.NormalizedText))
            .ToDictionaryAsync(word => word.NormalizedText, StringComparer.Ordinal, cancellationToken);

        foreach (var item in applicable)
        {
            var normalized = TextNormalizer.Normalize(item.Word);

            if (!existing.TryGetValue(normalized, out var word))
            {
                word = new Word(item.Word, item.Description);
                database.Words.Add(word);
                existing[normalized] = word;
            }
            else
            {
                word.SetText(item.Word);
                word.SetDescription(item.Description);
            }

            word.ReplaceCategories([.. item.Categories
                .Select(TextNormalizer.Normalize)
                .Where(categories.ContainsKey)
                .Select(name => categories[name])]);

            if (item.VideoFileName is { } videoFileName && report.Files.TryGetValue(videoFileName, out var storedName))
            {
                word.MarkVideoPending(storedName);
            }
        }

        // Идентификаторы новых слов известны только после записи.
        await database.SaveChangesAsync(cancellationToken);

        foreach (var item in applicable)
        {
            if (item.VideoFileName is not { } videoFileName || !report.Files.TryGetValue(videoFileName, out var storedName))
            {
                continue;
            }

            var word = existing[TextNormalizer.Normalize(item.Word)];
            pendingVideos.Add(new VideoProcessingRequest(word.Id, storedName));
        }

        return pendingVideos;
    }

    /// <summary>Темы, упомянутые в файле, но отсутствующие в словаре, создаются на лету.</summary>
    private async Task<Dictionary<string, Category>> LoadOrCreateCategoriesAsync(
        IReadOnlyList<ImportPlanItem> items,
        CancellationToken cancellationToken)
    {
        var wanted = items
            .SelectMany(item => item.Categories)
            .Select(name => (Raw: name, Normalized: TextNormalizer.Normalize(name)))
            .Where(pair => pair.Normalized.Length > 0)
            .DistinctBy(pair => pair.Normalized)
            .ToList();

        // Список имён выносим наружу: подзапрос вида wanted.Select(...) внутри
        // предиката EF перевести в SQL не может и уходит в вычисление на клиенте.
        var wantedNames = wanted.Select(pair => pair.Normalized).ToList();

        var result = await database.Categories
            .Where(category => wantedNames.Contains(category.NormalizedName))
            .ToDictionaryAsync(category => category.NormalizedName, StringComparer.Ordinal, cancellationToken);

        foreach (var pair in wanted.Where(pair => !result.ContainsKey(pair.Normalized)))
        {
            var category = new Category(pair.Raw);
            database.Categories.Add(category);
            result[pair.Normalized] = category;
        }

        return result;
    }

    /// <summary>
    /// Распаковывает архив в область незавершённых загрузок.
    /// </summary>
    /// <remarks>
    /// Имена внутри архива приходят от пользователя, поэтому путь из записи не
    /// используется вовсе: берётся только имя файла, а на диск всё ложится под
    /// сгенерированным именем. Так архив с записью «../../app.dll» ничего не перезапишет.
    /// </remarks>
    private async Task<Dictionary<string, string>> ExtractArchiveAsync(Stream archiveStream, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var originalName = Path.GetFileName(entry.FullName);

            // Каталоги и служебные записи вроде __MACOSX пропускаем.
            if (string.IsNullOrWhiteSpace(originalName) || entry.Length == 0)
            {
                continue;
            }

            if (files.ContainsKey(originalName))
            {
                logger.LogWarning("В архиве несколько файлов с именем {File}, взят первый", originalName);
                continue;
            }

            var extension = Path.GetExtension(originalName);
            var storedName = $"{Guid.NewGuid():N}{extension}";

            await using var entryStream = entry.Open();
            await storage.SaveAsync(MediaArea.Incoming, storedName, entryStream, cancellationToken);

            files[originalName] = storedName;
        }

        return files;
    }

    private async Task<int> SaveJobAsync(string fileName, ImportReport report, ImportPlan plan, CancellationToken cancellationToken)
    {
        var job = new ImportJob(
            fileName,
            report.ToJson(),
            plan.TotalRows,
            plan.ToCreate,
            plan.ToUpdate,
            plan.Failed);

        if (report.Error is not null)
        {
            job.MarkFailed();
        }

        database.ImportJobs.Add(job);
        await database.SaveChangesAsync(cancellationToken);

        return job.Id;
    }

    private void TryDeleteIncoming(string storedName)
    {
        try
        {
            storage.Delete(MediaArea.Incoming, storedName);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            logger.LogWarning(exception, "Не удалось удалить распакованный файл {File}", storedName);
        }
    }

    private static ImportJobView ToView(ImportJob job, bool includeItems = true)
    {
        var report = ImportReport.FromJson(job.ReportJson);

        return new ImportJobView(
            job.Id,
            job.FileName,
            job.CreatedAt,
            job.CompletedAt,
            job.Status,
            job.TotalRows,
            job.ToCreate,
            job.ToUpdate,
            job.Failed,
            includeItems ? report.Items : [],
            report.Error);
    }
}
