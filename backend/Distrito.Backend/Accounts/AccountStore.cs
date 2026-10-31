using System.Security.Cryptography;
using Npgsql;

namespace Distrito.Backend.Accounts;

public sealed record IssuedSession(string Token, DateTimeOffset ExpiresAt);

/// <summary>Cuentas y sesiones de login.</summary>
public sealed class AccountStore(NpgsqlDataSource db, PasswordHasher hasher, TimeProvider clock, IConfiguration config)
{
    private TimeSpan SessionLifetime => TimeSpan.FromHours(config.GetValue("Auth:SessionHours", 24));

    /// <summary>Crea la cuenta. Devuelve null si el usuario ya existe.</summary>
    public async Task<Guid?> RegisterAsync(string username, string password, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = db.CreateCommand("""
            INSERT INTO distrito.accounts (id, username, username_normalized, password_hash)
            VALUES (@id, @u, @n, @h)
            ON CONFLICT (username_normalized) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("u", username);
        cmd.Parameters.AddWithValue("n", username.ToUpperInvariant());
        cmd.Parameters.AddWithValue("h", hasher.Hash(password));
        return await cmd.ExecuteNonQueryAsync(ct) == 1 ? id : null;
    }

    /// <summary>Verifica credenciales y emite una sesión. Devuelve null si son incorrectas.</summary>
    public async Task<IssuedSession?> LoginAsync(string username, string password, CancellationToken ct)
    {
        Guid accountId;
        string storedHash;
        await using (var find = db.CreateCommand(
            "SELECT id, password_hash FROM distrito.accounts WHERE username_normalized = @n"))
        {
            find.Parameters.AddWithValue("n", username.ToUpperInvariant());
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Se calcula un hash igual para que el tiempo de respuesta no revele si el usuario existe.
                PasswordHasher.Verify(password, _dummyHash.Value);
                return null;
            }
            accountId = reader.GetGuid(0);
            storedHash = reader.GetString(1);
        }

        if (!PasswordHasher.Verify(password, storedHash))
            return null;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = clock.GetUtcNow() + SessionLifetime;

        await using var insert = db.CreateCommand(
            "INSERT INTO distrito.sessions (token_hash, account_id, expires_at) VALUES (@t, @a, @e)");
        insert.Parameters.AddWithValue("t", HashToken(token));
        insert.Parameters.AddWithValue("a", accountId);
        insert.Parameters.AddWithValue("e", expiresAt);
        await insert.ExecuteNonQueryAsync(ct);

        return new IssuedSession(token, expiresAt);
    }

    /// <summary>Cuenta dueña de un token vigente, o null.</summary>
    public async Task<Guid?> ResolveTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            return null;

        await using var cmd = db.CreateCommand(
            "SELECT account_id FROM distrito.sessions WHERE token_hash = @t AND expires_at > @now");
        cmd.Parameters.AddWithValue("t", HashToken(token));
        cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
        return await cmd.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }

    private static byte[] HashToken(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    private readonly Lazy<string> _dummyHash = new(() => hasher.Hash("dummy-password-for-timing"));
}
