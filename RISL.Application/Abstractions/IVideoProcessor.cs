namespace RISL.Application.Abstractions;

/// <summary>Перекодирование исходной записи в пригодный для веба вид.</summary>
public interface IVideoProcessor
{
    /// <summary>
    /// Проверяет, что файл вообще является видео, и заодно возвращает его длительность.
    /// Вызывается до записи в базу, чтобы мусорный файл не породил карточку.
    /// </summary>
    Task<VideoProbeResult> ProbeAsync(string sourceFileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Готовит mp4 и постер. Имена результатов детерминированы и строятся от <paramref name="assetId"/>.
    /// </summary>
    Task<VideoProcessingResult> ProcessAsync(string sourceFileName, Guid assetId, CancellationToken cancellationToken = default);
}

/// <param name="IsVideo">Удалось ли распознать поток видео.</param>
/// <param name="DurationSeconds">Длительность, если распознана.</param>
/// <param name="Error">Причина, по которой файл забракован.</param>
public sealed record VideoProbeResult(bool IsVideo, double DurationSeconds, string? Error);

/// <param name="VideoFileName">Имя готового mp4 в <see cref="MediaArea.Videos"/>.</param>
/// <param name="PosterFileName">Имя постера в <see cref="MediaArea.Posters"/>.</param>
/// <param name="DurationSeconds">Длительность готового видео.</param>
public sealed record VideoProcessingResult(string VideoFileName, string PosterFileName, double DurationSeconds);
