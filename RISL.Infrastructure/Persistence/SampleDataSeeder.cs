using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;
using RISL.Domain.Entities;
using RISL.Infrastructure.Video;

namespace RISL.Infrastructure.Persistence;

/// <summary>
/// Наполняет пустую базу примерами для локальной разработки.
/// </summary>
/// <remarks>
/// Чтобы страницы выглядели как настоящие, один синтетический ролик генерируется
/// через ffmpeg и переиспользуется всеми словами. Настоящего контента здесь нет
/// и быть не должно — в продакшене сидирование выключено.
/// </remarks>
public sealed class SampleDataSeeder(
    RislDbContext database,
    IMediaStorage storage,
    IOptions<FfmpegOptions> ffmpegOptions,
    ILogger<SampleDataSeeder> logger)
{
    private readonly FfmpegOptions _ffmpeg = ffmpegOptions.Value;

    private static readonly (string Word, string Description, string Category)[] Samples =
    [
        ("Привет", "Раскрытая ладонь у виска, короткое движение вперёд от лица.", "Приветствия"),
        ("Спасибо", "Кончики пальцев касаются подбородка, рука опускается вперёд и вниз.", "Приветствия"),
        ("Пожалуйста", "Раскрытая ладонь описывает небольшой круг у груди.", "Приветствия"),
        ("Мама", "Указательный палец дважды касается щеки.", "Семья"),
        ("Папа", "Указательный и средний пальцы дважды касаются лба.", "Семья"),
        ("Сестра", "Ребро ладони дважды проводит вдоль щеки сверху вниз.", "Семья"),
        ("Хлеб", "Ребро ладони изображает нарезание по другой раскрытой ладони.", "Еда"),
        ("Вода", "Указательный палец дважды касается уголка рта.", "Еда"),
        ("Яблоко", "Согнутый указательный палец поворачивается у щеки.", "Еда"),
        ("Один", "Поднятый вверх указательный палец.", "Цифры"),
        ("Два", "Указательный и средний пальцы подняты вверх.", "Цифры"),
        ("Три", "Большой, указательный и средний пальцы подняты вверх.", "Цифры"),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await database.Words.AnyAsync(cancellationToken))
        {
            return;
        }

        var sample = await TryCreateSampleVideoAsync(cancellationToken);

        var categories = Samples
            .Select(item => item.Category)
            .Distinct()
            .Select((name, order) => new Category(name, order))
            .ToDictionary(category => category.Name, StringComparer.Ordinal);

        database.Categories.AddRange(categories.Values);

        foreach (var (text, description, categoryName) in Samples)
        {
            var word = new Word(text, description);
            word.ReplaceCategories([categories[categoryName]]);

            if (sample is { } media)
            {
                word.MarkVideoReady(media.VideoFileName, media.PosterFileName, media.DurationSeconds);
            }

            database.Words.Add(word);
        }

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "База наполнена примерами: {WordCount} слов, видео {VideoState}",
            Samples.Length,
            sample is null ? "не создано" : "создано");
    }

    /// <summary>
    /// Рисует короткий тестовый ролик средствами ffmpeg. Если ffmpeg недоступен,
    /// слова заводятся без видео — гостю они не покажутся, но админка будет рабочей.
    /// </summary>
    private async Task<VideoProcessingResult?> TryCreateSampleVideoAsync(CancellationToken cancellationToken)
    {
        var assetId = Guid.NewGuid();
        var videoFileName = $"{assetId:N}.mp4";
        var posterFileName = $"{assetId:N}.jpg";

        try
        {
            var videoPath = storage.GetPhysicalPath(MediaArea.Videos, videoFileName);
            var posterPath = storage.GetPhysicalPath(MediaArea.Posters, posterFileName);

            var encode = await ProcessRunner.RunAsync(
                _ffmpeg.Path,
                [
                    "-y",
                    "-f", "lavfi",
                    "-i", "testsrc=size=1280x720:rate=25:duration=6",
                    "-c:v", "libx264",
                    "-pix_fmt", "yuv420p",
                    "-movflags", "+faststart",
                    videoPath,
                ],
                TimeSpan.FromSeconds(120),
                cancellationToken);

            if (!encode.IsSuccess)
            {
                logger.LogWarning("Тестовое видео не создано: {Reason}", encode.ShortError(200));
                return null;
            }

            var poster = await ProcessRunner.RunAsync(
                _ffmpeg.Path,
                ["-y", "-i", videoPath, "-frames:v", "1", "-vf", "scale=640:-2", posterPath],
                TimeSpan.FromSeconds(120),
                cancellationToken);

            return poster.IsSuccess ? new VideoProcessingResult(videoFileName, posterFileName, 6) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "ffmpeg недоступен, примеры создаются без видео");
            return null;
        }
    }
}
