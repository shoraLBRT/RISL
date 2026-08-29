using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using RISL.Application.Abstractions;
using RISL.Application.Security;
using RISL.Blazor.Components;
using RISL.Blazor.Endpoints;
using RISL.Blazor.Security;
using RISL.Infrastructure;
using RISL.Infrastructure.Media;

// Служебный режим: печатает соль и хеш пароля для конфигурации.
// Иначе завести администратора было бы негде — регистрации в сервисе нет.
if (args is ["hash-password", var plainPassword, ..])
{
    var (salt, hash) = PasswordHasher.Create(plainPassword);

    Console.WriteLine("Admin__PasswordSalt=" + salt);
    Console.WriteLine("Admin__PasswordHash=" + hash);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Публичная часть работает без интерактивности: ни одного WebSocket на гостя,
// страница остаётся живой на плохой мобильной связи и полностью индексируется.
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRislInfrastructure(builder.Configuration);

// Проверка состояния для docker compose: контейнер считается живым,
// только когда приложение действительно отвечает.
builder.Services.AddHealthChecks();

builder.Services.Configure<AdminAccountOptions>(builder.Configuration.GetSection(AdminAccountOptions.SectionName));

builder.Services
    .AddAuthentication(AdminAuthentication.Scheme)
    .AddCookie(AdminAuthentication.Scheme, options =>
    {
        options.Cookie.Name = "risl-admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(AdminAuthentication.Policy, policy => policy
        .AddAuthenticationSchemes(AdminAuthentication.Scheme)
        .RequireAuthenticatedUser());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Подбор пароля к единственному аккаунту — самый очевидный вектор атаки.
    options.AddPolicy(RateLimitPolicies.Login, PerClientFixedWindow(5, TimeSpan.FromMinutes(15)));

    // Форма обратной связи открыта всем, поэтому её надо прикрыть от спама.
    options.AddPolicy(RateLimitPolicies.Feedback, PerClientFixedWindow(5, TimeSpan.FromHours(1)));
});

// Видео с телефона легко весит сотни мегабайт, а умолчания ASP.NET рассчитаны
// на формы в десятки килобайт. Предел берём из той же настройки, что и проверка
// размера при загрузке, чтобы они не разошлись.
var maxUploadBytes = builder.Configuration.GetValue<long?>($"{MediaOptions.SectionName}:MaxUploadBytes")
    ?? new MediaOptions().MaxUploadBytes;

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
    options.MultipartHeadersLengthLimit = 64 * 1024;
});

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadBytes + (1024 * 1024));

// За обратным прокси иначе не видно ни настоящего IP клиента, ни схемы https.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.MapStaticAssets();
MapMediaFiles(app);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapRazorComponents<App>();
app.MapAdminEndpoints();
app.MapPublicEndpoints();

app.Run();

static Func<HttpContext, RateLimitPartition<string>> PerClientFixedWindow(int permitLimit, TimeSpan window) =>
    context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
        });

/// <summary>
/// Раздача видео и постеров из каталога вне wwwroot: иначе загруженное админом
/// затиралось бы при каждой публикации приложения.
/// </summary>
static void MapMediaFiles(WebApplication app)
{
    var storage = app.Services.GetRequiredService<FileSystemMediaStorage>();
    var options = app.Services.GetRequiredService<IOptions<MediaOptions>>().Value;

    foreach (var area in new[] { MediaArea.Videos, MediaArea.Posters })
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(storage.GetAreaRoot(area)),
            RequestPath = $"{options.PublicPathPrefix.TrimEnd('/')}/{FileSystemMediaStorage.FolderOf(area)}",
            OnPrepareResponse = context =>
            {
                // Имена файлов содержат GUID и никогда не переиспользуются,
                // поэтому кэшировать их можно сколь угодно долго.
                context.Context.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
            },
        });
    }
}
