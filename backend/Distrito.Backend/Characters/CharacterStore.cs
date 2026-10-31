using Distrito.Backend.Accounts;
using Distrito.Shared.Api;
using Npgsql;

namespace Distrito.Backend.Characters;

public enum CreateCharacterOutcome { Created, NameTaken, LimitReached }

public sealed class CharacterStore(NpgsqlDataSource db)
{
    public async Task<List<CharacterSummary>> ListAsync(Guid accountId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            "SELECT id, name, zone_id FROM distrito.characters WHERE account_id = @a ORDER BY created_at");
        cmd.Parameters.AddWithValue("a", accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<CharacterSummary>();
        while (await reader.ReadAsync(ct))
            list.Add(new CharacterSummary(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return list;
    }

    public async Task<(CreateCharacterOutcome Outcome, CharacterSummary? Character)> CreateAsync(
        Guid accountId, string name, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        // Bloquea la fila de la cuenta para que dos pedidos simultáneos no superen el límite.
        await using (var lockAccount = new NpgsqlCommand(
            "SELECT 1 FROM distrito.accounts WHERE id = @a FOR UPDATE", connection, tx))
        {
            lockAccount.Parameters.AddWithValue("a", accountId);
            await lockAccount.ExecuteScalarAsync(ct);
        }

        await using (var count = new NpgsqlCommand(
            "SELECT count(*) FROM distrito.characters WHERE account_id = @a", connection, tx))
        {
            count.Parameters.AddWithValue("a", accountId);
            if ((long)(await count.ExecuteScalarAsync(ct))! >= NameRules.MaxCharactersPerAccount)
                return (CreateCharacterOutcome.LimitReached, null);
        }

        var id = Guid.CreateVersion7();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO distrito.characters (id, account_id, name, name_normalized, zone_id, pos_x, pos_y, pos_z)
            VALUES (@id, @a, @name, @norm, @zone, @x, @y, @z)
            ON CONFLICT (name_normalized) DO NOTHING
            """, connection, tx))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("a", accountId);
            insert.Parameters.AddWithValue("name", name);
            insert.Parameters.AddWithValue("norm", NameRules.Normalize(name));
            insert.Parameters.AddWithValue("zone", ZoneIds.TestNeighborhood);
            insert.Parameters.AddWithValue("x", ZoneIds.NewCharacterSpawn.X);
            insert.Parameters.AddWithValue("y", ZoneIds.NewCharacterSpawn.Y);
            insert.Parameters.AddWithValue("z", ZoneIds.NewCharacterSpawn.Z);
            if (await insert.ExecuteNonQueryAsync(ct) == 0)
                return (CreateCharacterOutcome.NameTaken, null);
        }

        await tx.CommitAsync(ct);
        return (CreateCharacterOutcome.Created, new CharacterSummary(id, name, ZoneIds.TestNeighborhood));
    }

    /// <summary>Datos del personaje si pertenece a la cuenta.</summary>
    public async Task<ResolvedSession?> LoadForAccountAsync(Guid accountId, Guid characterId, CancellationToken ct)
    {
        // Entrar al mundo cuenta como actividad (sucesión de cuadrilla por inactividad).
        await using var cmd = db.CreateCommand("""
            UPDATE distrito.characters SET last_seen_at = now()
            WHERE id = @c AND account_id = @a
            RETURNING name, zone_id, pos_x, pos_y, pos_z, yaw
            """);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("a", accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new ResolvedSession(accountId, characterId, reader.GetString(0), reader.GetString(1),
            new CharacterState(reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5)));
    }

    public async Task<bool> SaveStateAsync(Guid characterId, CharacterState state, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("""
            UPDATE distrito.characters
            SET pos_x = @x, pos_y = @y, pos_z = @z, yaw = @yaw, updated_at = now(), last_seen_at = now()
            WHERE id = @c
            """);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("x", state.X);
        cmd.Parameters.AddWithValue("y", state.Y);
        cmd.Parameters.AddWithValue("z", state.Z);
        cmd.Parameters.AddWithValue("yaw", state.Yaw);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }
}
