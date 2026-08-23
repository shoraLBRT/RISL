namespace RISL.Application.Abstractions;

/// <summary>Раздел хранилища медиафайлов.</summary>
public enum MediaArea
{
    /// <summary>Незавершённые загрузки и распакованные из архива исходники.</summary>
    Incoming,

    /// <summary>Исходники как есть — позволяют перекодировать словарь заново без пересъёмки.</summary>
    Originals,

    /// <summary>Готовые к раздаче mp4.</summary>
    Videos,

    /// <summary>Кадры-заставки к готовым видео.</summary>
    Posters,
}
