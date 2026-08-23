namespace RISL.Domain;

/// <summary>
/// Состояние видеозаписи слова в конвейере обработки.
/// Публично слово показывается только в состоянии <see cref="Ready"/>.
/// </summary>
public enum VideoStatus
{
    /// <summary>Видео к слову ещё не приложено.</summary>
    None = 0,

    /// <summary>Исходник загружен и ждёт своей очереди на перекодирование.</summary>
    Pending = 1,

    /// <summary>Ffmpeg обрабатывает исходник прямо сейчас.</summary>
    Processing = 2,

    /// <summary>Видео и постер готовы, слово можно публиковать.</summary>
    Ready = 3,

    /// <summary>Обработка сорвалась, причина лежит в <see cref="Entities.Word.VideoError"/>.</summary>
    Failed = 4,
}
