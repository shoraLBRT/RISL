using RISL.Application.Import;

namespace RISL.Tests.Import;

public class ImportPipelineTests
{
    private static ImportPlan Plan(
        string csv,
        IEnumerable<string>? existingWords = null,
        IEnumerable<string>? archiveFiles = null)
    {
        var read = ImportCsvReader.Read(csv);

        return ImportPlanner.Plan(
            read,
            new HashSet<string>(existingWords ?? [], StringComparer.Ordinal),
            new HashSet<string>(archiveFiles ?? [], StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_ТребуетКолонкуСоСловом()
    {
        var result = ImportCsvReader.Read("описание;видео\nживотное;a.mp4");

        Assert.True(result.IsFatal);
        Assert.Contains("слово", result.FatalError);
    }

    [Fact]
    public void Read_ПониматАнглийскиеИРусскиеЗаголовки()
    {
        var russian = ImportCsvReader.Read("Слово;Описание;Категории;Видео\nкошка;животное;Животные;a.mp4");
        var english = ImportCsvReader.Read("word;description;categories;video\nкошка;животное;Животные;a.mp4");

        Assert.Equal("кошка", Assert.Single(russian.Rows).Word);
        Assert.Equal("кошка", Assert.Single(english.Rows).Word);
    }

    [Fact]
    public void Read_РазбираетНесколькоКатегорийВОднойЯчейке()
    {
        var result = ImportCsvReader.Read("слово;категории\nяблоко;\"Еда|Природа\"");

        Assert.Equal(["Еда", "Природа"], Assert.Single(result.Rows).Categories);
    }

    [Fact]
    public void Read_ОтмечаетПустоеСловоКакОшибку()
    {
        var result = ImportCsvReader.Read("слово;описание\n;животное\nкошка;животное");

        Assert.Single(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.LineNumber);
        Assert.Contains("Пустое слово", error.Message);
    }

    [Fact]
    public void Read_ЛовитПовторСловаВнутриФайлаСУчётомРегистраИБуквыЁ()
    {
        var result = ImportCsvReader.Read("слово;описание\nЁлка;дерево\nелка;другое описание");

        Assert.Single(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains("строке 2", error.Message);
    }

    [Fact]
    public void Plan_НовоеСловоСоздаётсяСуществующееОбновляется()
    {
        var plan = Plan(
            "слово;описание\nкошка;животное\nсобака;животное",
            existingWords: ["кошка"]);

        Assert.Equal(1, plan.ToUpdate);
        Assert.Equal(1, plan.ToCreate);
        Assert.Equal(0, plan.Failed);
    }

    [Fact]
    public void Plan_ПовторныйИмпортТогоЖеФайлаТолькоОбновляет()
    {
        const string csv = "слово;описание\nкошка;животное\nсобака;животное";

        var first = Plan(csv);
        var second = Plan(csv, existingWords: ["кошка", "собака"]);

        Assert.Equal(2, first.ToCreate);
        Assert.Equal(0, second.ToCreate);
        Assert.Equal(2, second.ToUpdate);
    }

    [Fact]
    public void Plan_БракуетСтрокуЕслиВидеоНетВАрхиве()
    {
        var plan = Plan(
            "слово;видео\nкошка;koshka.mp4\nсобака;sobaka.mp4",
            archiveFiles: ["koshka.mp4"]);

        Assert.Equal(1, plan.ToCreate);
        Assert.Equal(1, plan.Failed);
        Assert.Contains("sobaka.mp4", plan.Items.Single(item => item.Action == ImportAction.Error).Message);
    }

    [Fact]
    public void Plan_СопоставляетИмяФайлаБезУчётаРегистра()
    {
        var plan = Plan("слово;видео\nкошка;Koshka.MP4", archiveFiles: ["koshka.mp4"]);

        Assert.Equal(1, plan.ToCreate);
    }

    [Fact]
    public void Plan_ПропускаетСтрокуБезВидеоНоНеБракуетЕё()
    {
        var plan = Plan("слово;описание;видео\nкошка;животное;");

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportAction.Create, item.Action);
        Assert.Null(item.VideoFileName);
    }

    [Fact]
    public void Plan_СохраняетПорядокСтрокФайлаВключаяОшибки()
    {
        var plan = Plan("слово;описание\nкошка;животное\n;пусто\nсобака;животное");

        Assert.Equal([2, 3, 4], plan.Items.Select(item => item.LineNumber));
        Assert.Equal(3, plan.TotalRows);
    }
}
