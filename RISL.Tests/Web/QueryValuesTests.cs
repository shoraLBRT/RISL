using RISL.Blazor;

namespace RISL.Tests.Web;

/// <summary>
/// Значения из адресной строки не должны ронять страницу.
/// </summary>
/// <remarks>
/// Штатная привязка Blazor к <c>bool</c> и <c>int</c> выбрасывает исключение на
/// любом неразобранном значении, и пользователь видит страницу ошибки. Ловилось это
/// только живым переходом по ссылке, поэтому правила разбора закреплены тестами.
/// </remarks>
public class QueryValuesTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    // Параметр без значения: адрес вида ?saved
    [InlineData("", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    // Мусор — это отсутствие флага, а не повод падать.
    [InlineData("abc", false)]
    [InlineData("<script>", false)]
    public void IsSet_РазбираетФлагИНеПадаетНаМусоре(string? value, bool expected)
    {
        Assert.Equal(expected, QueryValues.IsSet(value));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void ToBool_РазличаетНетЗначенияИНеразборчивоеЗначение(string? value, bool? expected)
    {
        Assert.Equal(expected, QueryValues.ToBool(value));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("7", 7)]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("abc", 1)]
    [InlineData("", 1)]
    [InlineData(null, 1)]
    [InlineData("99999999999999999999", 1)]
    public void ToPage_СводитЛюбойМусорКПервойСтранице(string? value, int expected)
    {
        Assert.Equal(expected, QueryValues.ToPage(value));
    }
}
