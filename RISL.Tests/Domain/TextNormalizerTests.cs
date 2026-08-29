using RISL.Domain;

namespace RISL.Tests.Domain;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Кошка", "кошка")]
    [InlineData("КОШКА", "кошка")]
    [InlineData("  кошка  ", "кошка")]
    [InlineData("серая   кошка", "серая кошка")]
    [InlineData("Ёлка", "елка")]
    [InlineData("ёЖ", "еж")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_ПриводитТекстКФормеПоиска(string? input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_СхлопываетПереносыСтрокИТабуляции()
    {
        Assert.Equal("два слова", TextNormalizer.Normalize("два\n\t слова"));
    }

    [Theory]
    [InlineData("Кошка", "koshka")]
    [InlineData("Ёлка", "elka")]
    [InlineData("Щенок", "schenok")]
    [InlineData("серая кошка", "seraya-koshka")]
    [InlineData("Мать-и-мачеха", "mat-i-macheha")]
    [InlineData("Подъезд", "podezd")]
    [InlineData("Ы", "y")]
    public void Slugify_ТранслитерируетКириллицу(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Slugify(input));
    }

    [Fact]
    public void Slugify_ОтбрасываетЗнакиПрепинанияИНеОставляетКрайнихДефисов()
    {
        Assert.Equal("privet-mir", TextNormalizer.Slugify("  «Привет, мир!»  "));
    }

    [Fact]
    public void Slugify_НаПустомВходеВозвращаетПустуюСтроку()
    {
        Assert.Equal(string.Empty, TextNormalizer.Slugify("!!!"));
    }

    [Fact]
    public void Slugify_ОграничиваетДлину()
    {
        var slug = TextNormalizer.Slugify(new string('щ', 200));

        Assert.True(slug.Length <= 84, $"Слаг неожиданно длинный: {slug.Length}");
    }
}
