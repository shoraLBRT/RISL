using RISL.Domain;

namespace RISL.Application.Import;

/// <summary>
/// Сверяет разобранные строки с тем, что уже есть в словаре и в архиве с видео,
/// и решает по каждой: создать, обновить или забраковать.
/// </summary>
/// <remarks>
/// Планировщик намеренно ничего не знает ни о базе, ни о файловой системе — он
/// получает готовые множества. Благодаря этому весь разбор правил проверяется тестами
/// без поднятия инфраструктуры.
/// </remarks>
public static class ImportPlanner
{
    /// <param name="existingNormalizedWords">Нормализованные слова, уже имеющиеся в словаре.</param>
    /// <param name="availableVideoFiles">
    /// Имена файлов из загруженного архива. Пустое множество означает импорт без архива —
    /// тогда ссылки на видео считаются ненайденными.
    /// </param>
    public static ImportPlan Plan(
        ImportReadResult readResult,
        IReadOnlySet<string> existingNormalizedWords,
        IReadOnlySet<string> availableVideoFiles)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(existingNormalizedWords);
        ArgumentNullException.ThrowIfNull(availableVideoFiles);

        if (readResult.IsFatal)
        {
            return ImportPlan.Empty;
        }

        var items = new List<ImportPlanItem>(readResult.Rows.Count + readResult.Errors.Count);

        foreach (var row in readResult.Rows)
        {
            items.Add(PlanRow(row, existingNormalizedWords, availableVideoFiles));
        }

        items.AddRange(readResult.Errors);

        return new ImportPlan([.. items.OrderBy(item => item.LineNumber)]);
    }

    private static ImportPlanItem PlanRow(
        ImportRow row,
        IReadOnlySet<string> existingNormalizedWords,
        IReadOnlySet<string> availableVideoFiles)
    {
        if (row.VideoFileName is { } videoFileName && !availableVideoFiles.Contains(videoFileName))
        {
            // Строку с потерянным видео проще забраковать целиком: иначе в словаре
            // появится скрытая от гостей карточка, о которой все забудут.
            return row.ToItem(ImportAction.Error, $"В архиве нет файла «{videoFileName}».");
        }

        var normalized = TextNormalizer.Normalize(row.Word);
        var action = existingNormalizedWords.Contains(normalized) ? ImportAction.Update : ImportAction.Create;

        var message = action == ImportAction.Update
            ? "Слово уже есть в словаре, запись будет обновлена."
            : null;

        return row.ToItem(action, message);
    }

    private static ImportPlanItem ToItem(this ImportRow row, ImportAction action, string? message) =>
        new(row.LineNumber, row.Word, row.Description, row.Categories, row.VideoFileName, action, message);
}
