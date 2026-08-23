using System.Collections.Frozen;
using RISL.Application.Abstractions;
using RISL.Domain;

namespace RISL.Application.Search;

/// <summary>
/// Поиск по словарю в памяти: перебор трёх тысяч записей укладывается в микросекунды,
/// поэтому никаких структур сложнее плоского массива здесь не нужно.
/// </summary>
/// <remarks>
/// Морфологии нет: запрос «кошками» не найдёт «кошка». Для словаря, где ищут начальную
/// форму, этого достаточно; при необходимости отсечение окончаний добавляется здесь же.
/// </remarks>
public sealed class WordSearchIndex : IWordSearchIndex
{
    private volatile Snapshot _snapshot = Snapshot.Empty;

    public int Count => _snapshot.Words.Length;

    public IReadOnlyList<CategoryView> Categories => _snapshot.Categories;

    public IReadOnlyList<char> Letters => _snapshot.Letters;

    public void Load(IReadOnlyList<WordSearchEntry> words, IReadOnlyList<CategoryView> categories)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(categories);

        var ordered = words.OrderBy(word => word.NormalizedText, StringComparer.Ordinal).ToArray();

        var letters = ordered
            .Select(word => word.IndexLetter)
            .Distinct()
            // Решётка собирает всё нецирилличное и должна стоять в конце указателя.
            .OrderBy(letter => letter == WordSearchEntry.OtherLetter ? 1 : 0)
            .ThenBy(letter => letter)
            .ToArray();

        var orderedCategories = categories
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name, StringComparer.CurrentCulture)
            .ToArray();

        _snapshot = new Snapshot(
            ordered,
            ordered.ToFrozenDictionary(word => word.Id),
            orderedCategories,
            orderedCategories.ToFrozenDictionary(category => category.Slug, StringComparer.OrdinalIgnoreCase),
            letters);
    }

    public WordSearchEntry? FindById(int id) =>
        _snapshot.WordsById.TryGetValue(id, out var word) ? word : null;

    public SearchResult Search(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var snapshot = _snapshot;
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var page = Math.Max(query.Page, 1);

        var categoryId = ResolveCategoryId(snapshot, query.CategorySlug);
        if (categoryId is null && !string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            // Категорию из адресной строки не опознали — честнее показать пустой список,
            // чем молча выдать весь словарь.
            return new SearchResult([], 0, page, pageSize);
        }

        var normalizedQuery = TextNormalizer.Normalize(query.Text);
        var tokens = normalizedQuery.Length == 0
            ? []
            : normalizedQuery.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

        var matches = new List<(int Rank, WordSearchEntry Word)>();

        foreach (var word in snapshot.Words)
        {
            if (query.Ids is { Count: > 0 } ids && !ids.Contains(word.Id))
            {
                continue;
            }

            if (categoryId is { } id && !word.CategoryIds.Contains(id))
            {
                continue;
            }

            if (query.Letter is { } letter && word.IndexLetter != letter)
            {
                continue;
            }

            var rank = Rank(word, normalizedQuery, tokens);
            if (rank is null)
            {
                continue;
            }

            matches.Add((rank.Value, word));
        }

        // Снимок уже отсортирован по алфавиту, а OrderBy устойчива,
        // поэтому внутри одного ранга порядок остаётся алфавитным.
        var ordered = normalizedQuery.Length == 0
            ? matches
            : [.. matches.OrderBy(match => match.Rank)];

        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(match => match.Word)
            .ToArray();

        return new SearchResult(items, ordered.Count, page, pageSize);
    }

    /// <summary>
    /// Чем меньше ранг, тем выше место в выдаче; <c>null</c> означает, что слово не подошло.
    /// Совпадение по самому слову всегда весит больше, чем совпадение по определению.
    /// </summary>
    private static int? Rank(WordSearchEntry word, string normalizedQuery, string[] tokens)
    {
        if (normalizedQuery.Length == 0)
        {
            return 0;
        }

        if (word.NormalizedText.Equals(normalizedQuery, StringComparison.Ordinal))
        {
            return 0;
        }

        if (word.NormalizedText.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            return 1;
        }

        if (word.NormalizedText.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 2;
        }

        // Дальше — многословные запросы, где порядок слов может не совпадать с текстом.
        if (tokens.Length > 1 && ContainsAll(word.NormalizedText, tokens))
        {
            return 3;
        }

        if (word.NormalizedDescription.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 4;
        }

        if (tokens.Length > 1 && ContainsAll(word.NormalizedDescription, tokens))
        {
            return 5;
        }

        return null;
    }

    private static bool ContainsAll(string haystack, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (!haystack.Contains(token, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int? ResolveCategoryId(Snapshot snapshot, string? categorySlug)
    {
        if (string.IsNullOrWhiteSpace(categorySlug))
        {
            return null;
        }

        return snapshot.CategoriesBySlug.TryGetValue(categorySlug.Trim(), out var category)
            ? category.Id
            : null;
    }

    private static readonly char[] SpaceSeparator = [' '];

    private sealed record Snapshot(
        WordSearchEntry[] Words,
        FrozenDictionary<int, WordSearchEntry> WordsById,
        CategoryView[] Categories,
        FrozenDictionary<string, CategoryView> CategoriesBySlug,
        char[] Letters)
    {
        public static readonly Snapshot Empty = new(
            [],
            FrozenDictionary<int, WordSearchEntry>.Empty,
            [],
            FrozenDictionary<string, CategoryView>.Empty,
            []);
    }
}
