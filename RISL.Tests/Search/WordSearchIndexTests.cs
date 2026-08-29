using RISL.Application.Search;
using RISL.Domain;

namespace RISL.Tests.Search;

public class WordSearchIndexTests
{
    private static WordSearchEntry Entry(int id, string text, string description = "", params int[] categoryIds)
    {
        return new WordSearchEntry(
            id,
            text,
            TextNormalizer.Normalize(text),
            description,
            TextNormalizer.Normalize(description),
            TextNormalizer.Slugify(text),
            $"{id}.mp4",
            $"{id}.jpg",
            25,
            categoryIds);
    }

    private static WordSearchIndex BuildIndex(params WordSearchEntry[] entries)
    {
        var index = new WordSearchIndex();
        index.Load(entries, []);
        return index;
    }

    [Fact]
    public void Search_БезЗапросаВозвращаетВесьСловарьПоАлфавиту()
    {
        var index = BuildIndex(Entry(1, "яблоко"), Entry(2, "арбуз"), Entry(3, "банан"));

        var result = index.Search(new SearchQuery());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["арбуз", "банан", "яблоко"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public void Search_СтавитТочноеСовпадениеВышеЧастичного()
    {
        var index = BuildIndex(
            Entry(1, "домашняя кошка"),
            Entry(2, "кошка"),
            Entry(3, "кошка сиамская"));

        var result = index.Search(new SearchQuery { Text = "кошка" });

        // Точное совпадение, затем «начинается с», затем «содержит».
        Assert.Equal(["кошка", "кошка сиамская", "домашняя кошка"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public void Search_ПоНачалуСловаНаходитВсеПродолжения()
    {
        var index = BuildIndex(Entry(1, "кошечка"), Entry(2, "кошка"), Entry(3, "собака"));

        var result = index.Search(new SearchQuery { Text = "кош" });

        Assert.Equal(["кошечка", "кошка"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public void Search_СовпадениеПоСловуВышеСовпаденияПоОписанию()
    {
        var index = BuildIndex(
            Entry(1, "собака", "домашнее животное, родственник волка"),
            Entry(2, "волк", "дикое животное"));

        var result = index.Search(new SearchQuery { Text = "волк" });

        Assert.Equal(["волк", "собака"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public void Search_НеРазличаетРегистрИБукву_Ё()
    {
        var index = BuildIndex(Entry(1, "Ёлка"));

        Assert.Single(index.Search(new SearchQuery { Text = "елка" }).Items);
        Assert.Single(index.Search(new SearchQuery { Text = "ЁЛКА" }).Items);
        Assert.Single(index.Search(new SearchQuery { Text = "Ёл" }).Items);
    }

    [Fact]
    public void Search_НаходитПоСловамВЛюбомПорядке()
    {
        var index = BuildIndex(Entry(1, "красная площадь"));

        var result = index.Search(new SearchQuery { Text = "площадь красная" });

        Assert.Single(result.Items);
    }

    [Fact]
    public void Search_ВозвращаетПустойРезультатКогдаНичегоНеПодходит()
    {
        var index = BuildIndex(Entry(1, "кошка"), Entry(2, "собака"));

        var result = index.Search(new SearchQuery { Text = "автомобиль" });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public void Search_ФильтруетПоКатегории()
    {
        var index = new WordSearchIndex();
        index.Load(
            [Entry(1, "яблоко", "", 10), Entry(2, "молоток", "", 20)],
            [new CategoryView(10, "Еда", "eda", 0, 1), new CategoryView(20, "Инструменты", "instrumenty", 1, 1)]);

        var result = index.Search(new SearchQuery { CategorySlug = "eda" });

        Assert.Equal(["яблоко"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public void Search_НеизвестнаяКатегорияДаётПустуюВыдачуАНеВесьСловарь()
    {
        var index = BuildIndex(Entry(1, "яблоко"));

        var result = index.Search(new SearchQuery { CategorySlug = "nesuschestvuyuschaya" });

        Assert.Empty(result.Items);
    }

    [Fact]
    public void Search_ФильтруетПоБуквеУказателя()
    {
        var index = BuildIndex(Entry(1, "арбуз"), Entry(2, "банан"));

        var result = index.Search(new SearchQuery { Letter = 'а' });

        Assert.Equal(["арбуз"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public void Search_ОграничиваетВыдачуСпискомИзбранного()
    {
        var index = BuildIndex(Entry(1, "арбуз"), Entry(2, "банан"), Entry(3, "виноград"));

        var result = index.Search(new SearchQuery { Ids = [1, 3] });

        Assert.Equal(["арбуз", "виноград"], result.Items.Select(item => item.Text));
    }

    [Fact]
    public void Search_РазбиваетВыдачуНаСтраницы()
    {
        var entries = Enumerable.Range(1, 25).Select(number => Entry(number, $"слово{number:D2}")).ToArray();
        var index = BuildIndex(entries);

        var second = index.Search(new SearchQuery { Page = 2, PageSize = 10 });

        Assert.Equal(25, second.TotalCount);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(10, second.Items.Count);
        Assert.Equal("слово11", second.Items[0].Text);
        Assert.True(second.HasPreviousPage);
        Assert.True(second.HasNextPage);
    }

    [Fact]
    public void Letters_СодержатТолькоБуквыЗаКоторымиЕстьСловаИРешёткуВКонце()
    {
        var index = BuildIndex(Entry(1, "арбуз"), Entry(2, "яблоко"), Entry(3, "1 сентября"));

        Assert.Equal(['а', 'я', '#'], index.Letters);
    }

    [Fact]
    public void Load_ПодменяетСнимокЦеликом()
    {
        var index = BuildIndex(Entry(1, "старое"));
        index.Load([Entry(2, "новое")], []);

        Assert.Equal(1, index.Count);
        Assert.Null(index.FindById(1));
        Assert.NotNull(index.FindById(2));
    }
}
