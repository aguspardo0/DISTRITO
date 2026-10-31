using Distrito.Shared.Crews;
using Npgsql;

namespace Distrito.Backend.Crews;

/// <summary>Coaliciones entre cuadrillas (Etapa E). Las maneja el jefe de cada cuadrilla.</summary>
public sealed partial class CrewService
{
    private async Task<Membership> RequireLeaderAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var crew = await CrewOfAsync(c, tx, characterId, ct)
            ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
        await LockCrewAsync(c, tx, crew.Id, ct);
        await ApplyInactivitySuccessionAsync(c, tx, crew.Id, ct);
        crew = await CrewOfAsync(c, tx, characterId, ct) ?? crew;
        if (crew.Role != CrewRoles.Leader)
            throw new CrewRejected("not_leader", "Las coaliciones las maneja el jefe.");
        return crew;
    }

    private static async Task<Guid?> CoalitionOfCrewAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT coalition_id FROM distrito.coalition_members WHERE crew_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", crewId);
        return await cmd.ExecuteScalarAsync(ct) as Guid?;
    }

    private async Task EnsureNoCooldownAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, string who, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT coalition_left_at FROM distrito.crews WHERE id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", crewId);
        if (await cmd.ExecuteScalarAsync(ct) is DateTime left &&
            new DateTimeOffset(DateTime.SpecifyKind(left, DateTimeKind.Utc)) + CoalitionRules.RejoinCooldown > clock.GetUtcNow())
            throw new CrewRejected("coalition_cooldown", $"{who} salió de una coalición hace menos de 24 horas.");
    }

    private static async Task<int> CoalitionSizeAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid coalitionId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT count(*)::int FROM distrito.coalition_members WHERE coalition_id = @id", c, tx);
        cmd.Parameters.AddWithValue("id", coalitionId);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public Task<CrewActionResult> InviteToCoalitionAsync(CoalitionInviteRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await RequireLeaderAsync(c, tx, r.CharacterId, ct);
            Guid target;
            string targetName;
            await using (var find = new NpgsqlCommand("SELECT id, name FROM distrito.crews WHERE name_normalized = @n", c, tx))
            {
                find.Parameters.AddWithValue("n", CrewRules.Normalize(r.TargetCrewName ?? ""));
                await using var reader = await find.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new CrewRejected("crew_not_found", "No existe una cuadrilla con ese nombre.");
                (target, targetName) = (reader.GetGuid(0), reader.GetString(1));
            }
            if (target == crew.Id)
                throw new CrewRejected("invalid_target", "No podés invitar a tu propia cuadrilla.");
            if (await CoalitionOfCrewAsync(c, tx, target, ct) is not null)
                throw new CrewRejected("target_in_coalition", "Esa cuadrilla ya está en una coalición.");

            var mine = await CoalitionOfCrewAsync(c, tx, crew.Id, ct);
            var proposed = (r.CoalitionName ?? "").Trim();
            if (mine is { } coalitionId)
            {
                if (await CoalitionSizeAsync(c, tx, coalitionId, ct) >= CoalitionRules.MaxCrews)
                    throw new CrewRejected("coalition_full", $"La coalición ya tiene {CoalitionRules.MaxCrews} cuadrillas.");
            }
            else
            {
                await EnsureNoCooldownAsync(c, tx, crew.Id, "Tu cuadrilla", ct);
                if (CrewRules.ValidateName(proposed) is { } invalid)
                    throw new CrewRejected("invalid_name", invalid.Replace("de la cuadrilla", "de la coalición"));
            }

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.coalition_invitations (from_crew_id, to_crew_id, proposed_name, expires_at)
                VALUES (@from, @to, @name, @exp)
                ON CONFLICT (from_crew_id, to_crew_id) DO UPDATE SET proposed_name = EXCLUDED.proposed_name, expires_at = EXCLUDED.expires_at
                """, c, tx);
            cmd.Parameters.AddWithValue("from", crew.Id);
            cmd.Parameters.AddWithValue("to", target);
            cmd.Parameters.AddWithValue("name", proposed.Length > 0 ? proposed : "-");
            cmd.Parameters.AddWithValue("exp", clock.GetUtcNow() + CoalitionRules.InvitationLifetime);
            await cmd.ExecuteNonQueryAsync(ct);
            await LogAsync(c, tx, crew.Id, "coalition_invited", r.CharacterId, targetName, ct);
            return $"Invitaste a «{targetName}» a la coalición. Su jefe tiene 24 horas para responder.";
        });

    public Task<CrewActionResult> RespondCoalitionAsync(CoalitionResponseRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await RequireLeaderAsync(c, tx, r.CharacterId, ct);
            string proposed;
            await using (var take = new NpgsqlCommand("""
                DELETE FROM distrito.coalition_invitations
                WHERE from_crew_id = @from AND to_crew_id = @to AND expires_at > @now
                RETURNING proposed_name
                """, c, tx))
            {
                take.Parameters.AddWithValue("from", r.FromCrewId);
                take.Parameters.AddWithValue("to", crew.Id);
                take.Parameters.AddWithValue("now", clock.GetUtcNow());
                proposed = await take.ExecuteScalarAsync(ct) as string
                    ?? throw new CrewRejected("no_invitation", "Esa invitación ya no existe o venció.");
            }
            if (!r.Accept)
                return "Rechazaste la invitación a la coalición.";

            await LockCrewAsync(c, tx, r.FromCrewId, ct);
            if (await CoalitionOfCrewAsync(c, tx, crew.Id, ct) is not null)
                throw new CrewRejected("already_in_coalition", "Tu cuadrilla ya está en una coalición.");
            await EnsureNoCooldownAsync(c, tx, crew.Id, "Tu cuadrilla", ct);

            var coalitionId = await CoalitionOfCrewAsync(c, tx, r.FromCrewId, ct);
            if (coalitionId is { } existing)
            {
                if (await CoalitionSizeAsync(c, tx, existing, ct) >= CoalitionRules.MaxCrews)
                    throw new CrewRejected("coalition_full", $"La coalición ya tiene {CoalitionRules.MaxCrews} cuadrillas.");
            }
            else
            {
                await EnsureNoCooldownAsync(c, tx, r.FromCrewId, "La cuadrilla que invitó", ct);
                var id = Guid.CreateVersion7();
                await using var create = new NpgsqlCommand("""
                    INSERT INTO distrito.coalitions (id, name, name_normalized) VALUES (@id, @name, @norm) ON CONFLICT (name_normalized) DO NOTHING;
                    """, c, tx);
                create.Parameters.AddWithValue("id", id);
                create.Parameters.AddWithValue("name", proposed);
                create.Parameters.AddWithValue("norm", CrewRules.Normalize(proposed));
                if (await create.ExecuteNonQueryAsync(ct) == 0)
                    throw new CrewRejected("name_taken", "Ya existe una coalición con ese nombre: pedile que invite con otro.");
                await AddToCoalitionAsync(c, tx, id, r.FromCrewId, ct);
                coalitionId = id;
            }
            await AddToCoalitionAsync(c, tx, coalitionId.Value, crew.Id, ct);
            return "¡Tu cuadrilla entró a la coalición!";
        });

    private static async Task AddToCoalitionAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid coalitionId, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO distrito.coalition_members (coalition_id, crew_id) VALUES (@co, @crew);
            DELETE FROM distrito.coalition_invitations WHERE to_crew_id = @crew;
            SELECT name FROM distrito.coalitions WHERE id = @co;
            """, c, tx);
        cmd.Parameters.AddWithValue("co", coalitionId);
        cmd.Parameters.AddWithValue("crew", crewId);
        var name = (string)(await cmd.ExecuteScalarAsync(ct))!;
        await LogAsync(c, tx, crewId, "coalition_joined", null, name, ct);
    }

    public Task<CrewActionResult> LeaveCoalitionAsync(CoalitionLeaveRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await RequireLeaderAsync(c, tx, r.CharacterId, ct);
            var coalitionId = await CoalitionOfCrewAsync(c, tx, crew.Id, ct)
                ?? throw new CrewRejected("no_coalition", "Tu cuadrilla no está en una coalición.");
            await LeaveCoalitionInternalAsync(c, tx, coalitionId, crew.Id, ct);
            return "Tu cuadrilla salió de la coalición. Puede entrar a otra en 24 horas.";
        });

    /// <summary>Sale de la coalición; si queda una sola cuadrilla, se disuelve (sin espera para la que queda).</summary>
    private async Task LeaveCoalitionInternalAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid coalitionId, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT name FROM distrito.coalitions WHERE id = @co FOR UPDATE;
            """, c, tx);
        cmd.Parameters.AddWithValue("co", coalitionId);
        var name = (string)(await cmd.ExecuteScalarAsync(ct))!;
        await using (var leave = new NpgsqlCommand("""
            DELETE FROM distrito.coalition_members WHERE crew_id = @crew;
            UPDATE distrito.crews SET coalition_left_at = @now WHERE id = @crew;
            """, c, tx))
        {
            leave.Parameters.AddWithValue("crew", crewId);
            leave.Parameters.AddWithValue("now", clock.GetUtcNow());
            await leave.ExecuteNonQueryAsync(ct);
        }
        await LogAsync(c, tx, crewId, "coalition_left", null, name, ct);

        if (await CoalitionSizeAsync(c, tx, coalitionId, ct) <= 1)
        {
            var remaining = new List<Guid>();
            await using (var rest = new NpgsqlCommand("SELECT crew_id FROM distrito.coalition_members WHERE coalition_id = @co", c, tx))
            {
                rest.Parameters.AddWithValue("co", coalitionId);
                await using var reader = await rest.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) remaining.Add(reader.GetGuid(0));
            }
            foreach (var other in remaining)
                await LogAsync(c, tx, other, "coalition_left", null, $"{name} (se disolvió)", ct);
            await using var dissolve = new NpgsqlCommand("""
                DELETE FROM distrito.coalition_members WHERE coalition_id = @co;
                UPDATE distrito.coalitions SET name_normalized = name_normalized || ':' || id::text WHERE id = @co;
                """, c, tx);
            dissolve.Parameters.AddWithValue("co", coalitionId);
            await dissolve.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Datos de coalición para el panel de la cuadrilla.</summary>
    private async Task<(CoalitionView? View, List<CoalitionInviteView> Invites)> LoadCoalitionAsync(NpgsqlConnection c, NpgsqlTransaction? tx, Guid crewId, CancellationToken ct)
    {
        Guid? id = null;
        string? name = null;
        var crews = new List<string>();
        DateTimeOffset? cooldown = null;
        var invites = new List<CoalitionInviteView>();
        await using var cmd = new NpgsqlCommand("""
            SELECT co.id, co.name FROM distrito.coalition_members m JOIN distrito.coalitions co ON co.id = m.coalition_id WHERE m.crew_id = @crew;
            SELECT cr.name FROM distrito.coalition_members m JOIN distrito.crews cr ON cr.id = m.crew_id
             WHERE m.coalition_id = (SELECT coalition_id FROM distrito.coalition_members WHERE crew_id = @crew) ORDER BY m.joined_at;
            SELECT coalition_left_at FROM distrito.crews WHERE id = @crew;
            SELECT i.from_crew_id, cr.name, COALESCE(co.name, i.proposed_name)
              FROM distrito.coalition_invitations i JOIN distrito.crews cr ON cr.id = i.from_crew_id
              LEFT JOIN distrito.coalition_members m ON m.crew_id = i.from_crew_id
              LEFT JOIN distrito.coalitions co ON co.id = m.coalition_id
             WHERE i.to_crew_id = @crew AND i.expires_at > @now ORDER BY i.expires_at;
            """, c, tx);
        cmd.Parameters.AddWithValue("crew", crewId);
        cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) (id, name) = (reader.GetGuid(0), reader.GetString(1));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) crews.Add(reader.GetString(0));
        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct) && !reader.IsDBNull(0))
        {
            var until = reader.GetFieldValue<DateTimeOffset>(0) + CoalitionRules.RejoinCooldown;
            if (until > clock.GetUtcNow()) cooldown = until;
        }
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) invites.Add(new CoalitionInviteView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return (new CoalitionView(id, name, crews, cooldown), invites);
    }
}
