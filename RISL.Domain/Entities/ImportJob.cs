namespace RISL.Domain.Entities;

/// <summary>
/// Задание массового импорта. Живёт от разбора файлов до применения изменений,
/// чтобы админ успел прочитать отчёт и подтвердить или отменить операцию.
/// </summary>
public class ImportJob
{
    private ImportJob()
    {
        // Для EF Core.
    }

    public ImportJob(string fileName, string reportJson, int totalRows, int toCreate, int toUpdate, int failed)
    {
        FileName = fileName;
        ReportJson = reportJson;
        TotalRows = totalRows;
        ToCreate = toCreate;
        ToUpdate = toUpdate;
        Failed = failed;
        Status = ImportJobStatus.AwaitingConfirmation;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }

    /// <summary>Имя загруженного CSV — только для показа в списке заданий.</summary>
    public string FileName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public ImportJobStatus Status { get; private set; }

    public int TotalRows { get; private set; }

    public int ToCreate { get; private set; }

    public int ToUpdate { get; private set; }

    public int Failed { get; private set; }

    /// <summary>Построчный отчёт разбора в JSON — показывается до и после применения.</summary>
    public string ReportJson { get; private set; } = string.Empty;

    /// <summary>Каталог с распакованными видео, живёт до применения или отмены задания.</summary>
    public string? StagingDirectory { get; set; }

    public void MarkApplying() => Status = ImportJobStatus.Applying;

    public void MarkCompleted()
    {
        Status = ImportJobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled()
    {
        Status = ImportJobStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        Status = ImportJobStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
