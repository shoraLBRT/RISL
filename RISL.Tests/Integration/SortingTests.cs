using Microsoft.Extensions.DependencyInjection;
using RISL.Application.Abstractions;
using RISL.Application.Admin;
using RISL.Application.Import;

namespace RISL.Tests.Integration;

/// <summary>
/// Проверяет сортировку по датам на настоящем SQLite.
/// </summary>
/// <remarks>
/// SQLite отказывается сортировать по DateTimeOffset в его обычном представлении,
/// и обнаруживается это только на живой базе: в памяти запрос отработал бы молча.
/// Поэтому даты хранятся в сортируемом UTC-формате, а эти тесты стерегут договорённость.
/// </remarks>
public sealed class SortingTests : IAsyncLifetime
{
    private DictionaryFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new DictionaryFixture();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task СписокСловСортируетсяПоДатеИзменения()
    {
        await _fixture.InScopeAsync(async services =>
        {
            var words = services.GetRequiredService<IWordAdminService>();

            await words.CreateAsync(new AdminWordForm("первое", "", true, [], null));
            await words.CreateAsync(new AdminWordForm("второе", "", true, [], null));
        });

        var page = await _fixture.InScopeAsync(services => services
            .GetRequiredService<IWordAdminService>()
            .ListAsync(new AdminWordQuery { Sort = AdminWordSort.Updated, Descending = true }));

        Assert.Equal(["второе", "первое"], page.Items.Select(item => item.Text));
    }

    [Fact]
    public async Task СообщенияОбратнойСвязиСортируютсяПоДате()
    {
        await _fixture.InScopeAsync(async services =>
        {
            var feedback = services.GetRequiredService<IFeedbackService>();

            await feedback.SubmitAsync("первое сообщение", null, null);
            await feedback.SubmitAsync("второе сообщение", "Иван", "ivan@example.org");
        });

        var messages = await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IFeedbackService>().ListAsync(onlyPending: true));

        Assert.Equal(2, messages.Count);
        Assert.Equal("второе сообщение", messages[0].Message);
    }

    [Fact]
    public async Task ЗаданияИмпортаСортируютсяПоДате()
    {
        await _fixture.InScopeAsync(async services =>
        {
            var import = services.GetRequiredService<IImportService>();

            await import.PrepareAsync(new ImportSource("first.csv", "слово\nкошка", null));
            await import.PrepareAsync(new ImportSource("second.csv", "слово\nсобака", null));
        });

        var jobs = await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IImportService>().ListAsync());

        Assert.Equal(["second.csv", "first.csv"], jobs.Select(job => job.FileName));
    }

    [Fact]
    public async Task ОбработанноеСообщениеУходитИзСпискаНепрочитанных()
    {
        await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IFeedbackService>().SubmitAsync("сообщение", null, null));

        var pending = await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IFeedbackService>().ListAsync(onlyPending: true));

        await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IFeedbackService>().SetHandledAsync(pending[0].Id, isHandled: true));

        Assert.Empty(await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IFeedbackService>().ListAsync(onlyPending: true)));

        Assert.Single(await _fixture.InScopeAsync(services =>
            services.GetRequiredService<IFeedbackService>().ListAsync(onlyPending: false)));
    }
}
