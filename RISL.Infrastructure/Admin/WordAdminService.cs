using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RISL.Application.Abstractions;
using RISL.Application.Admin;
using RISL.Domain;
using RISL.Domain.Entities;
using RISL.Infrastructure.Catalog;
using RISL.Infrastructure.Persistence;

namespace RISL.Infrastructure.Admin;

/// <inheritdoc cref="IWordAdminService"/>
public sealed class WordAdminService(
    RislDbContext database,
    IMediaStorage storage,
    IVideoProcessingQueue queue,
    SearchIndexMaintainer searchIndex,
    ILogger<WordAdminService> logger) : IWordAdminService
{
    public async Task<AdminWordPage> ListAsync(AdminWordQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var words = database.Words.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var normalized = TextNormalizer.Normalize(query.Search);
            words = words.Where(word =>
                word.NormalizedText.Contains(normalized) || word.NormalizedDescription.Contains(normalized));
        }

        words = query.Filter switch
        {
            AdminWordFilter.Visible => words.Where(word => word.IsPublished && word.VideoStatus == VideoStatus.Ready),
            AdminWordFilter.Hidden => words.Where(word => !word.IsPublished || word.VideoStatus != VideoStatus.Ready),
            AdminWordFilter.Failed => words.Where(word => word.VideoStatus == VideoStatus.Failed),
            AdminWordFilter.WithoutVideo => words.Where(word => word.VideoStatus == VideoStatus.None),
            _ => words,
        };

        words = (query.Sort, query.Descending) switch
        {
            (AdminWordSort.Word, false) => words.OrderBy(word => word.NormalizedText),
            (AdminWordSort.Word, true) => words.OrderByDescending(word => word.NormalizedText),
            (AdminWordSort.Views, false) => words.OrderBy(word => word.ViewCount),
            (AdminWordSort.Views, true) => words.OrderByDescending(word => word.ViewCount),
            (AdminWordSort.Status, false) => words.OrderBy(word => word.VideoStatus).ThenBy(word => word.NormalizedText),
            (AdminWordSort.Status, true) => words.OrderByDescending(word => word.VideoStatus).ThenBy(word => word.NormalizedText),
            (_, false) => words.OrderBy(word => word.UpdatedAt),
            _ => words.OrderByDescending(word => word.UpdatedAt),
        };

        var total = await words.CountAsync(cancellationToken);

        var items = await words
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(word => new AdminWordListItem(
                word.Id,
                word.Text,
                word.Description,
                word.VideoStatus,
                word.IsPublished,
                word.ViewCount,
                word.UpdatedAt,
                word.Categories.Select(category => category.Name).ToList()))
            .ToListAsync(cancellationToken);

        return new AdminWordPage(items, total, page, pageSize);
    }

    public async Task<AdminWordDetails?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var word = await database.Words
            .AsNoTracking()
            .Include(entity => entity.Categories)
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        return word is null ? null : ToDetails(word);
    }

    public async Task<AdminWordSaveResult> CreateAsync(AdminWordForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);

        var validation = await ValidateAsync(form, existingId: null, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var word = new Word(form.Text, form.Description ?? string.Empty)
        {
            IsPublished = form.IsPublished,
        };

        word.ReplaceCategories(await LoadCategoriesAsync(form.CategoryIds, cancellationToken));

        if (!string.IsNullOrWhiteSpace(form.IncomingVideoFileName))
        {
            word.MarkVideoPending(form.IncomingVideoFileName);
        }

        database.Words.Add(word);
        await database.SaveChangesAsync(cancellationToken);

        await EnqueueVideoIfPendingAsync(word, cancellationToken);
        await searchIndex.RefreshAsync(cancellationToken);

        logger.LogInformation("Создано слово {WordId} «{Text}»", word.Id, word.Text);

        return AdminWordSaveResult.Ok(word.Id);
    }

    public async Task<AdminWordSaveResult> UpdateAsync(int id, AdminWordForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);

        var word = await database.Words
            .Include(entity => entity.Categories)
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (word is null)
        {
            return AdminWordSaveResult.Fail("Слово не найдено.");
        }

        var validation = await ValidateAsync(form, existingId: id, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        word.SetText(form.Text);
        word.SetDescription(form.Description);
        word.IsPublished = form.IsPublished;
        word.ReplaceCategories(await LoadCategoriesAsync(form.CategoryIds, cancellationToken));

        if (!string.IsNullOrWhiteSpace(form.IncomingVideoFileName))
        {
            word.MarkVideoPending(form.IncomingVideoFileName);
        }

        await database.SaveChangesAsync(cancellationToken);

        await EnqueueVideoIfPendingAsync(word, cancellationToken);
        await searchIndex.RefreshAsync(cancellationToken);

        return AdminWordSaveResult.Ok(word.Id);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var word = await database.Words.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (word is null)
        {
            return false;
        }

        var video = word.VideoFileName;
        var poster = word.PosterFileName;
        var incoming = word.IncomingVideoFileName;

        database.Words.Remove(word);
        await database.SaveChangesAsync(cancellationToken);

        // Файлы убираем после базы: если удаление сорвётся, на диске останется мусор,
        // а не карточка со ссылкой в никуда.
        TryDelete(MediaArea.Videos, video);
        TryDelete(MediaArea.Posters, poster);
        TryDelete(MediaArea.Incoming, incoming);

        await searchIndex.RefreshAsync(cancellationToken);

        logger.LogInformation("Удалено слово {WordId}", id);

        return true;
    }

    public async Task<AdminWordSaveResult> RetryVideoAsync(int id, CancellationToken cancellationToken = default)
    {
        var word = await database.Words.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (word is null)
        {
            return AdminWordSaveResult.Fail("Слово не найдено.");
        }

        if (string.IsNullOrWhiteSpace(word.IncomingVideoFileName)
            || !storage.Exists(MediaArea.Incoming, word.IncomingVideoFileName))
        {
            return AdminWordSaveResult.Fail("Исходник не сохранился, загрузите видео заново.");
        }

        word.MarkVideoPending(word.IncomingVideoFileName);
        await database.SaveChangesAsync(cancellationToken);

        await queue.EnqueueAsync(new VideoProcessingRequest(word.Id, word.IncomingVideoFileName), cancellationToken);

        return AdminWordSaveResult.Ok(word.Id);
    }

    public async Task<AdminDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        // Шесть отдельных подсчётов вместо одной хитрой группировки: на трёх тысячах
        // строк в локальном файле разницы нет, а читается запрос без расшифровки.
        var words = database.Words.AsNoTracking();

        return new AdminDashboard(
            await words.CountAsync(cancellationToken),
            await words.CountAsync(word => word.IsPublished && word.VideoStatus == VideoStatus.Ready, cancellationToken),
            await words.CountAsync(word => word.VideoStatus == VideoStatus.None, cancellationToken),
            await words.CountAsync(
                word => word.VideoStatus == VideoStatus.Pending || word.VideoStatus == VideoStatus.Processing,
                cancellationToken),
            await words.CountAsync(word => word.VideoStatus == VideoStatus.Failed, cancellationToken),
            await database.Feedback.AsNoTracking().CountAsync(message => !message.IsHandled, cancellationToken));
    }

    private async Task<AdminWordSaveResult?> ValidateAsync(AdminWordForm form, int? existingId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Text))
        {
            return AdminWordSaveResult.Fail("Укажите слово.");
        }

        var normalized = TextNormalizer.Normalize(form.Text);

        var duplicate = await database.Words
            .AsNoTracking()
            .AnyAsync(
                word => word.NormalizedText == normalized && (existingId == null || word.Id != existingId),
                cancellationToken);

        return duplicate
            ? AdminWordSaveResult.Fail($"Слово «{form.Text.Trim()}» уже есть в словаре.")
            : null;
    }

    private async Task<IReadOnlyList<Category>> LoadCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0)
        {
            return [];
        }

        return await database.Categories
            .Where(category => categoryIds.Contains(category.Id))
            .ToListAsync(cancellationToken);
    }

    private async Task EnqueueVideoIfPendingAsync(Word word, CancellationToken cancellationToken)
    {
        if (word.VideoStatus == VideoStatus.Pending && !string.IsNullOrWhiteSpace(word.IncomingVideoFileName))
        {
            await queue.EnqueueAsync(new VideoProcessingRequest(word.Id, word.IncomingVideoFileName), cancellationToken);
        }
    }

    private AdminWordDetails ToDetails(Word word) => new(
        word.Id,
        word.Text,
        word.Description,
        word.IsPublished,
        word.VideoStatus,
        word.VideoError,
        word.VideoFileName is null ? null : storage.GetPublicUrl(MediaArea.Videos, word.VideoFileName),
        word.PosterFileName is null ? null : storage.GetPublicUrl(MediaArea.Posters, word.PosterFileName),
        word.VideoDurationSeconds,
        !string.IsNullOrWhiteSpace(word.IncomingVideoFileName),
        [.. word.Categories.Select(category => category.Id)]);

    private void TryDelete(MediaArea area, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            storage.Delete(area, fileName);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            logger.LogWarning(exception, "Не удалось удалить файл {File}", fileName);
        }
    }
}
