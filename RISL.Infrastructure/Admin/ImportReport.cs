using System.Text.Json;
using RISL.Application.Import;

namespace RISL.Infrastructure.Admin;

/// <summary>
/// Содержимое отчёта задания импорта, как оно лежит в базе.
/// </summary>
/// <param name="Items">Построчный план: что будет создано, что обновлено, что забраковано.</param>
/// <param name="Files">
/// Соответствие «имя файла из CSV — имя, под которым видео сохранено в хранилище».
/// Из архива берутся только имена файлов, пути игнорируются, а на диск всё
/// кладётся под сгенерированными именами — так архив не сможет записать ничего лишнего.
/// </param>
/// <param name="Error">Причина, по которой файл не удалось разобрать вовсе.</param>
internal sealed record ImportReport(
    IReadOnlyList<ImportPlanItem> Items,
    IReadOnlyDictionary<string, string> Files,
    string? Error)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static ImportReport FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ImportReport>(json, SerializerOptions)
                ?? new ImportReport([], new Dictionary<string, string>(), "Отчёт повреждён.");
        }
        catch (JsonException)
        {
            return new ImportReport([], new Dictionary<string, string>(), "Отчёт повреждён.");
        }
    }
}
