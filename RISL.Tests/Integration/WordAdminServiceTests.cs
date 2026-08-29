using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RISL.Application.Abstractions;
using RISL.Application.Admin;
using RISL.Application.Search;
using RISL.Infrastructure.Catalog;
using RISL.Infrastructure.Persistence;

namespace RISL.Tests.Integration;

public sealed class WordAdminServiceTests : IAsyncLifetime
{
    private DictionaryFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new DictionaryFixture();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private Task<AdminWordSaveResult> CreateAsync(string text, string description = "", bool published = true) =>
        _fixture.InScopeAsync(services => services
            .GetRequiredService<IWordAdminService>()
            .CreateAsync(new AdminWordForm(text, description, published, [], null)));

    /// <summary>Отмечает видео готовым в обход ffmpeg и пересобирает снимок словаря.</summary>
    private Task MakeVideoReadyAsync(int wordId) =>
        _fixture.InScopeAsync(async services =>
        {
            var database = services.GetRequiredService<RislDbContext>();
            var word = await database.Words.FirstAsync(entity => entity.Id == wordId);

            word.MarkVideoPending("source.mp4");
            word.MarkVideoReady($"{wordId}.mp4", $"{wordId}.jpg", 25);
            await database.SaveChangesAsync();

            await services.GetRequiredService<SearchIndexMaintainer>().RefreshAsync();
        });

    [Fact]
    public async Task Создание_ОтклоняетПовторСловаБезУчётаРегистраИБуквыЁ()
    {
        await CreateAsync("Ёлка");

        var duplicate = await CreateAsync("елка");

        Assert.False(duplicate.Success);
        Assert.Contains("уже есть", duplicate.Error);
    }

    [Fact]
    public async Task Создание_ЗаполняетСлагДляАдресаСтраницы()
    {
        var created = await CreateAsync("Серая кошка");
        await MakeVideoReadyAsync(created.Id);

        var entry = _fixture.Index.FindById(created.Id);

        Assert.NotNull(entry);
        Assert.Equal("seraya-koshka", entry.Slug);
    }

    [Fact]
    public async Task СловоБезГотовогоВидеоНеПопадаетВВыдачуГостя()
    {
        var created = await CreateAsync("кошка", "животное");

        Assert.Equal(0, _fixture.Index.Count);

        await MakeVideoReadyAsync(created.Id);

        Assert.Equal(1, _fixture.Index.Count);
        Assert.NotNull(_fixture.Index.FindById(created.Id));
    }

    [Fact]
    public async Task СнятиеПубликацииУбираетСловоИзВыдачи()
    {
        var created = await CreateAsync("кошка", "животное");
        await MakeVideoReadyAsync(created.Id);

        await _fixture.InScopeAsync(services => services
            .GetRequiredService<IWordAdminService>()
            .UpdateAsync(created.Id, new AdminWordForm("кошка", "животное", IsPublished: false, [], null)));

        Assert.Equal(0, _fixture.Index.Count);
    }

    [Fact]
    public async Task Удаление_УбираетСловоИзБазыИИзВыдачи()
    {
        var created = await CreateAsync("кошка", "животное");
        await MakeVideoReadyAsync(created.Id);

        var deleted = await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IWordAdminService>().DeleteAsync(created.Id));

        Assert.True(deleted);
        Assert.Equal(0, _fixture.Index.Count);
        Assert.Null(await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IWordAdminService>().GetAsync(created.Id)));
    }

    [Fact]
    public async Task ПоискГостяНаходитСловоПоФрагментуОписания()
    {
        var created = await CreateAsync("собака", "домашнее животное, родственник волка");
        await MakeVideoReadyAsync(created.Id);

        var result = _fixture.Index.Search(new SearchQuery { Text = "волка" });

        Assert.Equal(["собака"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public async Task Сводка_СчитаетСловаПоСостояниямВидео()
    {
        var ready = await CreateAsync("кошка");
        await CreateAsync("собака");
        await CreateAsync("черновик", published: false);
        await MakeVideoReadyAsync(ready.Id);

        var dashboard = await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IWordAdminService>().GetDashboardAsync());

        Assert.Equal(3, dashboard.TotalWords);
        Assert.Equal(1, dashboard.VisibleWords);
        Assert.Equal(2, dashboard.WithoutVideo);
    }
}
