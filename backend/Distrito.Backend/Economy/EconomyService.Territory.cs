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

    // ---------- Eventos de distrito (config/eventos.json, se relee si cambia) ----------

    private readonly object _eventsLock = new();
    private List<DistrictEvent> _events = [];
    private DateTime _eventsStamp = DateTime.MinValue;

    private string EventsFile => config.GetValue<string>("Events:File") ?? Path.Combine(AppContext.BaseDirectory, "eventos.json");

    public List<DistrictEvent> DistrictEventList()
    {
        lock (_eventsLock)
        {
            var file = EventsFile;
            var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            if (stamp != _eventsStamp)
            {
                _eventsStamp = stamp;
                if (stamp == DateTime.MinValue)
                    _events = [];
                else
                {
                    var (events, errors) = DistrictEvents.Parse(File.ReadAllText(file));
                    if (errors.Count == 0)
                        _events = events;
                    else
                        Console.Error.WriteLine($"[DISTRITO] eventos.json con errores, se mantienen los anteriores: {string.Join(" | ", errors)}");
                }
            }
            return _events;
        }
    }

    public List<EventView> UpcomingEvents(TimeSpan window)
    {
        var now = clock.GetUtcNow();
        return DistrictEvents.Occurrences(DistrictEventList(), now, now + window);
    }

    private EventView? ActiveEventIn(string zone)
    {
        var now = clock.GetUtcNow();
        return DistrictEvents.Occurrences(DistrictEventList(), now, now.AddSeconds(1)).FirstOrDefault(e => e.Zone == zone && e.IsActive(now));
    }

    /// <summary>Influencia de un evento: tiene tope propio por cuadrilla y vez del evento, y no cuenta para el tope diario.</summary>
    private async Task<int> GrantEventInfluenceAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, EventView e, CancellationToken ct)
    {
        var reason = $"evento:{e.OccurrenceId}";
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", c, tx))
        {
            lockCmd.Parameters.AddWithValue("k", $"influence:{crewId}:{e.Zone}");
            await lockCmd.ExecuteNonQueryAsync(ct);
        }
        int already;
        await using (var sum = new NpgsqlCommand(
            "SELECT COALESCE(sum(points), 0)::int FROM distrito.zone_influence WHERE crew_id = @crew AND reason = @r", c, tx))
        {
            sum.Parameters.AddWithValue("crew", crewId);
            sum.Parameters.AddWithValue("r", reason);
            already = (int)(await sum.ExecuteScalarAsync(ct))!;
        }
        var grant = Math.Clamp(e.CapPerCrew - already, 0, e.PointsPerContract);
        if (grant <= 0)
            return 0;
        await InsertInfluenceAsync(c, tx, crewId, e.Zone, grant, reason, ct);
        return grant;
    }

    private async Task InsertInfluenceAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, string zone, int points, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO distrito.zone_influence (season, zone, crew_id, points, reason, created_at)
            VALUES (@season, @zone, @crew, @points, @reason, @now)
            """, c, tx);
        insert.Parameters.AddWithValue("season", TerritoryRules.SeasonAt(now));
        insert.Parameters.AddWithValue("zone", zone);
        insert.Parameters.AddWithValue("crew", crewId);
        insert.Parameters.AddWithValue("points", points);
        insert.Parameters.AddWithValue("reason", reason);
        insert.Parameters.AddWithValue("now", now);
        await insert.ExecuteNonQueryAsync(ct);
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
            WHERE crew_id = @crew AND zone = @zone AND created_at >= @day AND created_at <= @now AND reason NOT LIKE 'evento:%'
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
        await InsertInfluenceAsync(c, tx, crewId, zone, grant, reason, ct);
        return grant;
    }

    /// <summary>
    /// Posición en una zona de una cuadrilla sola o de una coalición (Crew = id de la cuadrilla o de la coalición).
    /// La influencia de una coalición es la suma de sus cuadrillas al 75 %.
    /// </summary>
    private sealed record Standing(Guid Crew, string CrewName, int Points, DateTimeOffset LastGain, bool IsCoalition = false);

    private sealed record CrewPoints(string Zone, Guid Crew, string CrewName, int Points, DateTimeOffset LastGain, Guid? Coalition, string? CoalitionName);

    private static async Task<List<CrewPoints>> CrewPointsAsync(NpgsqlConnection c, NpgsqlTransaction tx, int season, CancellationToken ct)
    {
        var list = new List<CrewPoints>();
        await using var cmd = new NpgsqlCommand("""
            SELECT i.zone, i.crew_id, cr.name, sum(i.points)::int, max(i.created_at), co.id, co.name
              FROM distrito.zone_influence i JOIN distrito.crews cr ON cr.id = i.crew_id
              LEFT JOIN distrito.coalition_members m ON m.crew_id = i.crew_id
              LEFT JOIN distrito.coalitions co ON co.id = m.coalition_id
             WHERE i.season = @season
             GROUP BY i.zone, i.crew_id, cr.name, co.id, co.name
            """, c, tx);
        cmd.Parameters.AddWithValue("season", season);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new CrewPoints(reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return list;
    }

    private static async Task<Dictionary<string, List<Standing>>> StandingsAsync(NpgsqlConnection c, NpgsqlTransaction tx, int season, CancellationToken ct)
    {
        var result = TerritoryRules.Zones.ToDictionary(z => z.Code, _ => new List<Standing>());
        foreach (var group in (await CrewPointsAsync(c, tx, season, ct)).GroupBy(p => (p.Zone, Key: p.Coalition ?? p.Crew)))
        {
            if (!result.TryGetValue(group.Key.Zone, out var list))
                continue;
            var first = group.First();
            list.Add(first.Coalition is { } coalition
                ? new Standing(coalition, $"Coalición {first.CoalitionName}", Distrito.Shared.Crews.CoalitionRules.CombinedInfluence(group.Select(p => p.Points)),
                    group.Max(p => p.LastGain), IsCoalition: true)
                : new Standing(first.Crew, first.CrewName, first.Points, first.LastGain));
        }
        return result;
    }

    /// <summary>La "facción" de una cuadrilla para el territorio: su coalición si tiene, si no ella misma.</summary>
    private static async Task<Guid?> FactionOfCrewAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid? crewId, CancellationToken ct)
    {
        if (crewId is not { } id)
            return null;
        await using var cmd = new NpgsqlCommand("SELECT coalition_id FROM distrito.coalition_members WHERE crew_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", id);
        return await cmd.ExecuteScalarAsync(ct) as Guid? ?? id;
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
        var myFaction = await FactionOfCrewAsync(c, tx, await CrewOfCharacterAsync(c, tx, characterId, ct), ct);
        var zones = TerritoryRules.Zones.Select(z =>
        {
            var list = standings[z.Code];
            var controller = ControllerOf(list);
            var mine = myFaction is { } id ? list.FirstOrDefault(s => s.Crew == id)?.Points ?? 0 : 0;
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
                    INSERT INTO distrito.zone_season_results (season, zone, crew_id, coalition_id, points) VALUES (@s, @z, @crew, @co, @p);
                    """, c, tx);
                insert.Parameters.AddWithValue("s", season);
                insert.Parameters.AddWithValue("z", zone);
                insert.Parameters.AddWithValue("crew", winner is { IsCoalition: false } ? winner.Crew : DBNull.Value);
                insert.Parameters.AddWithValue("co", winner is { IsCoalition: true } ? winner.Crew : DBNull.Value);
                insert.Parameters.AddWithValue("p", winner?.Points ?? 0);
                await insert.ExecuteNonQueryAsync(ct);
                if (winner is null)
                    continue;
                var crews = new List<Guid> { winner.Crew };
                if (winner.IsCoalition)
                {
                    crews.Clear();
                    await using var members = new NpgsqlCommand("SELECT crew_id FROM distrito.coalition_members WHERE coalition_id = @co", c, tx);
                    members.Parameters.AddWithValue("co", winner.Crew);
                    await using var reader = await members.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) crews.Add(reader.GetGuid(0));
                }
                foreach (var crew in crews)
                {
                    await using var log = new NpgsqlCommand(
                        "INSERT INTO distrito.crew_events (crew_id, kind, character_id, detail) VALUES (@crew, 'season_won', NULL, @d)", c, tx);
                    log.Parameters.AddWithValue("crew", crew);
                    log.Parameters.AddWithValue("d", string.Create(CultureInfo.InvariantCulture,
                        $"{name}, temporada {season} ({winner.Points} de influencia{(winner.IsCoalition ? $", con la {winner.CrewName}" : "")})"));
                    await log.ExecuteNonQueryAsync(ct);
                }
            }
            closed++;
        }
        await tx.CommitAsync(ct);
        return closed;
    }
}
