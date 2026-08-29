using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Antiforgery;
using RISL.Application.Abstractions;
using RISL.Application.Search;
using RISL.Blazor.Security;

namespace RISL.Blazor.Endpoints;

/// <summary>Обработчики гостевых форм и служебных файлов для поисковых систем.</summary>
public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Отдельный путь: страница /feedback на статическом SSR сама отвечает на POST.
        endpoints.MapPost("/feedback/send", SubmitFeedbackAsync)
            .RequireRateLimiting(RateLimitPolicies.Feedback)
            .DisableAntiforgery();

        endpoints.MapGet("/sitemap.xml", GetSitemap);
        endpoints.MapGet("/robots.txt", GetRobots);

        return endpoints;
    }

    private static async Task<IResult> SubmitFeedbackAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IFeedbackService feedback,
        CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Redirect("/feedback?error=expired");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);

        // Поле-приманка: человек его не видит и не заполняет, простые боты заполняют всё.
        if (!string.IsNullOrWhiteSpace(form["website"]))
        {
            return Results.Redirect("/feedback?sent=1");
        }

        var message = form["message"].ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return Results.Redirect("/feedback?error=empty");
        }

        await feedback.SubmitAsync(message, form["name"], form["contact"], cancellationToken);

        return Results.Redirect("/feedback?sent=1");
    }

    /// <summary>
    /// Карта сайта собирается из поискового снимка, то есть содержит ровно те слова,
    /// которые видит гость.
    /// </summary>
    private static IResult GetSitemap(HttpContext context, IWordSearchIndex index)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host}";

        // Пишем в поток, а не в StringBuilder: иначе XmlWriter объявит в заголовке
        // utf-16 (внутреннюю кодировку строк .NET), и файл разойдётся с тем,
        // в чём он реально отдаётся.
        var buffer = new MemoryStream();

        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            WriteUrl(writer, $"{origin}/", "daily", "1.0");
            WriteUrl(writer, $"{origin}/about", "monthly", "0.3");
            WriteUrl(writer, $"{origin}/team", "monthly", "0.3");

            // Индекс отдаёт выдачу страницами, поэтому словарь любого размера
            // обходим до конца, а не только первой порцией.
            const int pageSize = 500;
            for (var page = 1; ; page++)
            {
                var result = index.Search(new SearchQuery { Page = page, PageSize = pageSize });

                foreach (var word in result.Items)
                {
                    WriteUrl(writer, $"{origin}/word/{word.Id}/{word.Slug}", "monthly", "0.8");
                }

                if (!result.HasNextPage)
                {
                    break;
                }
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Results.Bytes(buffer.ToArray(), "application/xml; charset=utf-8");
    }

    private static void WriteUrl(XmlWriter writer, string location, string changeFrequency, string priority)
    {
        writer.WriteStartElement("url");
        writer.WriteElementString("loc", location);
        writer.WriteElementString("changefreq", changeFrequency);
        writer.WriteElementString("priority", priority);
        writer.WriteEndElement();
    }

    private static IResult GetRobots(HttpContext context)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host}";

        var content = $"""
            User-agent: *
            Disallow: /admin
            Allow: /

            Sitemap: {origin}/sitemap.xml
            """;

        return Results.Text(content, "text/plain", Encoding.UTF8);
    }
}
