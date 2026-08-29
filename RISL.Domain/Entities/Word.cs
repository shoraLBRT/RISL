namespace RISL.Domain.Entities;

/// <summary>
/// Статья словаря: слово, его определение и видеозапись жеста.
/// </summary>
/// <remarks>
/// Нормализованные варианты текста и слаг пересчитываются вместе с исходными
/// значениями, поэтому менять их напрямую нельзя — только через методы.
/// </remarks>
public class Word
{
    private readonly List<Category> _categories = [];

    private Word()
    {
        // Для EF Core.
    }

    public Word(string text, string description)
    {
        SetText(text);
        SetDescription(description);

        var now = DateTimeOffset.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
        IsPublished = true;
    }

    public int Id { get; private set; }

    /// <summary>Слово в том виде, в котором его показывают пользователю.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Ключ сравнения и поиска: нижний регистр, «ё» сведена к «е».</summary>
    public string NormalizedText { get; private set; } = string.Empty;

    /// <summary>Определение слова.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Нормализованное определение — по нему ищут во вторую очередь.</summary>
    public string NormalizedDescription { get; private set; } = string.Empty;

    /// <summary>Декоративная часть URL. Авторитетным идентификатором остаётся <see cref="Id"/>.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Имя готового файла вида {guid}.mp4 без пути.</summary>
    public string? VideoFileName { get; private set; }

    /// <summary>Имя кадра-заставки вида {guid}.jpg без пути.</summary>
    public string? PosterFileName { get; private set; }

    /// <summary>
    /// Имя исходника, ожидающего перекодирования. Хранится в базе, чтобы после
    /// перезапуска сервера незавершённые задания можно было поставить в очередь заново.
    /// </summary>
    public string? IncomingVideoFileName { get; private set; }

    public double? VideoDurationSeconds { get; private set; }

    public VideoStatus VideoStatus { get; private set; } = VideoStatus.None;

    /// <summary>Текст ошибки последней неудачной обработки — показывается только админу.</summary>
    public string? VideoError { get; private set; }

    public bool IsPublished { get; set; }

    /// <summary>
    /// Просмотры дописываются фоновым сбросом напрямую в SQL, минуя загрузку сущности:
    /// открытие страницы слова не должно порождать запись в базу.
    /// </summary>
    public int ViewCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Category> Categories => _categories;

    /// <summary>
    /// Гость видит слово, только когда оно опубликовано и видео действительно готово.
    /// Так карточка с провалившейся обработкой не попадёт в выдачу.
    /// </summary>
    public bool IsVisibleToGuests => IsPublished && VideoStatus == VideoStatus.Ready;

    public void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Слово не может быть пустым.", nameof(text));
        }

        Text = text.Trim();
        NormalizedText = TextNormalizer.Normalize(Text);
        Slug = TextNormalizer.Slugify(Text);
        Touch();
    }

    public void SetDescription(string? description)
    {
        Description = description?.Trim() ?? string.Empty;
        NormalizedDescription = TextNormalizer.Normalize(Description);
        Touch();
    }

    /// <summary>Исходник загружен и поставлен в очередь на перекодирование.</summary>
    public void MarkVideoPending(string incomingFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incomingFileName);

        IncomingVideoFileName = incomingFileName;
        VideoStatus = VideoStatus.Pending;
        VideoError = null;
        Touch();
    }

    public void MarkVideoProcessing()
    {
        VideoStatus = VideoStatus.Processing;
        VideoError = null;
        Touch();
    }

    public void MarkVideoReady(string videoFileName, string posterFileName, double durationSeconds)
    {
        VideoFileName = videoFileName;
        PosterFileName = posterFileName;
        VideoDurationSeconds = durationSeconds;
        VideoStatus = VideoStatus.Ready;
        VideoError = null;
        IncomingVideoFileName = null;
        Touch();
    }

    /// <summary>
    /// Обработка сорвалась. Имя исходника сохраняется: админ сможет повторить попытку,
    /// не загружая файл заново.
    /// </summary>
    public void MarkVideoFailed(string error)
    {
        VideoStatus = VideoStatus.Failed;
        VideoError = error;
        Touch();
    }

    /// <summary>
    /// Отвязывает видео от слова. Сами файлы удаляет вызывающая сторона —
    /// домен о файловой системе ничего не знает.
    /// </summary>
    public void DetachVideo()
    {
        VideoFileName = null;
        PosterFileName = null;
        VideoDurationSeconds = null;
        VideoStatus = VideoStatus.None;
        VideoError = null;
        IncomingVideoFileName = null;
        Touch();
    }

    public void ReplaceCategories(IEnumerable<Category> categories)
    {
        _categories.Clear();

        // Отсеиваем повторы по названию, а не по Id: при импорте темы создаются
        // на лету и до сохранения у всех них Id равен нулю.
        _categories.AddRange(categories.DistinctBy(category => category.NormalizedName, StringComparer.Ordinal));
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
