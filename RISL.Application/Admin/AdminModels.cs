using RISL.Domain;

namespace RISL.Application.Admin;

/// <summary>Колонка, по которой отсортирован список слов в панели.</summary>
public enum AdminWordSort
{
    Word = 0,
    Updated = 1,
    Views = 2,
    Status = 3,
}

/// <summary>Быстрый фильтр списка слов.</summary>
public enum AdminWordFilter
{
    All = 0,

    /// <summary>Опубликованные и с готовым видео — ровно то, что видит гость.</summary>
    Visible = 1,

    /// <summary>Слова, до которых гость не доберётся: черновики и незавершённое видео.</summary>
    Hidden = 2,

    /// <summary>Обработка видео сорвалась — требуют внимания в первую очередь.</summary>
    Failed = 3,

    /// <summary>Видео ещё не приложено.</summary>
    WithoutVideo = 4,
}

/// <summary>Параметры выборки списка слов в панели.</summary>
public sealed record AdminWordQuery
{
    public const int DefaultPageSize = 50;

    public string? Search { get; init; }

    public AdminWordFilter Filter { get; init; } = AdminWordFilter.All;

    public AdminWordSort Sort { get; init; } = AdminWordSort.Updated;

    public bool Descending { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record AdminWordListItem(
    int Id,
    string Text,
    string Description,
    VideoStatus VideoStatus,
    bool IsPublished,
    int ViewCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Categories);

public sealed record AdminWordPage(
    IReadOnlyList<AdminWordListItem> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
}

public sealed record AdminWordDetails(
    int Id,
    string Text,
    string Description,
    bool IsPublished,
    VideoStatus VideoStatus,
    string? VideoError,
    string? VideoUrl,
    string? PosterUrl,
    double? VideoDurationSeconds,
    bool HasIncomingSource,
    IReadOnlyList<int> CategoryIds);

/// <summary>Данные формы редактирования слова.</summary>
/// <param name="IncomingVideoFileName">
/// Имя уже сохранённого в хранилище исходника. Пусто — видео не меняли.
/// </param>
public sealed record AdminWordForm(
    string Text,
    string? Description,
    bool IsPublished,
    IReadOnlyList<int> CategoryIds,
    string? IncomingVideoFileName);

/// <summary>Итог сохранения: либо идентификатор записи, либо причина отказа.</summary>
public sealed record AdminWordSaveResult(bool Success, int Id, string? Error)
{
    public static AdminWordSaveResult Ok(int id) => new(true, id, null);

    public static AdminWordSaveResult Fail(string error) => new(false, 0, error);
}

public sealed record AdminCategory(int Id, string Name, string Slug, int SortOrder, int WordCount);

public sealed record AdminFeedback(
    int Id,
    string? Name,
    string? Contact,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsHandled);

/// <summary>Сводка на главной странице панели.</summary>
public sealed record AdminDashboard(
    int TotalWords,
    int VisibleWords,
    int WithoutVideo,
    int Processing,
    int Failed,
    int PendingFeedback);
