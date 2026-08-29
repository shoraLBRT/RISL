using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;
using RISL.Application.Admin;
using RISL.Application.Import;
using RISL.Blazor.Security;
using RISL.Infrastructure.Media;

namespace RISL.Blazor.Endpoints;

/// <summary>
/// Обработчики форм панели администратора.
/// </summary>
/// <remarks>
/// Все изменения проходят обычным POST формы с последующим редиректом: страница
/// не зависит от JavaScript, повторная отправка по F5 невозможна, а крупные видео
/// загружаются штатными средствами браузера, а не через канал приложения.
/// </remarks>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Пути эндпоинтов не должны совпадать с адресами страниц: страницы Blazor
        // на статическом SSR тоже отвечают на POST, и маршрут стал бы неоднозначным.
        endpoints.MapPost("/admin/api/login", LoginAsync)
            .RequireRateLimiting(RateLimitPolicies.Login)
            .DisableAntiforgery();

        endpoints.MapPost("/admin/api/logout", (Delegate)LogoutAsync)
            .RequireAuthorization(AdminAuthentication.Policy)
            .DisableAntiforgery();

        var admin = endpoints.MapGroup("/admin/api")
            .RequireAuthorization(AdminAuthentication.Policy)
            // Проверку токена делаем вручную внутри обработчиков: формы читаются
            // напрямую, чтобы крупные файлы не проходили через привязку модели.
            .DisableAntiforgery();

        admin.MapPost("/words", SaveWordAsync);
        admin.MapPost("/words/{id:int}/delete", DeleteWordAsync);
        admin.MapPost("/words/{id:int}/retry", RetryVideoAsync);

        admin.MapPost("/categories", SaveCategoryAsync);
        admin.MapPost("/categories/{id:int}/delete", DeleteCategoryAsync);

        admin.MapPost("/import", PrepareImportAsync);
        admin.MapPost("/import/{id:int}/apply", ApplyImportAsync);
        admin.MapPost("/import/{id:int}/cancel", CancelImportAsync);

        admin.MapPost("/feedback/{id:int}/handled", SetFeedbackHandledAsync);
        admin.MapPost("/feedback/{id:int}/delete", DeleteFeedbackAsync);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IOptions<AdminAccountOptions> options)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/login?error=expired");
        }

        var form = await context.Request.ReadFormAsync();
        var principal = AdminAuthentication.TrySignIn(options.Value, form["login"], form["password"]);

        if (principal is null)
        {
            return Results.Redirect("/admin/login?error=invalid");
        }

        await context.SignInAsync(AdminAuthentication.Scheme, principal);

        return Results.Redirect("/admin");
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(AdminAuthentication.Scheme);

        return Results.Redirect("/");
    }

    private static async Task<IResult> SaveWordAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IWordAdminService words,
        IMediaStorage storage,
        IVideoProcessor processor,
        IOptions<MediaOptions> mediaOptions,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/words?error=expired");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);

        var id = ParseInt(form["id"]);
        var returnUrl = id is null ? "/admin/words/new" : $"/admin/words/{id}";

        var upload = await UploadHelper.SaveVideoAsync(
            form.Files["video"],
            storage,
            processor,
            mediaOptions,
            cancellationToken);

        if (upload.IsRejected)
        {
            return Results.Redirect($"{returnUrl}?error={Uri.EscapeDataString(upload.Error!)}");
        }

        var model = new AdminWordForm(
            form["text"].ToString(),
            form["description"].ToString(),
            form["isPublished"].Count > 0,
            [.. form["categoryIds"].Select(value => ParseInt(value)).OfType<int>()],
            upload.StoredFileName);

        var result = id is null
            ? await words.CreateAsync(model, cancellationToken)
            : await words.UpdateAsync(id.Value, model, cancellationToken);

        if (!result.Success)
        {
            // Принятый файл больше не нужен: слово не сохранилось.
            if (upload.StoredFileName is not null)
            {
                storage.Delete(MediaArea.Incoming, upload.StoredFileName);
            }

            return Results.Redirect($"{returnUrl}?error={Uri.EscapeDataString(result.Error!)}");
        }

        return Results.Redirect($"/admin/words/{result.Id}?saved=1");
    }

    private static async Task<IResult> DeleteWordAsync(
        int id,
        HttpContext context,
        IAntiforgery antiforgery,
        IWordAdminService words,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/words?error=expired");
        }

        await words.DeleteAsync(id, cancellationToken);

        return Results.Redirect("/admin/words?deleted=1");
    }

    private static async Task<IResult> RetryVideoAsync(
        int id,
        HttpContext context,
        IAntiforgery antiforgery,
        IWordAdminService words,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect($"/admin/words/{id}?error=expired");
        }

        var result = await words.RetryVideoAsync(id, cancellationToken);

        return result.Success
            ? Results.Redirect($"/admin/words/{id}?queued=1")
            : Results.Redirect($"/admin/words/{id}?error={Uri.EscapeDataString(result.Error!)}");
    }

    private static async Task<IResult> SaveCategoryAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ICategoryAdminService categories,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/categories?error=expired");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);

        var id = ParseInt(form["id"]);
        var name = form["name"].ToString();
        var sortOrder = ParseInt(form["sortOrder"]) ?? 0;

        var result = id is null
            ? await categories.CreateAsync(name, sortOrder, cancellationToken)
            : await categories.UpdateAsync(id.Value, name, sortOrder, cancellationToken);

        return result.Success
            ? Results.Redirect("/admin/categories?saved=1")
            : Results.Redirect($"/admin/categories?error={Uri.EscapeDataString(result.Error!)}");
    }

    private static async Task<IResult> DeleteCategoryAsync(
        int id,
        HttpContext context,
        IAntiforgery antiforgery,
        ICategoryAdminService categories,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/categories?error=expired");
        }

        await categories.DeleteAsync(id, cancellationToken);

        return Results.Redirect("/admin/categories?deleted=1");
    }

    private static async Task<IResult> PrepareImportAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IImportService import,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/import?error=expired");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);

        var csv = form.Files["csv"];
        if (csv is null || csv.Length == 0)
        {
            return Results.Redirect("/admin/import?error=" + Uri.EscapeDataString("Выберите файл CSV."));
        }

        string content;
        await using (var stream = csv.OpenReadStream())
        {
            // detectEncodingFromByteOrderMarks разберётся с UTF-8 BOM, который
            // добавляет Excel; без BOM считаем файл UTF-8.
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        var archive = form.Files["archive"];
        Stream? archiveStream = null;

        try
        {
            if (archive is { Length: > 0 })
            {
                archiveStream = archive.OpenReadStream();
            }

            var jobId = await import.PrepareAsync(
                new ImportSource(csv.FileName, content, archiveStream),
                cancellationToken);

            return Results.Redirect($"/admin/import/{jobId}");
        }
        catch (InvalidDataException)
        {
            return Results.Redirect("/admin/import?error=" + Uri.EscapeDataString("Архив повреждён или не является zip-файлом."));
        }
        finally
        {
            if (archiveStream is not null)
            {
                await archiveStream.DisposeAsync();
            }
        }
    }

    private static async Task<IResult> ApplyImportAsync(
        int id,
        HttpContext context,
        IAntiforgery antiforgery,
        IImportService import,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect($"/admin/import/{id}?error=expired");
        }

        await import.ApplyAsync(id, cancellationToken);

        return Results.Redirect($"/admin/import/{id}");
    }

    private static async Task<IResult> CancelImportAsync(
        int id,
        HttpContext context,
        IAntiforgery antiforgery,
        IImportService import,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect($"/admin/import/{id}?error=expired");
        }

        await import.CancelAsync(id, cancellationToken);

        return Results.Redirect($"/admin/import/{id}");
    }

    private static async Task<IResult> SetFeedbackHandledAsync(
        int id,
        [FromQuery] bool handled,
        HttpContext context,
        IAntiforgery antiforgery,
        IFeedbackService feedback,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/feedback?error=expired");
        }

        await feedback.SetHandledAsync(id, handled, cancellationToken);

        return Results.Redirect("/admin/feedback");
    }

    private static async Task<IResult> DeleteFeedbackAsync(
        int id,
        HttpContext context,
        IAntiforgery antiforgery,
        IFeedbackService feedback,
        CancellationToken cancellationToken)
    {
        if (!await IsTokenValidAsync(antiforgery, context))
        {
            return Results.Redirect("/admin/feedback?error=expired");
        }

        await feedback.DeleteAsync(id, cancellationToken);

        return Results.Redirect("/admin/feedback?deleted=1");
    }

    /// <summary>
    /// Проверка токена защиты от подделки межсайтовых запросов. Просроченный токен —
    /// это обычная ситуация после долгого простоя вкладки, поэтому вместо ошибки
    /// возвращаем пользователя на форму.
    /// </summary>
    private static async Task<bool> IsTokenValidAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;
}
