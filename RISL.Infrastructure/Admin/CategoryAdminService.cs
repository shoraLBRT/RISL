using Microsoft.EntityFrameworkCore;
using RISL.Application.Abstractions;
using RISL.Application.Admin;
using RISL.Domain;
using RISL.Domain.Entities;
using RISL.Infrastructure.Catalog;
using RISL.Infrastructure.Persistence;

namespace RISL.Infrastructure.Admin;

/// <inheritdoc cref="ICategoryAdminService"/>
public sealed class CategoryAdminService(
    RislDbContext database,
    SearchIndexMaintainer searchIndex) : ICategoryAdminService
{
    public async Task<IReadOnlyList<AdminCategory>> ListAsync(CancellationToken cancellationToken = default) =>
        await database.Categories
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new AdminCategory(
                category.Id,
                category.Name,
                category.Slug,
                category.SortOrder,
                category.Words.Count))
            .ToListAsync(cancellationToken);

    public async Task<AdminWordSaveResult> CreateAsync(string name, int sortOrder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return AdminWordSaveResult.Fail("Укажите название темы.");
        }

        if (await ExistsAsync(name, existingId: null, cancellationToken))
        {
            return AdminWordSaveResult.Fail($"Тема «{name.Trim()}» уже есть.");
        }

        var category = new Category(name, sortOrder);
        database.Categories.Add(category);
        await database.SaveChangesAsync(cancellationToken);

        await searchIndex.RefreshAsync(cancellationToken);

        return AdminWordSaveResult.Ok(category.Id);
    }

    public async Task<AdminWordSaveResult> UpdateAsync(int id, string name, int sortOrder, CancellationToken cancellationToken = default)
    {
        var category = await database.Categories.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (category is null)
        {
            return AdminWordSaveResult.Fail("Тема не найдена.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return AdminWordSaveResult.Fail("Укажите название темы.");
        }

        if (await ExistsAsync(name, id, cancellationToken))
        {
            return AdminWordSaveResult.Fail($"Тема «{name.Trim()}» уже есть.");
        }

        category.SetName(name);
        category.SortOrder = sortOrder;
        await database.SaveChangesAsync(cancellationToken);

        await searchIndex.RefreshAsync(cancellationToken);

        return AdminWordSaveResult.Ok(category.Id);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await database.Categories.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (category is null)
        {
            return false;
        }

        // Связи в WordCategories уйдут каскадом; сами слова остаются в словаре.
        database.Categories.Remove(category);
        await database.SaveChangesAsync(cancellationToken);

        await searchIndex.RefreshAsync(cancellationToken);

        return true;
    }

    private async Task<bool> ExistsAsync(string name, int? existingId, CancellationToken cancellationToken)
    {
        var normalized = TextNormalizer.Normalize(name);

        return await database.Categories
            .AsNoTracking()
            .AnyAsync(
                category => category.NormalizedName == normalized && (existingId == null || category.Id != existingId),
                cancellationToken);
    }
}
