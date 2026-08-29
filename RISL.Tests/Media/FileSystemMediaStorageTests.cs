using System.Text;
using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;
using RISL.Infrastructure.Media;

namespace RISL.Tests.Media;

public sealed class FileSystemMediaStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"risl-media-{Guid.NewGuid():N}");
    private readonly FileSystemMediaStorage _storage;

    public FileSystemMediaStorageTests()
    {
        _storage = new FileSystemMediaStorage(Options.Create(new MediaOptions { RootPath = _root }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Stream Content(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SaveAsync_СохраняетФайлИДелаетЕгоЧитаемым()
    {
        await _storage.SaveAsync(MediaArea.Videos, "a1.mp4", Content("данные"));

        Assert.True(_storage.Exists(MediaArea.Videos, "a1.mp4"));

        using var reader = new StreamReader(_storage.OpenRead(MediaArea.Videos, "a1.mp4"));
        Assert.Equal("данные", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task SaveAsync_ПерезаписываетСуществующийФайл()
    {
        await _storage.SaveAsync(MediaArea.Videos, "a1.mp4", Content("первая версия"));
        await _storage.SaveAsync(MediaArea.Videos, "a1.mp4", Content("вторая"));

        using var reader = new StreamReader(_storage.OpenRead(MediaArea.Videos, "a1.mp4"));
        Assert.Equal("вторая", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Delete_УдаляетФайлИНеПадаетНаПовторномВызове()
    {
        await _storage.SaveAsync(MediaArea.Posters, "a1.jpg", Content("кадр"));

        _storage.Delete(MediaArea.Posters, "a1.jpg");
        _storage.Delete(MediaArea.Posters, "a1.jpg");

        Assert.False(_storage.Exists(MediaArea.Posters, "a1.jpg"));
    }

    [Fact]
    public void РазныеОбластиХранятсяВРазныхКаталогах()
    {
        var video = _storage.GetPhysicalPath(MediaArea.Videos, "a1.mp4");
        var poster = _storage.GetPhysicalPath(MediaArea.Posters, "a1.mp4");

        Assert.NotEqual(video, poster);
        Assert.StartsWith(_root, video, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../secret.mp4")]
    [InlineData("../../etc/passwd")]
    [InlineData("sub/dir.mp4")]
    [InlineData("sub\\dir.mp4")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ИмяФайлаВыходящееЗаПределыХранилищаОтклоняется(string fileName)
    {
        // Имена приходят из формы загрузки и из архива импорта, то есть от пользователя.
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _storage.SaveAsync(MediaArea.Videos, fileName, Content("вредонос")));
    }

    [Fact]
    public void ExistsНеПадаетНаНедопустимомИмениАПростоОтвечаетНет()
    {
        Assert.False(_storage.Exists(MediaArea.Videos, "../../secret.mp4"));
    }

    [Fact]
    public async Task List_ВозвращаетТолькоФайлыПодходящиеПодШаблон()
    {
        // Уборка после прерванного перекодирования опирается именно на этот шаблон.
        await _storage.SaveAsync(MediaArea.Videos, "a1.mp4", Content("готовое"));
        await _storage.SaveAsync(MediaArea.Videos, "a2.mp4.part", Content("недописанное"));
        await _storage.SaveAsync(MediaArea.Videos, "a3.mp4.part", Content("недописанное"));

        var unfinished = _storage.List(MediaArea.Videos, "*.part");

        Assert.Equal(["a2.mp4.part", "a3.mp4.part"], unfinished.Order());
        Assert.Equal(3, _storage.List(MediaArea.Videos, "*").Count);
        Assert.Empty(_storage.List(MediaArea.Posters, "*"));
    }

    [Fact]
    public void GetPublicUrl_СтроитАдресВнутриПрефиксаРаздачи()
    {
        var url = _storage.GetPublicUrl(MediaArea.Videos, "a1.mp4");

        Assert.Equal("/media/videos/a1.mp4", url);
    }
}
