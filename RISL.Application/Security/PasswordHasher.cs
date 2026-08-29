using System.Security.Cryptography;

namespace RISL.Application.Security;

/// <summary>
/// Хеширование пароля единственного администратора. Пароль в открытом виде нигде
/// не хранится: в конфиге лежат только соль и результат PBKDF2.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>Создаёт пару «соль + хеш» для записи в конфигурацию.</summary>
    public static (string Salt, string Hash) Create(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Проверяет пароль. Сравнение постоянного времени, чтобы по длительности ответа
    /// нельзя было подбирать хеш побайтно.
    /// </summary>
    public static bool Verify(string? password, string? saltBase64, string? hashBase64)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(saltBase64) || string.IsNullOrWhiteSpace(hashBase64))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(saltBase64);
            expected = Convert.FromBase64String(hashBase64);
        }
        catch (FormatException)
        {
            // Конфиг заполнен мусором — это отказ в доступе, а не падение приложения.
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
