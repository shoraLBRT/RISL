using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;
using RISL.Infrastructure.Media;

namespace RISL.Blazor.Endpoints;

/// <param name="StoredFileName">Имя, под которым файл сохранён в области незавершённых загрузок.</param>
/// <param name="Error">Причина отказа, если файл не принят.</param>
public sealed record UploadOutcome(string? StoredFileName, string? Error)
{
    public static readonly UploadOutcome Skipped = new(null, null);

    public bool IsRejected => Error is not null;
}

/// <summary>Приём загруженного видеофайла.</summary>
public static class UploadHelper
{
    private static readonly string[] AllowedExtensions =
        [".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".3gp", ".wmv"];

    /// <summary>
    /// Сохраняет исходник и убеждается, что это действительно видео.
    /// </summary>
    /// <remarks>
    /// Имя файла берётся не от пользователя, а генерируется: так ни форма, ни архив
    /// импорта не смогут записать что-либо за пределы хранилища. Расширение проверяется
    /// заранее, а окончательный вердикт выносит ffprobe — подделать расширение проще,
    /// чем видеопоток.
    /// </remarks>
    public static async Task<UploadOutcome> SaveVideoAsync(
        IFormFile? file,
        IMediaStorage storage,
        IVideoProcessor processor,
        IOptions<MediaOptions> mediaOptions,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return UploadOutcome.Skipped;
        }

        var limit = mediaOptions.Value.MaxUploadBytes;
        if (file.Length > limit)
        {
            return new UploadOutcome(null, $"Файл больше допустимых {limit / (1024 * 1024)} МБ.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return new UploadOutcome(null, $"Формат «{extension}» не поддерживается. Загрузите видеофайл.");
        }

        var storedName = $"{Guid.NewGuid():N}{extension}";

        await using (var stream = file.OpenReadStream())
        {
            await storage.SaveAsync(MediaArea.Incoming, storedName, stream, cancellationToken);
        }

        var probe = await processor.ProbeAsync(storedName, cancellationToken);
        if (!probe.IsVideo)
        {
            storage.Delete(MediaArea.Incoming, storedName);
            return new UploadOutcome(null, probe.Error ?? "Файл не удалось распознать как видео.");
        }

        return new UploadOutcome(storedName, null);
    }
}
