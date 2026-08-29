using System.Globalization;
using System.Text;

namespace RISL.Domain;

/// <summary>
/// Приведение текста к форме, по которой ведётся поиск и сравнение слов,
/// и построение человекочитаемых URL-слагов.
/// </summary>
/// <remarks>
/// Нормализация выполняется один раз при сохранении и складывается в базу рядом
/// с исходным текстом, поэтому поиск не тратит время на разбор каждой строки.
/// Морфологии здесь нет: «кошками» не сведётся к «кошка».
/// </remarks>
public static class TextNormalizer
{
    private const int MaxSlugLength = 80;

    /// <summary>
    /// Нижний регистр, «ё» к «е», обрезка краёв и схлопывание любых пробельных
    /// последовательностей в один пробел.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var symbol in value)
        {
            if (char.IsWhiteSpace(symbol))
            {
                // Пробел добавляем не сразу, иначе на хвосте строки останется лишний.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            var lowered = char.ToLowerInvariant(symbol);
            builder.Append(lowered switch
            {
                'ё' => 'е',
                _ => lowered,
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Строит слаг для URL: транслитерация кириллицы, всё лишнее — в дефис.
    /// Слаг декоративен, авторитетным идентификатором остаётся Id.
    /// </summary>
    public static string Slugify(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        var pendingDash = false;

        foreach (var symbol in normalized)
        {
            var replacement = Transliterate(symbol);

            // null — символ разделяет слова (пробел, знак препинания);
            // пустая строка — символ исчезает бесследно, как твёрдый знак в «подъезде».
            if (replacement is null)
            {
                pendingDash = builder.Length > 0;
                continue;
            }

            if (replacement.Length == 0)
            {
                continue;
            }

            if (pendingDash)
            {
                builder.Append('-');
                pendingDash = false;
            }

            builder.Append(replacement);

            if (builder.Length >= MaxSlugLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string? Transliterate(char symbol) => symbol switch
    {
        'а' => "a",
        'б' => "b",
        'в' => "v",
        'г' => "g",
        'д' => "d",
        'е' => "e",
        'ж' => "zh",
        'з' => "z",
        'и' => "i",
        'й' => "y",
        'к' => "k",
        'л' => "l",
        'м' => "m",
        'н' => "n",
        'о' => "o",
        'п' => "p",
        'р' => "r",
        'с' => "s",
        'т' => "t",
        'у' => "u",
        'ф' => "f",
        'х' => "h",
        'ц' => "ts",
        'ч' => "ch",
        'ш' => "sh",
        'щ' => "sch",
        'ъ' => string.Empty,
        'ы' => "y",
        'ь' => string.Empty,
        'э' => "e",
        'ю' => "yu",
        'я' => "ya",
        _ when symbol is >= 'a' and <= 'z' => symbol.ToString(),
        _ when symbol is >= '0' and <= '9' => symbol.ToString(),
        // Латиница с диакритикой встречается в заимствованиях: снимаем надстрочные знаки.
        _ when char.GetUnicodeCategory(symbol) is UnicodeCategory.LowercaseLetter => StripDiacritics(symbol),
        _ => null,
    };

    /// <summary>
    /// Снимает надстрочные знаки с латиницы. Если от символа не осталось латинской
    /// основы (иероглиф, греческая буква), он работает как разделитель.
    /// </summary>
    private static string? StripDiacritics(char symbol)
    {
        var decomposed = symbol.ToString().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var part in decomposed)
        {
            if (char.GetUnicodeCategory(part) is not UnicodeCategory.NonSpacingMark && part is >= 'a' and <= 'z')
            {
                builder.Append(part);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
