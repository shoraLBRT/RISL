using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;

namespace RISL.Infrastructure.Media;

/// <summary>
/// Хранение медиафайлов на диске сервера. Материалов немного и аудитория невелика,
/// поэтому отдельное объектное хранилище не окупается; при переезде на S3/CDN
/// достаточно подменить реализацию <see cref="IMediaStorage"/>.
/// </summary>
public sealed class FileSystemMediaStorage : IMediaStorage
{
    private readonly MediaOptions _options;
    private readonly string _root;

    public FileSystemMediaStorage(IOptions<MediaOptions> options)
    {
        _options = options.Value;
        _root = Path.GetFullPath(_options.RootPath);

        foreach (var area in Enum.GetValues<MediaArea>())
        {
            Directory.CreateDirectory(Path.Combine(_root, FolderOf(area)));
        }
    }

    public async Task SaveAsync(MediaArea area, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = ResolvePath(area, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await content.CopyToAsync(target, cancellationToken);
    }

    public Stream OpenRead(MediaArea area, string fileName) =>
        new FileStream(ResolvePath(area, fileName), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

    public bool Exists(MediaArea area, string fileName)
    {
        try
        {
            return File.Exists(ResolvePath(area, fileName));
        }
        catch (ArgumentException)
        {
            // Недопустимое имя — это заведомо отсутствующий файл, а не повод падать.
            return false;
        }
    }

    public IReadOnlyList<string> List(MediaArea area, string searchPattern)
    {
        var directory = GetAreaRoot(area);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(directory, searchPattern).Select(Path.GetFileName).OfType<string>()];
    }

    public void Delete(MediaArea area, string fileName)
    {
        var path = ResolvePath(area, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public string GetPhysicalPath(MediaArea area, string fileName) => ResolvePath(area, fileName);

    public string GetPublicUrl(MediaArea area, string fileName) =>
        $"{_options.PublicPathPrefix.TrimEnd('/')}/{FolderOf(area)}/{Uri.EscapeDataString(ValidateFileName(fileName))}";

    /// <summary>Каталог области внутри корня хранилища. Совпадает с сегментом публичного URL.</summary>
    public static string FolderOf(MediaArea area) => area switch
    {
        MediaArea.Incoming => "incoming",
        MediaArea.Originals => "originals",
        MediaArea.Videos => "videos",
        MediaArea.Posters => "posters",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Неизвестная область хранилища."),
    };

    /// <summary>Корневой каталог области — нужен раздаче статики.</summary>
    public string GetAreaRoot(MediaArea area) => Path.Combine(_root, FolderOf(area));

    private string ResolvePath(MediaArea area, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(_root, FolderOf(area), ValidateFileName(fileName)));

        // Ремень поверх подтяжек: даже если проверка имени когда-нибудь ослабнет,
        // за пределы корня хранилища запись не уйдёт.
        if (!path.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Путь «{fileName}» выходит за пределы хранилища.", nameof(fileName));
        }

        return path;
    }

    /// <summary>
    /// Имена файлов приходят из формы загрузки и из архива импорта, то есть от пользователя.
    /// Разделители пути и переходы вверх отсекаются здесь, а не в вызывающем коде.
    /// </summary>
    private static string ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Имя файла «{fileName}» содержит недопустимые символы.", nameof(fileName));
        }

        if (Path.IsPathRooted(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Имя файла «{fileName}» содержит недопустимые символы.", nameof(fileName));
        }

        return fileName;
    }
}
