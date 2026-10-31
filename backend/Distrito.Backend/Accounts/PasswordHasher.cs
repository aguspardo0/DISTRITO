using System.Security.Cryptography;

namespace Distrito.Backend.Accounts;

/// <summary>
/// PBKDF2-HMAC-SHA256. Formato guardado: pbkdf2-sha256$iteraciones$salBase64$hashBase64.
/// Las iteraciones quedan en el hash, así se pueden subir más adelante sin romper cuentas viejas.
/// </summary>
public sealed class PasswordHasher(int iterations)
{
    public const int DefaultIterations = 600_000;
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public int Iterations { get; } = iterations >= 1_000
        ? iterations
        : throw new ArgumentOutOfRangeException(nameof(iterations), "Mínimo 1000 iteraciones.");

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        ArgumentNullException.ThrowIfNull(password);
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations) || iterations < 1_000)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
