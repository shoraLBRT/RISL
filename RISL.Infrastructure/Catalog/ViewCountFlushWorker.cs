using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RISL.Application.Abstractions;
using RISL.Infrastructure.Persistence;

namespace RISL.Infrastructure.Catalog;

/// <summary>Периодически переносит накопленные просмотры из памяти в базу.</summary>
public sealed class ViewCountFlushWorker(
    IViewCounter counter,
    IServiceScopeFactory scopeFactory,
    ILogger<ViewCountFlushWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Приложение останавливается — успеваем записать последнюю порцию.
        }

        await FlushAsync(CancellationToken.None);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var pending = counter.DrainPending();
        if (pending.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<RislDbContext>();

            foreach (var (wordId, views) in pending)
            {
                // Инкремент прямо в SQL: не тянем сущность и не трогаем дату изменения.
                await database.Words
                    .Where(word => word.Id == wordId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(word => word.ViewCount, word => word.ViewCount + views),
                        cancellationToken);
            }
        }
        catch (Exception exception)
        {
            // Счётчик просмотров не стоит того, чтобы ронять фоновую службу.
            logger.LogWarning(exception, "Не удалось записать накопленные просмотры ({Count} слов)", pending.Count);
        }
    }
}
