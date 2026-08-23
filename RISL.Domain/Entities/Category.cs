namespace RISL.Domain.Entities;

/// <summary>Тема, по которой можно отфильтровать словарь: «Еда», «Семья», «Цифры».</summary>
public class Category
{
    private readonly List<Word> _words = [];

    private Category()
    {
        // Для EF Core.
    }

    public Category(string name, int sortOrder = 0)
    {
        SetName(name);
        SortOrder = sortOrder;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Ключ сопоставления при импорте, чтобы «еда» и «Еда» не разошлись в две темы.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    /// <summary>Порядок в фильтре; при равенстве сортируем по имени.</summary>
    public int SortOrder { get; set; }

    public IReadOnlyCollection<Word> Words => _words;

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Название категории не может быть пустым.", nameof(name));
        }

        Name = name.Trim();
        NormalizedName = TextNormalizer.Normalize(Name);
        Slug = TextNormalizer.Slugify(Name);
    }
}
