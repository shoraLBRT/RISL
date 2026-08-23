namespace RISL.Application.Abstractions;

/// <summary>
/// Очередь заданий на перекодирование. Загрузка файла и его обработка разнесены:
/// админ не ждёт ffmpeg, а массовый импорт не упирается в таймаут запроса.
/// </summary>
public interface IVideoProcessingQueue
{
    /// <summary>Ставит слово в очередь на обработку приложенного к нему исходника.</summary>
    ValueTask EnqueueAsync(VideoProcessingRequest request, CancellationToken cancellationToken = default);
}

/// <param name="WordId">Слово, к которому относится запись.</param>
/// <param name="IncomingFileName">Имя исходника в <see cref="MediaArea.Incoming"/>.</param>
public sealed record VideoProcessingRequest(int WordId, string IncomingFileName);
