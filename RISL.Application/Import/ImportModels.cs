namespace RISL.Application.Import;

/// <summary>Что произойдёт со строкой файла при применении импорта.</summary>
public enum ImportAction
{
    /// <summary>Слова ещё нет в словаре — будет создано.</summary>
    Create = 0,

    /// <summary>Слово уже есть — описание, темы и видео будут обновлены.</summary>
    Update = 1,

    /// <summary>Строку применить нельзя, причина в сообщении.</summary>
    Error = 2,
}

/// <summary>Разобранная строка файла импорта до сверки с базой и архивом.</summary>
public sealed record ImportRow(
    int LineNumber,
    string Word,
    string Description,
    IReadOnlyList<string> Categories,
    string? VideoFileName);

/// <summary>Строка файла вместе с решением, принятым по ней планировщиком.</summary>
public sealed record ImportPlanItem(
    int LineNumber,
    string Word,
    string Description,
    IReadOnlyList<string> Categories,
    string? VideoFileName,
    ImportAction Action,
    string? Message)
{
    public bool IsApplicable => Action is ImportAction.Create or ImportAction.Update;
}

/// <summary>
/// Полный план импорта. Показывается админу до применения: ничего не пишется в базу,
/// пока он не увидит, сколько записей будет создано, сколько обновлено и что не так.
/// </summary>
public sealed record ImportPlan(IReadOnlyList<ImportPlanItem> Items)
{
    public static readonly ImportPlan Empty = new([]);

    public int TotalRows => Items.Count;

    public int ToCreate => Items.Count(item => item.Action == ImportAction.Create);

    public int ToUpdate => Items.Count(item => item.Action == ImportAction.Update);

    public int Failed => Items.Count(item => item.Action == ImportAction.Error);

    public bool HasApplicableRows => Items.Any(item => item.IsApplicable);
}
