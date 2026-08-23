namespace RISL.Domain;

/// <summary>Состояние задания массового импорта словаря.</summary>
public enum ImportJobStatus
{
    /// <summary>Файлы разобраны, отчёт показан, админ ещё не подтвердил применение.</summary>
    AwaitingConfirmation = 0,

    /// <summary>Записи применяются к базе.</summary>
    Applying = 1,

    /// <summary>Импорт завершён, видео могут ещё обрабатываться в фоне.</summary>
    Completed = 2,

    /// <summary>Импорт отменён админом до применения.</summary>
    Cancelled = 3,

    /// <summary>Импорт сорвался целиком, изменения откачены.</summary>
    Failed = 4,
}
