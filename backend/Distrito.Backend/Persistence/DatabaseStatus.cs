using Npgsql;

namespace Distrito.Backend.Persistence;

public sealed record DatabaseCheckResult(bool IsReachable, int? SchemaVersion);

/// <summary>Verifica que PostgreSQL responde y devuelve la última migración aplicada.</summary>
public sealed class DatabaseStatus(NpgsqlDataSource dataSource, ILogger<DatabaseStatus> logger)
{
    public async Task<DatabaseCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                "SELECT max(version) FROM distrito.schema_migrations");
            var value = await command.ExecuteScalarAsync(ct);
            return new DatabaseCheckResult(true, value is int version ? version : null);
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            logger.LogWarning(ex, "La base de datos no responde.");
            return new DatabaseCheckResult(false, null);
        }
    }
}
