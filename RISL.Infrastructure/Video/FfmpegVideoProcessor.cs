using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;

namespace RISL.Infrastructure.Video;

/// <summary>
/// Приводит записи с камеры или телефона к виду, который проигрывает любой браузер.
/// </summary>
/// <remarks>
/// Без этого шага часть исходников просто не откроется у пользователя: телефоны
/// снимают в HEVC внутри .mov, а мобильные браузеры уверенно тянут только H.264.
/// </remarks>
public sealed class FfmpegVideoProcessor(
    IMediaStorage storage,
    IOptions<FfmpegOptions> options,
    ILogger<FfmpegVideoProcessor> logger) : IVideoProcessor
{
    /// <summary>Расширение недоделанного файла. Раздача статики такие файлы не отдаёт.</summary>
    public const string PartSuffix = ".part";

    private readonly FfmpegOptions _options = options.Value;

    private TimeSpan Timeout => TimeSpan.FromSeconds(_options.TimeoutSeconds);

    public async Task<VideoProbeResult> ProbeAsync(string sourceFileName, CancellationToken cancellationToken = default)
    {
        var sourcePath = storage.GetPhysicalPath(MediaArea.Incoming, sourceFileName);

        var result = await ProcessRunner.RunAsync(
            _options.ProbePath,
            [
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=codec_name",
                "-show_entries", "format=duration",
                "-of", "json",
                sourcePath,
            ],
            Timeout,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return new VideoProbeResult(false, 0, $"Файл не удалось прочитать как видео. {result.ShortError(300)}");
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;

            var hasVideoStream = root.TryGetProperty("streams", out var streams)
                && streams.ValueKind == JsonValueKind.Array
                && streams.GetArrayLength() > 0;

            if (!hasVideoStream)
            {
                return new VideoProbeResult(false, 0, "В файле нет видеодорожки.");
            }

            var duration = 0d;
            if (root.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var durationValue)
                && double.TryParse(durationValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                duration = parsed;
            }

            return new VideoProbeResult(true, duration, null);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Не удалось разобрать ответ ffprobe для {File}", sourceFileName);
            return new VideoProbeResult(false, 0, "Не удалось разобрать сведения о файле.");
        }
    }

    public async Task<VideoProcessingResult> ProcessAsync(string sourceFileName, Guid assetId, CancellationToken cancellationToken = default)
    {
        var sourcePath = storage.GetPhysicalPath(MediaArea.Incoming, sourceFileName);

        var videoFileName = $"{assetId:N}.mp4";
        var posterFileName = $"{assetId:N}.jpg";

        // Кодируем во временные имена и переставляем на место только при полном
        // успехе. Если процесс убьют посреди работы, в каталоге останется файл
        // с расширением .part: раздача его не отдаёт, а уборка при старте удалит.
        // Иначе каждый перезапуск во время обработки оставлял бы навсегда лежащий
        // недокодированный ролик, на который никто не ссылается.
        var videoTempName = videoFileName + PartSuffix;
        var posterTempName = posterFileName + PartSuffix;

        var videoPath = storage.GetPhysicalPath(MediaArea.Videos, videoTempName);
        var posterPath = storage.GetPhysicalPath(MediaArea.Posters, posterTempName);

        var encode = await ProcessRunner.RunAsync(
            _options.Path,
            [
                "-y",
                "-i", sourcePath,
                // Уменьшаем только то, что шире заданного предела: апскейл ничего не даёт.
                "-vf", $"scale='min({_options.MaxWidth},iw)':-2",
                "-c:v", "libx264",
                "-preset", _options.Preset,
                "-crf", _options.Crf.ToString(CultureInfo.InvariantCulture),
                "-profile:v", "high",
                // Без yuv420p часть мобильных браузеров покажет чёрный экран.
                "-pix_fmt", "yuv420p",
                // Индекс в начало файла: видео стартует, не дожидаясь полной загрузки.
                "-movflags", "+faststart",
                "-c:a", "aac",
                "-b:a", "96k",
                // Формат задаём явно: по расширению .part ffmpeg его не угадает.
                "-f", "mp4",
                videoPath,
            ],
            Timeout,
            cancellationToken);

        if (!encode.IsSuccess)
        {
            TryDelete(MediaArea.Videos, videoTempName);
            throw new VideoProcessingException($"Перекодирование не удалось. {encode.ShortError()}");
        }

        var poster = await ProcessRunner.RunAsync(
            _options.Path,
            [
                "-y",
                "-ss", _options.PosterSecond.ToString(CultureInfo.InvariantCulture),
                "-i", videoPath,
                "-frames:v", "1",
                "-vf", "scale=640:-2",
                "-c:v", "mjpeg",
                "-f", "image2",
                posterPath,
            ],
            Timeout,
            cancellationToken);

        if (!poster.IsSuccess)
        {
            // Короткий ролик может закончиться раньше выбранной секунды — берём первый кадр.
            poster = await ProcessRunner.RunAsync(
                _options.Path,
                [
                    "-y", "-i", videoPath, "-frames:v", "1", "-vf", "scale=640:-2",
                    "-c:v", "mjpeg", "-f", "image2", posterPath,
                ],
                Timeout,
                cancellationToken);
        }

        if (!poster.IsSuccess)
        {
            TryDelete(MediaArea.Videos, videoTempName);
            TryDelete(MediaArea.Posters, posterTempName);
            throw new VideoProcessingException($"Не удалось получить кадр-заставку. {poster.ShortError()}");
        }

        var probe = await ProbeProcessedAsync(videoPath, cancellationToken);

        File.Move(videoPath, storage.GetPhysicalPath(MediaArea.Videos, videoFileName), overwrite: true);
        File.Move(posterPath, storage.GetPhysicalPath(MediaArea.Posters, posterFileName), overwrite: true);

        return new VideoProcessingResult(videoFileName, posterFileName, probe);
    }

    private async Task<double> ProbeProcessedAsync(string videoPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            _options.ProbePath,
            ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", videoPath],
            Timeout,
            cancellationToken);

        return result.IsSuccess
            && double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            ? duration
            : 0;
    }

    private void TryDelete(MediaArea area, string fileName)
    {
        try
        {
            storage.Delete(area, fileName);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Не удалось убрать незавершённый файл {File}", fileName);
        }
    }
}

/// <summary>Обработка видео сорвалась; сообщение показывается админу в карточке слова.</summary>
public sealed class VideoProcessingException(string message) : Exception(message);
