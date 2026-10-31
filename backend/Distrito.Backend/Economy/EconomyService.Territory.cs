using System.Globalization;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>Territorio: influencia de las cuadrillas por zona y temporada (ver TerritoryRules).</summary>
public sealed partial class EconomyService
{
    private static async Task<Guid?> CrewOfCharacterAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT crew_id FROM distrito.crew_members WHERE character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        return await cmd.ExecuteScalarAsync(ct) as Guid?;
    }

    /// <summary>Suma influencia respetando el tope diario por zona. Devuelve los puntos que se sumaron.</summary>
    private async Task<int> GrantInfluenceAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, string zone, int points,
        string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", c, tx))
        {
            lockCmd.Parameters.AddWithValue("k", $"influence:{crewId}:{zone}");
            await lockCmd.ExecuteNonQueryAsync(ct);
        }
        int today;
        await using (var sum = new NpgsqlCommand("""
            SELECT COALESCE(sum(points), 0)::int FROM distrito.zone_influence
            WHERE crew_id = @crew AND zone = @zone AND created_at >= @day AND created_at <= @now
            """, c, tx))
        {
            sum.Parameters.AddWithValue("crew", crewId);
            sum.Parameters.AddWithValue("zone", zone);
            sum.Parameters.AddWithValue("day", TerritoryRules.DayStart(now));
            sum.Parameters.AddWithValue("now", now);
            today = (int)(await sum.ExecuteScalarAsync(ct))!;
        }
        var grant = TerritoryRules.Grantable(points, today);
        if (grant <= 0)
            return 0;
        await using var insert = new NpgsqlCommand("""
            INSERT INTO distrito.zone_influence (season, zone, crew_id, points, reason, created_at)
            VALUES (@season, @zone, @crew, @points, @reason, @now)
            """, c, tx);
        insert.Parameters.AddWithValue("season", TerritoryRules.SeasonAt(now));
        insert.Parameters.AddWithValue("zone", zone);
        insert.Parameters.AddWithValue("crew", crewId);
        insert.Parameters.AddWithValue("points", grant);
        insert.Parameters.AddWithValue("reason", reason);
        insert.Parameters.AddWithValue("now", now);
        await insert.ExecuteNonQueryAsync(ct);
        return grant;
    }

    private sealed record Standing(Guid Crew, string CrewName, int Points, DateTimeOffset LastGain);

    private static async Task<Dictionary<string, List<Standing>>> StandingsAsync(NpgsqlConnection c, NpgsqlTransaction tx, int season, CancellationToken ct)
    {
        var result = TerritoryRules.Zones.ToDictionary(z => z.Code, _ => new List<Standing>());
        await using var cmd = new NpgsqlCommand("""
            SELECT i.zone, i.crew_id, cr.name, sum(i.points)::int, max(i.created_at)
              FROM distrito.zone_influence i JOIN distrito.crews cr ON cr.id = i.crew_id
             WHERE i.season = @season
             GROUP BY i.zone, i.crew_id, cr.name
            """, c, tx);
        cmd.Parameters.AddWithValue("season", season);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (result.TryGetValue(reader.GetString(0), out var list))
                list.Add(new Standing(reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3), reader.GetFieldValue<DateTimeOffset>(4)));
        return result;
    }

    private static Standing? ControllerOf(List<Standing> standings)
    {
        var winner = TerritoryRules.Controller(standings.Select(s => (s.Crew, s.Points, s.LastGain)));
        return winner is { } w ? standings.First(s => s.Crew == w.Crew) : null;
    }

    private async Task<Guid?> ControllerOfZoneAsync(NpgsqlConnection c, NpgsqlTransaction tx, string zone, CancellationToken ct)
    {
        var standings = await StandingsAsync(c, tx, TerritoryRules.SeasonAt(clock.GetUtcNow()), ct);
        return ControllerOf(standings[zone])?.Crew;
    }

    private async Task<TerritoryView> LoadTerritoryAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var season = TerritoryRules.SeasonAt(clock.GetUtcNow());
        var standings = await StandingsAsync(c, tx, season, ct);
        var myCrew = await CrewOfCharacterAsync(c, tx, characterId, ct);
        var zones = TerritoryRules.Zones.Select(z =>
        {
            var list = standings[z.Code];
            var controller = ControllerOf(list);
            var mine = myCrew is { } id ? list.FirstOrDefault(s => s.Crew == id)?.Points ?? 0 : 0;
            return new ZoneView(z.Code, z.Name, controller?.CrewName, controller?.Points ?? 0, mine);
        }).ToList();
        return new TerritoryView(season, TerritoryRules.SeasonEnd(season), zones);
    }

    /// <summary>
    /// Cierra las temporadas terminadas que falten: guarda el ganador de cada zona y lo anota en el historial de su cuadrilla.
    /// La influencia nueva ya cuenta para la temporada siguiente (cada registro lleva su número de temporada).
    /// </summary>
    public async Task<int> CloseSeasonsAsync(CancellationToken ct)
    {
        var current = TerritoryRules.SeasonAt(clock.GetUtcNow());
        if (current <= 1)
            return 0;
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('close-seasons'))", c, tx))
            await lockCmd.ExecuteNonQueryAsync(ct);

        var closed = 0;
        for (var season = Math.Max(1, current - 4); season < current; season++)
        {
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM distrito.zone_season_results WHERE season = @s LIMIT 1", c, tx))
            {
                exists.Parameters.AddWithValue("s", season);
                if (await exists.ExecuteScalarAsync(ct) is not null)
                    continue;
            }
            var standings = await StandingsAsync(c, tx, season, ct);
            foreach (var (zone, name) in TerritoryRules.Zones)
            {
                var winner = ControllerOf(standings[zone]);
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO distrito.zone_season_results (season, zone, crew_id, points) VALUES (@s, @z, @crew, @p);
                    """, c, tx);
                insert.Parameters.AddWithValue("s", season);
                insert.Parameters.AddWithValue("z", zone);
                insert.Parameters.AddWithValue("crew", (object?)winner?.Crew ?? DBNull.Value);
                insert.Parameters.AddWithValue("p", winner?.Points ?? 0);
                await insert.ExecuteNonQueryAsync(ct);
                if (winner is null)
                    continue;
                await using var log = new NpgsqlCommand(
                    "INSERT INTO distrito.crew_events (crew_id, kind, character_id, detail) VALUES (@crew, 'season_won', NULL, @d)", c, tx);
                log.Parameters.AddWithValue("crew", winner.Crew);
                log.Parameters.AddWithValue("d", string.Create(CultureInfo.InvariantCulture, $"{name}, temporada {season} ({winner.Points} de influencia)"));
                await log.ExecuteNonQueryAsync(ct);
            }
            closed++;
        }
        await tx.CommitAsync(ct);
        return closed;
    }
}
