using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RISL.Infrastructure.Catalog;

namespace RISL.Infrastructure.Persistence;

/// <summary>
/// Готовит базу к работе и загружает поисковый индекс.
/// </summary>
/// <remarks>
/// Регистрируется первой из фоновых служб: миграции должны завершиться до того,
/// как обработчик видео полезет искать незаконченные задания.
/// </remarks>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    SearchIndexMaintainer searchIndex,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    private readonly DatabaseOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<RislDbContext>();

        if (_options.MigrateOnStartup)
        {
            await database.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Миграции применены");
        }

        // WAL: фоновая запись просмотров перестаёт блокировать читающие запросы.
        // Режим сохраняется в самом файле базы, поэтому достаточно выставить его один раз.
        await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);

        if (_options.SeedSampleData)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }

        await searchIndex.RefreshAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
