using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RISL.Application.Abstractions;
using RISL.Application.Search;
using RISL.Infrastructure.Admin;
using RISL.Infrastructure.Catalog;
using RISL.Infrastructure.Media;
using RISL.Infrastructure.Persistence;
using RISL.Infrastructure.Video;

namespace RISL.Infrastructure;

/// <summary>Регистрация инфраструктуры словаря в контейнере приложения.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddRislInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));
        services.Configure<FfmpegOptions>(configuration.GetSection(FfmpegOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        var databasePath = configuration.GetSection(DatabaseOptions.SectionName)["Path"] ?? new DatabaseOptions().Path;
        services.AddDbContext<RislDbContext>(options => options.UseSqlite(BuildConnectionString(databasePath)));

        // Хранилище и поисковый индекс живут всё время работы процесса.
        services.AddSingleton<FileSystemMediaStorage>();
        services.AddSingleton<IMediaStorage>(provider => provider.GetRequiredService<FileSystemMediaStorage>());
        services.AddSingleton<IWordSearchIndex, WordSearchIndex>();
        services.AddSingleton<SearchIndexMaintainer>();
        services.AddSingleton<IViewCounter, ViewCounter>();
        services.AddSingleton<IVideoProcessor, FfmpegVideoProcessor>();
        services.AddSingleton<VideoProcessingQueue>();
        services.AddSingleton<IVideoProcessingQueue>(provider => provider.GetRequiredService<VideoProcessingQueue>());

        services.AddScoped<IWordAdminService, WordAdminService>();
        services.AddScoped<ICategoryAdminService, CategoryAdminService>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<SampleDataSeeder>();

        // Порядок важен: миграции должны пройти до того, как обработчик видео
        // начнёт искать в базе незавершённые задания.
        services.AddHostedService<DatabaseInitializer>();
        services.AddHostedService<VideoProcessingWorker>();
        services.AddHostedService<ViewCountFlushWorker>();

        return services;
    }

    /// <summary>
    /// Строит строку подключения и создаёт каталог под файл базы: при первом запуске
    /// в контейнере тома ещё пустые.
    /// </summary>
    private static string BuildConnectionString(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }
}
