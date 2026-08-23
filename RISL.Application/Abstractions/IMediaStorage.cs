namespace RISL.Application.Abstractions;

/// <summary>
/// Доступ к файлам видео и постеров. Единственная точка, знающая, где физически
/// лежат медиафайлы: переезд на S3/MinIO/CDN — это ещё одна реализация интерфейса,
/// а не правка вызывающего кода.
/// </summary>
public interface IMediaStorage
{
    /// <summary>Сохраняет поток в указанную область под указанным именем файла.</summary>
    /// <param name="area">Область хранения, см. <see cref="MediaArea"/>.</param>
    /// <param name="fileName">Имя файла без пути. Имена, содержащие разделители пути, отклоняются.</param>
    Task SaveAsync(MediaArea area, string fileName, Stream content, CancellationToken cancellationToken = default);

    Stream OpenRead(MediaArea area, string fileName);

    bool Exists(MediaArea area, string fileName);

    void Delete(MediaArea area, string fileName);

    /// <summary>
    /// Полный путь к файлу — нужен ffmpeg, который работает с файловой системой напрямую.
    /// Реализация для объектного хранилища обязана выбрасывать <see cref="NotSupportedException"/>.
    /// </summary>
    string GetPhysicalPath(MediaArea area, string fileName);

    /// <summary>URL, по которому браузер заберёт файл.</summary>
    string GetPublicUrl(MediaArea area, string fileName);
}
