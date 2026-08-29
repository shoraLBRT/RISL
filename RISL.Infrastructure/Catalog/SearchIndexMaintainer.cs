using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RISL.Application.Abstractions;
using RISL.Application.Search;
using RISL.Domain;
using RISL.Infrastructure.Persistence;

namespace RISL.Infrastructure.Catalog;

/// <summary>
/// Перечитывает публичную часть словаря из базы в поисковый индекс.
/// </summary>
/// <remarks>
/// Вызывается при старте приложения и после каждой записи в админке. Полная
/// перезагрузка вместо точечных правок — сознательный выбор: три тысячи строк
/// читаются за миллисекунды, а рассинхронизация индекса с базой становится невозможной.
/// </remarks>
public sealed class SearchIndexMaintainer(
    IServiceScopeFactory scopeFactory,
    IWordSearchIndex index,
    ILogger<SearchIndexMaintainer> logger)
{
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<RislDbContext>();

        var words = await database.Words
            .AsNoTracking()
            .Where(word => word.IsPublished && word.VideoStatus == VideoStatus.Ready)
            .Select(word => new
            {
                word.Id,
                word.Text,
                word.NormalizedText,
                word.Description,
                word.NormalizedDescription,
                word.Slug,
                word.VideoFileName,
                word.PosterFileName,
                word.VideoDurationSeconds,
                CategoryIds = word.Categories.Select(category => category.Id).ToList(),
            })
            .ToListAsync(cancellationToken);

        var categories = await database.Categories
            .AsNoTracking()
            .Select(category => new CategoryView(
                category.Id,
                category.Name,
                category.Slug,
                category.SortOrder,
                category.Words.Count(word => word.IsPublished && word.VideoStatus == VideoStatus.Ready)))
            .ToListAsync(cancellationToken);

        var entries = words
            .Select(word => new WordSearchEntry(
                word.Id,
                word.Text,
                word.NormalizedText,
                word.Description,
                word.NormalizedDescription,
                word.Slug,
                word.VideoFileName,
                word.PosterFileName,
                word.VideoDurationSeconds,
                word.CategoryIds))
            .ToArray();

        // Пустые темы в фильтре гостю только мешают.
        var visibleCategories = categories.Where(category => category.WordCount > 0).ToArray();

        index.Load(entries, visibleCategories);

        logger.LogInformation(
            "Поисковый индекс обновлён: {WordCount} слов, {CategoryCount} тем",
            entries.Length,
            visibleCategories.Length);
    }
}
