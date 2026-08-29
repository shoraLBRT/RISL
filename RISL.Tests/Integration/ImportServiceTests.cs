using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RISL.Application.Abstractions;
using RISL.Application.Admin;
using RISL.Application.Import;
using RISL.Domain;

namespace RISL.Tests.Integration;

public sealed class ImportServiceTests : IAsyncLifetime
{
    private DictionaryFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new DictionaryFixture();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private static Stream BuildArchive(params string[] fileNames)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var fileName in fileNames)
            {
                var entry = archive.CreateEntry(fileName);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes($"содержимое {fileName}"));
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    private Task<int> PrepareAsync(string csv, Stream? archive = null) =>
        _fixture.InScopeAsync(services => services
            .GetRequiredService<IImportService>()
            .PrepareAsync(new ImportSource("dictionary.csv", csv, archive)));

    private Task<ImportJobView?> ApplyAsync(int jobId) =>
        _fixture.InScopeAsync(services => services.GetRequiredService<IImportService>().ApplyAsync(jobId));

    private Task<ImportJobView?> GetAsync(int jobId) =>
        _fixture.InScopeAsync(services => services.GetRequiredService<IImportService>().GetAsync(jobId));

    private Task<AdminWordPage> ListWordsAsync() =>
        _fixture.InScopeAsync(services => services
            .GetRequiredService<IWordAdminService>()
            .ListAsync(new AdminWordQuery { Sort = AdminWordSort.Word, Descending = false }));

    [Fact]
    public async Task Подготовка_НичегоНеПишетВСловарьДоПодтверждения()
    {
        await PrepareAsync("слово;описание\nкошка;животное");

        var words = await ListWordsAsync();

        Assert.Equal(0, words.TotalCount);
    }

    [Fact]
    public async Task Применение_СоздаётСловаИТемыИзФайла()
    {
        var jobId = await PrepareAsync("слово;описание;категории\nяблоко;фрукт;Еда|Природа\nмолоток;инструмент;Инструменты");

        var job = await ApplyAsync(jobId);

        Assert.NotNull(job);
        Assert.Equal(ImportJobStatus.Completed, job.Status);

        var words = await ListWordsAsync();
        Assert.Equal(2, words.TotalCount);

        var apple = words.Items.Single(item => item.Text == "яблоко");
        Assert.Equal(["Еда", "Природа"], apple.Categories.Order());

        // Темы, которых не было в словаре, создаются на лету.
        var categories = await _fixture.InScopeAsync(services =>
            services.GetRequiredService<ICategoryAdminService>().ListAsync());
        Assert.Equal(3, categories.Count);
    }

    [Fact]
    public async Task ПовторныйИмпортТогоЖеФайлаОбновляетАНеДублирует()
    {
        const string csv = "слово;описание\nкошка;животное\nсобака;животное";

        await ApplyAsync(await PrepareAsync(csv));
        var second = await ApplyAsync(await PrepareAsync("слово;описание\nкошка;домашнее животное\nсобака;животное"));

        Assert.NotNull(second);
        Assert.Equal(0, second.ToCreate);
        Assert.Equal(2, second.ToUpdate);

        var words = await ListWordsAsync();
        Assert.Equal(2, words.TotalCount);
        Assert.Equal("домашнее животное", words.Items.Single(item => item.Text == "кошка").Description);
    }

    [Fact]
    public async Task Применение_РаспаковываетВидеоИСтавитЕгоВОчередь()
    {
        using var archive = BuildArchive("koshka.mp4", "sobaka.mp4");
        var jobId = await PrepareAsync("слово;видео\nкошка;koshka.mp4\nсобака;sobaka.mp4", archive);

        await ApplyAsync(jobId);

        Assert.Equal(2, _fixture.Queue.Requests.Count);

        foreach (var request in _fixture.Queue.Requests)
        {
            // Имя из архива не переносится на диск: файл сохраняется под сгенерированным.
            Assert.DoesNotContain("koshka", request.IncomingFileName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sobaka", request.IncomingFileName, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".mp4", request.IncomingFileName, StringComparison.Ordinal);
            Assert.True(_fixture.Storage.Exists(MediaArea.Incoming, request.IncomingFileName));
        }
    }

    [Fact]
    public async Task Архив_СПутёмВверхНеВыходитЗаПределыХранилища()
    {
        // Классическая атака zip slip: имя записи пытается увести файл из каталога.
        using var archive = BuildArchive("../../../evil.mp4");

        var jobId = await PrepareAsync("слово;видео\nкошка;evil.mp4", archive);
        await ApplyAsync(jobId);

        var request = Assert.Single(_fixture.Queue.Requests);
        Assert.DoesNotContain("..", request.IncomingFileName, StringComparison.Ordinal);
        Assert.True(_fixture.Storage.Exists(MediaArea.Incoming, request.IncomingFileName));
    }

    [Fact]
    public async Task СтрокаСПотеряннымВидеоНеПопадаетВСловарь()
    {
        using var archive = BuildArchive("koshka.mp4");
        var jobId = await PrepareAsync("слово;видео\nкошка;koshka.mp4\nсобака;sobaka.mp4", archive);

        var job = await ApplyAsync(jobId);

        Assert.NotNull(job);
        Assert.Equal(1, job.Failed);

        var words = await ListWordsAsync();
        Assert.Equal(["кошка"], words.Items.Select(item => item.Text));
    }

    [Fact]
    public async Task Отмена_УдаляетРаспакованныеФайлыИНеТрогаетСловарь()
    {
        using var archive = BuildArchive("koshka.mp4");
        var jobId = await PrepareAsync("слово;видео\nкошка;koshka.mp4", archive);

        var extracted = Directory.GetFiles(Path.GetDirectoryName(
            _fixture.Storage.GetPhysicalPath(MediaArea.Incoming, "probe.mp4"))!);
        Assert.Single(extracted);

        var cancelled = await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IImportService>().CancelAsync(jobId));

        Assert.True(cancelled);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(
            _fixture.Storage.GetPhysicalPath(MediaArea.Incoming, "probe.mp4"))!));

        var job = await GetAsync(jobId);
        Assert.Equal(ImportJobStatus.Cancelled, job!.Status);
        Assert.Equal(0, (await ListWordsAsync()).TotalCount);
    }

    [Fact]
    public async Task ФайлБезКолонкиСоСловомОтклоняетсяЦеликом()
    {
        var jobId = await PrepareAsync("описание;видео\nживотное;a.mp4");

        var job = await GetAsync(jobId);

        Assert.NotNull(job);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.NotNull(job.Error);
        Assert.False(job.CanApply);
    }

    [Fact]
    public async Task ИмпортированныеСловаНеВидныГостямПокаНетГотовогоВидео()
    {
        await ApplyAsync(await PrepareAsync("слово;описание\nкошка;животное"));

        // Снимок пересобирается при применении импорта, но слово в него не попадает:
        // видео ещё не обработано.
        Assert.Equal(0, _fixture.Index.Count);
    }
}
