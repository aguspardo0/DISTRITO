using System.Text.Json;
using Distrito.Shared.Life;
using Npgsql;

namespace Distrito.Backend.Life;

/// <summary>
/// Guarda a los habitantes de Bellmont (vida autónoma de los NPC). Quien decide qué hace cada uno es el servidor del
/// juego; acá solo se guarda quién es, su último estado y los hechos importantes. La primera vez que se piden, los crea
/// desde config/vida.json (una sola vez, aunque lo pidan varios a la vez).
/// </summary>
public sealed class LifeStore(NpgsqlDataSource db, IConfiguration config, TimeProvider clock, ILogger<LifeStore> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string ConfigFile => config.GetValue<string>("Life:ConfigFile") ?? Path.Combine(AppContext.BaseDirectory, "vida.json");

    public async Task<LifeSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var configJson = File.Exists(ConfigFile) ? await File.ReadAllTextAsync(ConfigFile, ct) : "{}";
        await using var c = await db.OpenConnectionAsync(ct);
        await EnsureCreatedAsync(c, configJson, ct);

        var residents = new List<ResidentRecord>();
        await using var cmd = new NpgsqlCommand("""
            SELECT r.identity, s.state FROM distrito.npc_residents r
              LEFT JOIN distrito.npc_resident_states s ON s.resident_id = r.id
             ORDER BY r.created_at, r.id
            """, c);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var identity = JsonSerializer.Deserialize<Resident>(reader.GetString(0), Json)!;
            var state = reader.IsDBNull(1) ? null : JsonSerializer.Deserialize<ResidentState>(reader.GetString(1), Json);
            residents.Add(new ResidentRecord(identity, state));
        }
        return new LifeSnapshot(configJson, residents);
    }

    private async Task EnsureCreatedAsync(NpgsqlConnection c, string configJson, CancellationToken ct)
    {
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(19520036)", c, tx))
            await lockCmd.ExecuteNonQueryAsync(ct);
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM distrito.npc_residents", c, tx))
            if ((long)(await count.ExecuteScalarAsync(ct))! > 0)
                return;

        LifeConfig cfg;
        try
        {
            cfg = LifeConfig.Parse(configJson);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "vida.json no se puede leer: no se crean habitantes");
            return;
        }
        var errors = cfg.Validate();
        if (errors.Count > 0 || cfg.Population == 0)
        {
            logger.LogError("vida.json con errores, no se crean habitantes: {Errors}", string.Join(" | ", errors));
            return;
        }

        var now = clock.GetUtcNow();
        var professions = cfg.Professions.ToDictionary(p => p.Code, p => p.Name);
        var districts = cfg.Districts.ToDictionary(d => d.Code, d => d.Name);
        var places = cfg.Places.ToDictionary(p => p.Code, p => p.Name);
        foreach (var r in ResidentGenerator.Generate(cfg))
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO distrito.npc_residents (id, identity) VALUES (@id, @identity::jsonb);
                INSERT INTO distrito.npc_resident_states (resident_id, state) VALUES (@id, @state::jsonb);
                INSERT INTO distrito.npc_resident_events (resident_id, at, kind, text) VALUES (@id, @at, 'llego', @text);
                """, c, tx);
            insert.Parameters.AddWithValue("id", r.Id);
            insert.Parameters.AddWithValue("identity", JsonSerializer.Serialize(r, Json));
            insert.Parameters.AddWithValue("state", JsonSerializer.Serialize(ResidentGenerator.InitialState(r, now), Json));
            insert.Parameters.AddWithValue("at", now);
            var job = r.Workplace is { } w ? $"{professions[r.Profession].ToLowerInvariant()} en {places.GetValueOrDefault(w, w)}" : "sin trabajo fijo";
            insert.Parameters.AddWithValue("text", $"{r.FullName}, {r.Age} años, vive en {districts[r.District]} ({job}).");
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        logger.LogInformation("Vida de los NPC: se crearon {Count} habitantes de Bellmont", cfg.Population);
    }

    /// <summary>Guarda el último estado de varios habitantes y sus hechos importantes, todo junto.</summary>
    public async Task SaveAsync(LifeStateBatch batch, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var s in batch.States)
        {
            await using var upsert = new NpgsqlCommand("""
                INSERT INTO distrito.npc_resident_states (resident_id, updated_at, state)
                SELECT @id, @at, @state::jsonb WHERE EXISTS (SELECT 1 FROM distrito.npc_residents WHERE id = @id)
                ON CONFLICT (resident_id) DO UPDATE SET updated_at = EXCLUDED.updated_at, state = EXCLUDED.state
                """, c, tx);
            upsert.Parameters.AddWithValue("id", s.ResidentId);
            upsert.Parameters.AddWithValue("at", clock.GetUtcNow());
            upsert.Parameters.AddWithValue("state", JsonSerializer.Serialize(s, Json));
            await upsert.ExecuteNonQueryAsync(ct);
        }
        foreach (var e in batch.Events)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO distrito.npc_resident_events (resident_id, at, kind, text)
                SELECT @id, @at, @kind, @text WHERE EXISTS (SELECT 1 FROM distrito.npc_residents WHERE id = @id)
                """, c, tx);
            insert.Parameters.AddWithValue("id", e.ResidentId);
            insert.Parameters.AddWithValue("at", e.At);
            insert.Parameters.AddWithValue("kind", e.Kind);
            insert.Parameters.AddWithValue("text", e.Text);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>Los últimos hechos de un habitante (para la herramienta de inspección).</summary>
    public async Task<List<ResidentEvent>> HistoryAsync(Guid resident, int limit, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            "SELECT at, kind, text FROM distrito.npc_resident_events WHERE resident_id = @id ORDER BY at DESC, id DESC LIMIT @n");
        cmd.Parameters.AddWithValue("id", resident);
        cmd.Parameters.AddWithValue("n", Math.Clamp(limit, 1, 100));
        var list = new List<ResidentEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new ResidentEvent(resident, reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetString(2)));
        return list;
    }
}
