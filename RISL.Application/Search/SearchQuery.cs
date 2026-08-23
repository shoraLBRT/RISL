namespace RISL.Application.Search;

/// <summary>Параметры выборки слов для главной страницы.</summary>
public sealed record SearchQuery
{
    public const int DefaultPageSize = 60;

    /// <summary>Строка поиска в том виде, в каком её ввёл пользователь.</summary>
    public string? Text { get; init; }

    /// <summary>Слаг категории; пусто — без фильтра по теме.</summary>
    public string? CategorySlug { get; init; }

    /// <summary>Буква алфавитного указателя; пусто — без фильтра по букве.</summary>
    public char? Letter { get; init; }

    /// <summary>Ограничение выборки конкретными словами — используется страницей избранного.</summary>
    public IReadOnlyCollection<int>? Ids { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}
