using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Crews;

/// <summary>Guerras entre cuadrillas (ver WarRules).</summary>
public sealed partial class CrewService
{
    public Task<CrewActionResult> DeclareWarAsync(DeclareWarRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await RequireLeaderAsync(c, tx, r.CharacterId, ct);
            Guid target;
            string targetName, ourName;
            await using (var find = new NpgsqlCommand("""
                SELECT id, name, (SELECT name FROM distrito.crews WHERE id = @mine) FROM distrito.crews WHERE name_normalized = @n
                """, c, tx))
            {
                find.Parameters.AddWithValue("n", CrewRules.Normalize(r.TargetCrewName ?? ""));
                find.Parameters.AddWithValue("mine", crew.Id);
                await using var reader = await find.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new CrewRejected("crew_not_found", "No existe una cuadrilla con ese nombre.");
                (target, targetName, ourName) = (reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
            }
            if (target == crew.Id)
                throw new CrewRejected("invalid_target", "No podés declararle la guerra a tu propia cuadrilla.");

            var now = clock.GetUtcNow();
            await using (var busy = new NpgsqlCommand(
                "SELECT 1 FROM distrito.crew_wars WHERE attacker_crew = @me AND ends_at > @now", c, tx))
            {
                busy.Parameters.AddWithValue("me", crew.Id);
                busy.Parameters.AddWithValue("now", now);
                if (await busy.ExecuteScalarAsync(ct) is not null)
                    throw new CrewRejected("war_active", "Tu cuadrilla ya tiene una guerra declarada en curso.");
            }
            await using (var recent = new NpgsqlCommand("""
                SELECT 1 FROM distrito.crew_wars
                WHERE ((attacker_crew = @me AND defender_crew = @t) OR (attacker_crew = @t AND defender_crew = @me))
                  AND ends_at > @since
                """, c, tx))
            {
                recent.Parameters.AddWithValue("me", crew.Id);
                recent.Parameters.AddWithValue("t", target);
                recent.Parameters.AddWithValue("since", now - WarRules.RedeclareCooldown);
                if (await recent.ExecuteScalarAsync(ct) is not null)
                    throw new CrewRejected("war_cooldown", "Con esa cuadrilla hubo guerra hace poco: esperá 24 horas desde que terminó.");
            }

            await using (var insert = new NpgsqlCommand("""
                INSERT INTO distrito.crew_wars (id, attacker_crew, defender_crew, declared_by, declared_at, starts_at, ends_at)
                VALUES (@id, @me, @t, @by, @now, @start, @end)
                """, c, tx))
            {
                var start = now + WarRules.Warning;
                insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
                insert.Parameters.AddWithValue("me", crew.Id);
                insert.Parameters.AddWithValue("t", target);
                insert.Parameters.AddWithValue("by", r.CharacterId);
                insert.Parameters.AddWithValue("now", now);
                insert.Parameters.AddWithValue("start", start);
                insert.Parameters.AddWithValue("end", start + WarRules.Duration);
                await insert.ExecuteNonQueryAsync(ct);
            }
            await LogAsync(c, tx, crew.Id, "war_declared", r.CharacterId, $"«{targetName}»", ct);
            await LogAsync(c, tx, target, "war_received", null, $"«{ourName}»", ct);
            return $"Le declaraste la guerra a «{targetName}». Empieza en {WarRules.Warning.TotalMinutes:0} minutos y dura 24 horas.";
        });

    private async Task<List<WarView>> LoadWarsAsync(NpgsqlConnection c, NpgsqlTransaction? tx, Guid crewId, CancellationToken ct)
    {
        var list = new List<WarView>();
        await using var cmd = new NpgsqlCommand("""
            SELECT CASE WHEN w.attacker_crew = @crew THEN d.name ELSE a.name END, w.attacker_crew = @crew, w.starts_at, w.ends_at
              FROM distrito.crew_wars w
              JOIN distrito.crews a ON a.id = w.attacker_crew JOIN distrito.crews d ON d.id = w.defender_crew
             WHERE (w.attacker_crew = @crew OR w.defender_crew = @crew) AND w.ends_at > @now
             ORDER BY w.starts_at
            """, c, tx);
        cmd.Parameters.AddWithValue("crew", crewId);
        cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new WarView(reader.GetString(0), reader.GetBoolean(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return list;
    }

    /// <summary>Guerras activas ahora y los integrantes de esas cuadrillas (para el servidor de juego).</summary>
    public async Task<(List<WarPair> Wars, Dictionary<Guid, Guid> Members)> ActiveWarsAsync(CancellationToken ct)
    {
        var wars = new List<WarPair>();
        var members = new Dictionary<Guid, Guid>();
        await using var c = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT attacker_crew, defender_crew FROM distrito.crew_wars WHERE starts_at <= @now AND ends_at > @now;
            SELECT m.character_id, m.crew_id FROM distrito.crew_members m
             WHERE m.crew_id IN (SELECT attacker_crew FROM distrito.crew_wars WHERE starts_at <= @now AND ends_at > @now
                                 UNION SELECT defender_crew FROM distrito.crew_wars WHERE starts_at <= @now AND ends_at > @now);
            """, c);
        cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) wars.Add(new WarPair(reader.GetGuid(0), reader.GetGuid(1)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) members[reader.GetGuid(0)] = reader.GetGuid(1);
        return (wars, members);
    }
}
