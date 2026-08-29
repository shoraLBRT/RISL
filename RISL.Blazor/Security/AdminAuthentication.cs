using System.Security.Claims;
using Microsoft.Extensions.Options;
using RISL.Application.Security;

namespace RISL.Blazor.Security;

/// <summary>Учётные данные единственного администратора.</summary>
/// <remarks>
/// Ролей в сервисе нет: либо гость, либо админ. Полноценный Identity с таблицами
/// пользователей и восстановлением пароля здесь был бы платой ни за что.
/// </remarks>
public sealed class AdminAccountOptions
{
    public const string SectionName = "Admin";

    public string Login { get; set; } = string.Empty;

    /// <summary>Соль в base64. Задаётся переменной окружения, в репозиторий не попадает.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>Хеш PBKDF2 в base64.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Login)
        && !string.IsNullOrWhiteSpace(PasswordSalt)
        && !string.IsNullOrWhiteSpace(PasswordHash);
}

/// <summary>Имена схемы и политики доступа к панели администратора.</summary>
public static class AdminAuthentication
{
    public const string Scheme = "risl-admin";

    public const string Policy = "AdminOnly";

    /// <summary>Проверяет пару логин-пароль и собирает удостоверение для cookie.</summary>
    public static ClaimsPrincipal? TrySignIn(AdminAccountOptions options, string? login, string? password)
    {
        if (!options.IsConfigured)
        {
            return null;
        }

        if (!string.Equals(login?.Trim(), options.Login, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!PasswordHasher.Verify(password, options.PasswordSalt, options.PasswordHash))
        {
            return null;
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, options.Login), new Claim(ClaimTypes.Role, "Admin")],
            Scheme);

        return new ClaimsPrincipal(identity);
    }
}

/// <summary>Имена политик ограничения частоты запросов.</summary>
public static class RateLimitPolicies
{
    public const string Login = "login";

    public const string Feedback = "feedback";
}
