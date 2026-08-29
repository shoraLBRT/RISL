using System.Reflection;
using Microsoft.AspNetCore.Components;
using RISL.Blazor;

namespace RISL.Tests.Web;

/// <summary>
/// Сторож против целого класса ошибок: параметр строки запроса, привязанный не к строке.
/// </summary>
/// <remarks>
/// Blazor разбирает такие параметры сам и выбрасывает исключение на любом значении,
/// которое не разобралось, — страница отдаёт 500. Ронять её может и опечатка в адресе,
/// и старая закладка, и обход поискового робота. Правило простое: из строки запроса
/// принимаем строку, а приводим её через <see cref="QueryValues"/>.
/// </remarks>
public class QueryParameterBindingTests
{
    [Fact]
    public void ВсеПараметрыСтрокиЗапросаПринимаютсяСтроками()
    {
        var offenders = typeof(QueryValues).Assembly
            .GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.GetCustomAttribute<SupplyParameterFromQueryAttribute>() is not null)
            .Where(property => property.PropertyType != typeof(string))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}: {property.PropertyType.Name}")
            .Order()
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Эти параметры уронят страницу на неразобранном значении в адресе. "
                + "Примите их как string? и приведите через QueryValues:\n  "
                + string.Join("\n  ", offenders));
    }

    [Fact]
    public void ПараметрыСтрокиЗапросаВообщеНайдены()
    {
        // Если поиск однажды перестанет что-либо находить, предыдущий тест
        // превратится в вечно зелёную пустышку.
        var count = typeof(QueryValues).Assembly
            .GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Count(property => property.GetCustomAttribute<SupplyParameterFromQueryAttribute>() is not null);

        Assert.True(count >= 10, $"Ожидались параметры строки запроса, найдено: {count}");
    }
}
