namespace RISL.Application.Search;

/// <summary>
/// Снимок слова для поиска и показа. Держит всё, что нужно и списку, и странице слова,
/// поэтому гостевые запросы обслуживаются целиком из памяти, без обращений к базе.
/// </summary>
public sealed record WordSearchEntry(
    int Id,
    string Text,
    string NormalizedText,
    string Description,
    string NormalizedDescription,
    string Slug,
    string? VideoFileName,
    string? PosterFileName,
    double? VideoDurationSeconds,
    IReadOnlyList<int> CategoryIds)
{
    /// <summary>Буква алфавитного указателя. Всё, что не кириллица, сводится к решётке.</summary>
    public char IndexLetter { get; } = ResolveIndexLetter(NormalizedText);

    /// <summary>Символ, под которым группируются слова вне кириллического алфавита.</summary>
    public const char OtherLetter = '#';

    private static char ResolveIndexLetter(string normalizedText)
    {
        if (normalizedText.Length == 0)
        {
            return OtherLetter;
        }

        var first = normalizedText[0];
        return first is >= 'а' and <= 'я' ? first : OtherLetter;
    }
}
