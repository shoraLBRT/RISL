using RISL.Application.Search;

namespace RISL.Application.Abstractions;

/// <summary>
/// Снимок публичной части словаря в памяти. Три тысячи статей занимают считанные
/// мегабайты, поэтому гостевые страницы обслуживаются без обращений к базе.
/// </summary>
/// <remarks>
/// Снимок неизменяем: <see cref="Load"/> целиком подменяет ссылку, поэтому читателям
/// не нужны блокировки и запрос никогда не видит полуобновлённое состояние.
/// </remarks>
public interface IWordSearchIndex
{
    /// <summary>Сколько слов доступно гостям.</summary>
    int Count { get; }

    /// <summary>Темы с числом слов в каждой, в порядке показа.</summary>
    IReadOnlyList<CategoryView> Categories { get; }

    /// <summary>Буквы, за которыми реально стоят слова, — остальные в указателе не показываем.</summary>
    IReadOnlyList<char> Letters { get; }

    SearchResult Search(SearchQuery query);

    WordSearchEntry? FindById(int id);

    /// <summary>Заменяет снимок целиком. Вызывается при старте и после каждой записи в админке.</summary>
    void Load(IReadOnlyList<WordSearchEntry> words, IReadOnlyList<CategoryView> categories);
}
