namespace RISL.Application.Search;

/// <param name="WordCount">Сколько видимых гостю слов относится к теме — показывается в фильтре.</param>
public sealed record CategoryView(int Id, string Name, string Slug, int SortOrder, int WordCount);
