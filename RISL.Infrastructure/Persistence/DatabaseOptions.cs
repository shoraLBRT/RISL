namespace RISL.Infrastructure.Persistence;

/// <summary>Настройки базы данных и её первичного наполнения.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Путь к файлу SQLite. Каталог создаётся при старте, если его нет.</summary>
    public string Path { get; set; } = "data/risl.db";

    /// <summary>
    /// Применять миграции при старте. Инстанс один, поэтому гонки исключены,
    /// а из деплоя уходит ручной шаг.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>
    /// Наполнить пустую базу примерами. Только для локальной разработки —
    /// в продакшене должно быть выключено.
    /// </summary>
    public bool SeedSampleData { get; set; }
}
