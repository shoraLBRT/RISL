namespace RISL.Infrastructure.Video;

/// <summary>Настройки перекодирования видео.</summary>
public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    /// <summary>Путь к ffmpeg. В образе Docker он лежит в PATH, локально может понадобиться полный путь.</summary>
    public string Path { get; set; } = "ffmpeg";

    /// <summary>Путь к ffprobe.</summary>
    public string ProbePath { get; set; } = "ffprobe";

    /// <summary>Ограничение ширины кадра. Жест читается и в 720p, а вес файла падает кратно.</summary>
    public int MaxWidth { get; set; } = 1280;

    /// <summary>
    /// Постоянное качество H.264: меньше — лучше и тяжелее. 24 даёт примерно 4–5 МБ
    /// на ролик в 25 секунд.
    /// </summary>
    public int Crf { get; set; } = 24;

    /// <summary>Компромисс между скоростью кодирования и размером файла.</summary>
    public string Preset { get; set; } = "medium";

    /// <summary>Секунда, с которой берётся кадр для постера.</summary>
    public double PosterSecond { get; set; } = 1;

    /// <summary>Потолок времени на одну операцию. Спасает от зависшего процесса.</summary>
    public int TimeoutSeconds { get; set; } = 600;
}
