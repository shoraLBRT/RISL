using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;
using RISL.Application.Search;
using RISL.Infrastructure.Admin;
using RISL.Infrastructure.Catalog;
using RISL.Infrastructure.Media;
using RISL.Infrastructure.Persistence;

namespace RISL.Tests.Integration;

/// <summary>
/// Поднимает словарь на настоящей базе SQLite и настоящем файловом хранилище
/// во временном каталоге.
/// </summary>
/// <remarks>
/// Импорт — самая рискованная операция сервиса: он пишет пачками, создаёт темы
/// на лету и распаковывает присланный пользователем архив. Проверять его на
/// подменённом хранилище смысла мало, поэтому здесь всё настоящее, кроме ffmpeg:
/// перекодирование в тестах не запускается, вместо очереди стоит перехватчик.
/// </remarks>
public sealed class DictionaryFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _root;

    public DictionaryFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), $"risl-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var collection = new ServiceCollection();

        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton(Options.Create(new MediaOptions { RootPath = Path.Combine(_root, "media") }));

        collection.AddDbContext<RislDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(_root, "test.db")}"));

        collection.AddSingleton<FileSystemMediaStorage>();
        collection.AddSingleton<IMediaStorage>(provider => provider.GetRequiredService<FileSystemMediaStorage>());
        collection.AddSingleton<IWordSearchIndex, WordSearchIndex>();
        collection.AddSingleton<SearchIndexMaintainer>();
        collection.AddSingleton<RecordingVideoQueue>();
        collection.AddSingleton<IVideoProcessingQueue>(provider => provider.GetRequiredService<RecordingVideoQueue>());

        collection.AddScoped<IImportService, ImportService>();
        collection.AddScoped<IWordAdminService, WordAdminService>();
        collection.AddScoped<ICategoryAdminService, CategoryAdminService>();
        collection.AddScoped<IFeedbackService, FeedbackService>();

        _services = collection.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RislDbContext>().Database.Migrate();
    }

    public IMediaStorage Storage => _services.GetRequiredService<IMediaStorage>();

    public IWordSearchIndex Index => _services.GetRequiredService<IWordSearchIndex>();

    public RecordingVideoQueue Queue => _services.GetRequiredService<RecordingVideoQueue>();

    /// <summary>Выполняет действие в собственной области, как это делает запрос к приложению.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = _services.CreateScope();
        await action(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог уберёт система; падать из-за него тест не должен.
        }
    }
}

/// <summary>Вместо ffmpeg просто запоминает, что было поставлено в очередь.</summary>
public sealed class RecordingVideoQueue : IVideoProcessingQueue
{
    private readonly List<VideoProcessingRequest> _requests = [];

    public IReadOnlyList<VideoProcessingRequest> Requests => _requests;

    public ValueTask EnqueueAsync(VideoProcessingRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Add(request);
        return ValueTask.CompletedTask;
    }
}
