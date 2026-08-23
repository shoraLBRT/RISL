namespace RISL.Application.Search;

/// <summary>Страница результатов поиска вместе с данными для пагинатора.</summary>
public sealed record SearchResult(
    IReadOnlyList<WordSearchEntry> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public static readonly SearchResult Empty = new([], 0, 1, SearchQuery.DefaultPageSize);

    public int TotalPages => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}
