using System.Globalization;

namespace RISL.Blazor;

/// <summary>
/// Разбор значений из строки запроса.
/// </summary>
/// <remarks>
/// Штатная привязка <c>[SupplyParameterFromQuery]</c> к <c>bool</c> или <c>int</c>
/// выбрасывает исключение, если значение не разобралось, и страница отдаёт 500.
/// В адресную строку может прийти что угодно — опечатка пользователя, старая
/// закладка, обход поискового робота, — поэтому все такие параметры принимаются
/// строками и приводятся здесь, а неразобранное значение просто игнорируется.
/// </remarks>
public static class QueryValues
{
    private static readonly string[] TrueValues = ["1", "true", "yes", "on"];

    /// <summary>
    /// Признак-флаг. Истиной считается «1», «true», «yes», «on», а также параметр
    /// без значения — форма вида <c>?saved</c>.
    /// </summary>
    public static bool IsSet(string? value) =>
        value is not null && (value.Length == 0 || TrueValues.Contains(value, StringComparer.OrdinalIgnoreCase));

    /// <summary>Трёхзначный флаг: <c>null</c>, если параметра нет или он неразборчив.</summary>
    public static bool? ToBool(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (TrueValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.Equals("0", StringComparison.Ordinal)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
    }

    /// <summary>Номер страницы. Мусор и значения меньше единицы сводятся к первой странице.</summary>
    public static int ToPage(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) && page > 0 ? page : 1;
}
