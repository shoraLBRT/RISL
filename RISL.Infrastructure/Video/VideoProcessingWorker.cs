using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;
using RISL.Domain;
using RISL.Infrastructure.Catalog;
using RISL.Infrastructure.Media;
using RISL.Infrastructure.Persistence;

namespace RISL.Infrastructure.Video;

/// <summary>
/// Фоновая обработка загруженных видео. Загрузка файла и его перекодирование
/// разнесены: админ не ждёт ffmpeg, а массовый импорт не упирается в таймаут запроса.
/// </summary>
public sealed class VideoProcessingWorker(
    VideoProcessingQueue queue,
    IServiceScopeFactory scopeFactory,
    IVideoProcessor processor,
    IMediaStorage storage,
    SearchIndexMaintainer searchIndex,
    IOptions<MediaOptions> mediaOptions,
    ILogger<VideoProcessingWorker> logger) : BackgroundService
{
    private readonly MediaOptions _mediaOptions = mediaOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanUpUnfinishedFiles();
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Приложение останавливается: слово останется в состоянии «обрабатывается»
                // и будет подхвачено при следующем старте.
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Обработка видео для слова {WordId} сорвалась", request.WordId);
                await MarkFailedAsync(request.WordId, exception.Message, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Убирает файлы, недописанные прерванным перекодированием.
    /// </summary>
    /// <remarks>
    /// На старте обработка заведомо не идёт, поэтому любой .part — это остаток
    /// прошлой попытки. Без уборки каждый перезапуск во время кодирования
    /// оставлял бы на диске мусор, на который никто не ссылается.
    /// </remarks>
    private void CleanUpUnfinishedFiles()
    {
        var removed = 0;

        foreach (var area in new[] { MediaArea.Videos, MediaArea.Posters })
        {
            foreach (var fileName in storage.List(area, $"*{FfmpegVideoProcessor.PartSuffix}"))
            {
                try
                {
                    storage.Delete(area, fileName);
                    removed++;
                }
                catch (IOException exception)
                {
                    logger.LogWarning(exception, "Не удалось удалить недописанный файл {File}", fileName);
                }
            }
        }

        if (removed > 0)
        {
            logger.LogInformation("Удалено недописанных файлов от прерванной обработки: {Count}", removed);
        }
    }

    /// <summary>
    /// Возвращает в очередь всё, что не доехало до готового состояния. Без этого
    /// перезапуск контейнера навсегда оставлял бы слова висеть в обработке.
    /// </summary>
    private async Task RequeueUnfinishedAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<RislDbContext>();

        var unfinished = await database.Words
            .Where(word => word.VideoStatus == VideoStatus.Pending || word.VideoStatus == VideoStatus.Processing)
            .Select(word => new { word.Id, word.IncomingVideoFileName })
            .ToListAsync(cancellationToken);

        foreach (var word in unfinished)
        {
            if (string.IsNullOrWhiteSpace(word.IncomingVideoFileName))
            {
                await MarkFailedAsync(word.Id, "Исходник видео потерян, загрузите файл заново.", cancellationToken);
                continue;
            }

            await queue.EnqueueAsync(new VideoProcessingRequest(word.Id, word.IncomingVideoFileName), cancellationToken);
        }

        if (unfinished.Count > 0)
        {
            logger.LogInformation("После запуска в очередь возвращено заданий: {Count}", unfinished.Count);
        }
    }

    private async Task ProcessAsync(VideoProcessingRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<RislDbContext>();

        var word = await database.Words.FirstOrDefaultAsync(entity => entity.Id == request.WordId, cancellationToken);
        if (word is null)
        {
            logger.LogWarning("Слово {WordId} удалено, задание пропущено", request.WordId);
            CleanupIncoming(request.IncomingFileName);
            return;
        }

        if (!storage.Exists(MediaArea.Incoming, request.IncomingFileName))
        {
            word.MarkVideoFailed("Исходник видео не найден на диске, загрузите файл заново.");
            await database.SaveChangesAsync(cancellationToken);
            return;
        }

        word.MarkVideoProcessing();
        await database.SaveChangesAsync(cancellationToken);

        var previousVideo = word.VideoFileName;
        var previousPoster = word.PosterFileName;

        try
        {
            var result = await processor.ProcessAsync(request.IncomingFileName, Guid.NewGuid(), cancellationToken);

            word.MarkVideoReady(result.VideoFileName, result.PosterFileName, result.DurationSeconds);
            await database.SaveChangesAsync(cancellationToken);

            // Прежние файлы удаляем только после успешной замены: если бы ffmpeg
            // упал, слово осталось бы вообще без видео.
            DeleteIfPresent(MediaArea.Videos, previousVideo, result.VideoFileName);
            DeleteIfPresent(MediaArea.Posters, previousPoster, result.PosterFileName);

            ArchiveIncoming(request.IncomingFileName);

            await searchIndex.RefreshAsync(cancellationToken);

            logger.LogInformation("Видео для слова {WordId} готово: {File}", word.Id, result.VideoFileName);
        }
        catch (VideoProcessingException exception)
        {
            word.MarkVideoFailed(exception.Message);
            await database.SaveChangesAsync(cancellationToken);

            // Слово могло быть опубликовано с прежним видео — снимок пересобираем,
            // чтобы оно исчезло из выдачи, пока обработка не удалась.
            await searchIndex.RefreshAsync(cancellationToken);

            logger.LogWarning("Видео для слова {WordId} не обработано: {Reason}", word.Id, exception.Message);
        }
    }

    private async Task MarkFailedAsync(int wordId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<RislDbContext>();

            var word = await database.Words.FirstOrDefaultAsync(entity => entity.Id == wordId, cancellationToken);
            if (word is null)
            {
                return;
            }

            word.MarkVideoFailed(reason);
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось отметить неудачу обработки для слова {WordId}", wordId);
        }
    }

    /// <summary>Исходник либо переезжает в архив, либо удаляется — по настройке.</summary>
    private void ArchiveIncoming(string incomingFileName)
    {
        try
        {
            if (_mediaOptions.KeepOriginals)
            {
                var source = storage.GetPhysicalPath(MediaArea.Incoming, incomingFileName);
                var target = storage.GetPhysicalPath(MediaArea.Originals, incomingFileName);
                File.Move(source, target, overwrite: true);
                return;
            }

            storage.Delete(MediaArea.Incoming, incomingFileName);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Не удалось убрать исходник {File}", incomingFileName);
        }
    }

    private void CleanupIncoming(string incomingFileName)
    {
        try
        {
            storage.Delete(MediaArea.Incoming, incomingFileName);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Не удалось удалить исходник {File}", incomingFileName);
        }
    }

    private void DeleteIfPresent(MediaArea area, string? fileName, string replacement)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.Equals(fileName, replacement, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            storage.Delete(area, fileName);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Не удалось удалить устаревший файл {File}", fileName);
        }
    }
}
