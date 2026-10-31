using System.Text.RegularExpressions;
using Npgsql;

namespace Distrito.Backend.Persistence;

/// <summary>
/// Aplica database/migrations/NNNN_*.sql en orden. Cada archivo en su propia transacción,
/// registrado en distrito.schema_migrations. Ver ADR-002.
/// </summary>
public sealed partial class MigrationRunner(NpgsqlDataSource dataSource, ILogger<MigrationRunner> logger)
{
    private const long AdvisoryLockKey = 0x44495354; // "DIST"

    public sealed record Migration(int Version, string Name, string Path);

    public static IReadOnlyList<Migration> Discover(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"No existe la carpeta de migraciones: {directory}");

        var migrations = Directory.GetFiles(directory, "*.sql")
            .Select(path => (path, match: FilePattern().Match(System.IO.Path.GetFileName(path))))
            .Where(x => x.match.Success)
            .Select(x => new Migration(int.Parse(x.match.Groups[1].Value), System.IO.Path.GetFileNameWithoutExtension(x.path), x.path))
            .OrderBy(m => m.Version)
            .ToList();

        var duplicated = migrations.GroupBy(m => m.Version).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new InvalidOperationException($"Hay dos migraciones con la versión {duplicated.Key}.");

        return migrations;
    }

    /// <summary>Devuelve la cantidad de migraciones aplicadas en esta ejecución.</summary>
    public async Task<int> MigrateAsync(string directory, CancellationToken ct = default)
    {
        var migrations = Discover(directory);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        // Candado de sesión: un solo backend migra a la vez (incluye la creación de la tabla de control).
        await ExecuteAsync(connection, null, $"SELECT pg_advisory_lock({AdvisoryLockKey})", ct);
        try
        {
            return await ApplyPendingAsync(connection, migrations, ct);
        }
        finally
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None);
        }
    }

    private async Task<int> ApplyPendingAsync(NpgsqlConnection connection, IReadOnlyList<Migration> migrations, CancellationToken ct)
    {
        await ExecuteAsync(connection, null, """
            CREATE SCHEMA IF NOT EXISTS distrito;
            CREATE TABLE IF NOT EXISTS distrito.schema_migrations (
                version     integer     PRIMARY KEY,
                name        text        NOT NULL,
                applied_at  timestamptz NOT NULL DEFAULT now()
            );
            """, ct);

        var applied = 0;
        foreach (var migration in migrations)
        {
            await using var tx = await connection.BeginTransactionAsync(ct);

            await using (var check = new NpgsqlCommand(
                "SELECT 1 FROM distrito.schema_migrations WHERE version = @v", connection, tx))
            {
                check.Parameters.AddWithValue("v", migration.Version);
                if (await check.ExecuteScalarAsync(ct) is not null)
                {
                    await tx.RollbackAsync(ct);
                    continue;
                }
            }

            logger.LogInformation("Aplicando migración {Name}", migration.Name);
            await ExecuteAsync(connection, tx, await File.ReadAllTextAsync(migration.Path, ct), ct);

            await using (var record = new NpgsqlCommand(
                "INSERT INTO distrito.schema_migrations (version, name) VALUES (@v, @n)", connection, tx))
            {
                record.Parameters.AddWithValue("v", migration.Version);
                record.Parameters.AddWithValue("n", migration.Name);
                await record.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            applied++;
        }

        logger.LogInformation("Migraciones: {Applied} aplicadas, {Total} en total", applied, migrations.Count);
        return applied;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        await command.ExecuteNonQueryAsync(ct);
    }

    [GeneratedRegex(@"^(\d{4})_[a-z0-9_]+\.sql$")]
    private static partial Regex FilePattern();
}
