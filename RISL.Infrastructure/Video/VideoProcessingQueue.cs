using System.Threading.Channels;
using RISL.Application.Abstractions;

namespace RISL.Infrastructure.Video;

/// <summary>
/// Очередь заданий на перекодирование в памяти процесса.
/// </summary>
/// <remarks>
/// Очередь намеренно не переживает перезапуск: источником правды остаётся база,
/// а <see cref="VideoProcessingWorker"/> при старте заново набирает в неё всё,
/// что осталось в состояниях «ожидает» и «обрабатывается».
/// </remarks>
public sealed class VideoProcessingQueue : IVideoProcessingQueue
{
    private readonly Channel<VideoProcessingRequest> _channel =
        Channel.CreateUnbounded<VideoProcessingRequest>(new UnboundedChannelOptions
        {
            // Обработчик один: ffmpeg упирается в процессор, и на скромном VPS
            // параллельное кодирование только замедлит общий проход.
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(VideoProcessingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    internal IAsyncEnumerable<VideoProcessingRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
