namespace RISL.Infrastructure.Media;

/// <summary>Настройки хранилища медиафайлов.</summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>
    /// Корень хранилища. Лежит вне wwwroot: иначе загруженное админом затиралось бы
    /// при каждой публикации приложения.
    /// </summary>
    public string RootPath { get; set; } = "data/media";

    /// <summary>
    /// Хранить ли исходники как есть. Они позволяют перекодировать словарь заново
    /// без пересъёмки, но три тысячи роликов с телефона — это сотня гигабайт.
    /// </summary>
    public bool KeepOriginals { get; set; } = true;

    /// <summary>Потолок размера одного загружаемого файла. По умолчанию 512 МБ.</summary>
    public long MaxUploadBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Префикс URL, по которому раздаются готовые файлы.</summary>
    public string PublicPathPrefix { get; set; } = "/media";
}
