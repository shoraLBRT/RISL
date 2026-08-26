using RISL.Domain;

namespace RISL.Application.Import;

/// <summary>Файлы, из которых собирается задание импорта.</summary>
/// <param name="FileName">Имя загруженного CSV — показывается в списке заданий.</param>
/// <param name="CsvContent">Содержимое CSV, уже приведённое к строке.</param>
/// <param name="VideoArchive">Архив с видео. Пусто — импорт только текстовых данных.</param>
public sealed record ImportSource(string FileName, string CsvContent, Stream? VideoArchive);

/// <summary>Задание импорта в том виде, в котором его показывают админу.</summary>
public sealed record ImportJobView(
    int Id,
    string FileName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    ImportJobStatus Status,
    int TotalRows,
    int ToCreate,
    int ToUpdate,
    int Failed,
    IReadOnlyList<ImportPlanItem> Items,
    string? Error)
{
    public bool CanApply => Status == ImportJobStatus.AwaitingConfirmation && (ToCreate + ToUpdate) > 0;

    public bool IsFinished => Status is ImportJobStatus.Completed or ImportJobStatus.Cancelled or ImportJobStatus.Failed;
}
