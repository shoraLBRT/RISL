using RISL.Domain;

namespace RISL.Application.Import;

/// <summary>Результат разбора файла: строки, пригодные к планированию, и ошибки формата.</summary>
public sealed record ImportReadResult(
    IReadOnlyList<ImportRow> Rows,
    IReadOnlyList<ImportPlanItem> Errors,
    string? FatalError)
{
    public bool IsFatal => FatalError is not null;
}

/// <summary>
/// Превращает содержимое CSV в строки импорта. Заголовки распознаются и по-русски,
/// и по-английски — заказчику удобнее готовить файл в привычных терминах.
/// </summary>
public static class ImportCsvReader
{
    private static readonly string[] WordHeaders = ["word", "слово"];
    private static readonly string[] DescriptionHeaders = ["description", "описание", "определение"];
    private static readonly string[] CategoryHeaders = ["categories", "category", "категории", "категория", "темы", "тема"];
    private static readonly string[] VideoHeaders = ["video", "videofile", "видео", "видеофайл", "файл"];

    private static readonly char[] CategorySeparators = ['|', ','];

    public static ImportReadResult Read(string content)
    {
        var delimiter = DelimitedTextParser.DetectDelimiter(content);
        var rows = DelimitedTextParser.Parse(content, delimiter);

        if (rows.Count == 0)
        {
            return new ImportReadResult([], [], "Файл пуст.");
        }

        var header = rows[0];
        var wordColumn = FindColumn(header, WordHeaders);
        if (wordColumn < 0)
        {
            return new ImportReadResult([], [], "В первой строке файла не найдена колонка со словом (ожидается «слово» или «word»).");
        }

        var descriptionColumn = FindColumn(header, DescriptionHeaders);
        var categoryColumn = FindColumn(header, CategoryHeaders);
        var videoColumn = FindColumn(header, VideoHeaders);

        var parsed = new List<ImportRow>(rows.Count - 1);
        var errors = new List<ImportPlanItem>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows.Skip(1))
        {
            var word = ValueAt(row, wordColumn);

            if (string.IsNullOrWhiteSpace(word))
            {
                errors.Add(Error(row.LineNumber, string.Empty, "Пустое слово."));
                continue;
            }

            var normalized = TextNormalizer.Normalize(word);
            if (seen.TryGetValue(normalized, out var firstLine))
            {
                errors.Add(Error(row.LineNumber, word, $"Слово уже встречалось в этом файле в строке {firstLine}."));
                continue;
            }

            seen[normalized] = row.LineNumber;

            parsed.Add(new ImportRow(
                row.LineNumber,
                word,
                ValueAt(row, descriptionColumn),
                SplitCategories(ValueAt(row, categoryColumn)),
                NullIfEmpty(ValueAt(row, videoColumn))));
        }

        return new ImportReadResult(parsed, errors, FatalError: null);
    }

    private static ImportPlanItem Error(int lineNumber, string word, string message) =>
        new(lineNumber, word, string.Empty, [], null, ImportAction.Error, message);

    private static IReadOnlyList<string> SplitCategories(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value
                .Split(CategorySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .DistinctBy(TextNormalizer.Normalize)];

    private static string ValueAt(CsvRow row, int column) =>
        column >= 0 && column < row.Fields.Count ? row.Fields[column] : string.Empty;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int FindColumn(CsvRow header, string[] names)
    {
        for (var index = 0; index < header.Fields.Count; index++)
        {
            var candidate = TextNormalizer.Normalize(header.Fields[index]);
            if (names.Contains(candidate, StringComparer.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
