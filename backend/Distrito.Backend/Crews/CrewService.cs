using Distrito.Shared.Crews;
using Npgsql;

namespace Distrito.Backend.Crews;

/// <summary>
/// Cuadrillas: crear, invitar, responder invitaciones y salir.
/// Cada acción es una transacción; la fila de la cuadrilla se bloquea para que el cupo y el liderazgo
/// no se rompan con pedidos simultáneos.
/// </summary>
public sealed partial class CrewService(NpgsqlDataSource db, TimeProvider clock)
{
    private sealed class CrewRejected(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    public async Task<CrewStatus> GetStatusAsync(Guid characterId, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await CrewOfAsync(c, tx, characterId, ct) is { } crew)
        {
            await ApplyInactivitySuccessionAsync(c, tx, crew.Id, ct);
            await CloseExpiredVotesAsync(c, tx, crew.Id, ct);
        }
        var status = await LoadStatusAsync(c, tx, characterId, ct);
        await tx.CommitAsync(ct);
        return status;
    }

    public Task<CrewActionResult> CreateAsync(CreateCrewRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var invalid = CrewRules.ValidateName(r.Name);
            if (invalid is not null)
                throw new CrewRejected("invalid_name", invalid);
            await EnsureCharacterExistsAsync(c, tx, r.CharacterId, ct);
            if (await CrewOfAsync(c, tx, r.CharacterId, ct) is not null)
                throw new CrewRejected("already_in_crew", "Ya estás en una cuadrilla.");

            var crewId = Guid.CreateVersion7();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.crews (id, name, name_normalized, leader_character_id)
                VALUES (@id, @name, @norm, @c)
                ON CONFLICT (name_normalized) DO NOTHING
                """, c, tx);
            cmd.Parameters.AddWithValue("id", crewId);
            cmd.Parameters.AddWithValue("name", r.Name);
            cmd.Parameters.AddWithValue("norm", CrewRules.Normalize(r.Name));
            cmd.Parameters.AddWithValue("c", r.CharacterId);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
                throw new CrewRejected("name_taken", "Ya existe una cuadrilla con ese nombre.");

            await AddMemberAsync(c, tx, crewId, r.CharacterId, "leader", ct);
            await DeleteInvitationsForAsync(c, tx, r.CharacterId, ct);
            await LogAsync(c, tx, crewId, "founded", r.CharacterId, r.Name, ct);
            return $"Fundaste la cuadrilla {r.Name}. Está en formación hasta llegar a {CrewRules.MembersToForm} integrantes.";
        });

    public Task<CrewActionResult> InviteAsync(InviteToCrewRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            if (r.CharacterId == r.TargetCharacterId)
                throw new CrewRejected("invalid_target", "No podés invitarte a vos mismo.");
            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);
            await ApplyInactivitySuccessionAsync(c, tx, crew.Id, ct);
            crew = await CrewOfAsync(c, tx, r.CharacterId, ct) ?? crew;
            if (!(await PermissionsAsync(c, tx, crew.Id, ct)).CanInvite(crew.Role))
                throw new CrewRejected("not_allowed", "Tu rol no tiene permiso para invitar en esta cuadrilla.");
            await EnsureCharacterExistsAsync(c, tx, r.TargetCharacterId, ct);
            if (await CrewOfAsync(c, tx, r.TargetCharacterId, ct) is not null)
                throw new CrewRejected("target_in_crew", "Esa persona ya está en una cuadrilla.");
            var capacity = await CapacityAsync(c, tx, crew.Id, ct);
            if (await MemberCountAsync(c, tx, crew.Id, ct) >= capacity)
                throw new CrewRejected("crew_full", $"La cuadrilla está completa ({capacity}).");

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.crew_invitations (crew_id, character_id, invited_by, expires_at)
                VALUES (@crew, @target, @by, @exp)
                ON CONFLICT (crew_id, character_id) DO UPDATE SET invited_by = EXCLUDED.invited_by, expires_at = EXCLUDED.expires_at
                """, c, tx);
            cmd.Parameters.AddWithValue("crew", crew.Id);
            cmd.Parameters.AddWithValue("target", r.TargetCharacterId);
            cmd.Parameters.AddWithValue("by", r.CharacterId);
            cmd.Parameters.AddWithValue("exp", clock.GetUtcNow() + CrewRules.InvitationLifetime);
            await cmd.ExecuteNonQueryAsync(ct);
            return "Invitación enviada.";
        });

    public Task<CrewActionResult> RespondAsync(CrewInvitationResponse r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            await LockCrewAsync(c, tx, r.CrewId, ct);
            await using (var find = new NpgsqlCommand("""
                SELECT 1 FROM distrito.crew_invitations
                WHERE crew_id = @crew AND character_id = @c AND expires_at > @now
                """, c, tx))
            {
                find.Parameters.AddWithValue("crew", r.CrewId);
                find.Parameters.AddWithValue("c", r.CharacterId);
                find.Parameters.AddWithValue("now", clock.GetUtcNow());
                if (await find.ExecuteScalarAsync(ct) is null)
                    throw new CrewRejected("no_invitation", "La invitación ya no es válida.");
            }

            if (!r.Accept)
            {
                await DeleteInvitationAsync(c, tx, r.CrewId, r.CharacterId, ct);
                return "Rechazaste la invitación.";
            }

            if (await CrewOfAsync(c, tx, r.CharacterId, ct) is not null)
                throw new CrewRejected("already_in_crew", "Ya estás en una cuadrilla.");
            if (await MemberCountAsync(c, tx, r.CrewId, ct) >= await CapacityAsync(c, tx, r.CrewId, ct))
                throw new CrewRejected("crew_full", "La cuadrilla está completa.");

            await AddMemberAsync(c, tx, r.CrewId, r.CharacterId, "member", ct);
            await DeleteInvitationsForAsync(c, tx, r.CharacterId, ct);
            await LogAsync(c, tx, r.CrewId, "joined", r.CharacterId, "", ct);
            var count = await MemberCountAsync(c, tx, r.CrewId, ct);
            return count == CrewRules.MembersToForm
                ? "Te uniste a la cuadrilla. ¡Con vos ya está formada!"
                : "Te uniste a la cuadrilla.";
        });

    public Task<CrewActionResult> KickAsync(KickFromCrewRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await RequireManagerAsync(c, tx, r.CharacterId, r.TargetCharacterId, ct);
            await using var delete = new NpgsqlCommand(
                "DELETE FROM distrito.crew_members WHERE crew_id = @crew AND character_id = @t", c, tx);
            delete.Parameters.AddWithValue("crew", crew.Id);
            delete.Parameters.AddWithValue("t", r.TargetCharacterId);
            await delete.ExecuteNonQueryAsync(ct);
            await LogAsync(c, tx, crew.Id, "kicked", r.TargetCharacterId, "", ct);
            return "Integrante expulsado.";
        });

    public Task<CrewActionResult> SetRoleAsync(SetCrewRoleRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            if (r.Role is not (CrewRoles.Second or CrewRoles.Member))
                throw new CrewRejected("invalid_role", "Rol inválido.");
            var crew = await RequireManagerAsync(c, tx, r.CharacterId, r.TargetCharacterId, ct);

            await using var cmd = new NpgsqlCommand(r.Role == CrewRoles.Second
                ? """
                  UPDATE distrito.crew_members SET role = 'member' WHERE crew_id = @crew AND role = 'second';
                  UPDATE distrito.crew_members SET role = 'second' WHERE crew_id = @crew AND character_id = @t;
                  """
                : "UPDATE distrito.crew_members SET role = 'member' WHERE crew_id = @crew AND character_id = @t;", c, tx);
            cmd.Parameters.AddWithValue("crew", crew.Id);
            cmd.Parameters.AddWithValue("t", r.TargetCharacterId);
            await cmd.ExecuteNonQueryAsync(ct);
            await LogAsync(c, tx, crew.Id, r.Role == CrewRoles.Second ? "role_second" : "role_member", r.TargetCharacterId, "", ct);
            return r.Role == CrewRoles.Second ? "Nombraste un segundo." : "Ya no es segundo.";
        });

    public Task<CrewActionResult> ExpandAsync(ExpandCrewRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);
            await ApplyInactivitySuccessionAsync(c, tx, crew.Id, ct);
            crew = await CrewOfAsync(c, tx, r.CharacterId, ct) ?? crew;
            if (crew.Role != CrewRoles.Leader)
                throw new CrewRejected("not_leader", "Solo el jefe puede ampliar la cuadrilla.");

            var status = await LoadStatusAsync(c, tx, r.CharacterId, ct);
            var next = status.NextExpansion
                ?? throw new CrewRejected("max_level", "La cuadrilla ya tiene el cupo máximo.");
            if (!status.CanExpand)
                throw new CrewRejected("requirements_not_met",
                    $"Para ampliar a {next.Capacity} hacen falta {next.Contracts} contratos y {next.Reputation} de reputación entre todos " +
                    $"(tienen {status.Contracts} y {status.Reputation}).");

            await using var cmd = new NpgsqlCommand("UPDATE distrito.crews SET level = @l WHERE id = @id", c, tx);
            cmd.Parameters.AddWithValue("l", next.ToLevel);
            cmd.Parameters.AddWithValue("id", crew.Id);
            await cmd.ExecuteNonQueryAsync(ct);
            await LogAsync(c, tx, crew.Id, "expanded", r.CharacterId, next.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
            return $"¡La cuadrilla creció! Ahora entran {next.Capacity}.";
        });

    /// <summary>Verifica que quien pide sea jefe y que el objetivo sea otro integrante de la misma cuadrilla.</summary>
    private async Task<Membership> RequireManagerAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid manager, Guid target, CancellationToken ct)
    {
        var crew = await CrewOfAsync(c, tx, manager, ct)
            ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
        await LockCrewAsync(c, tx, crew.Id, ct);
        await ApplyInactivitySuccessionAsync(c, tx, crew.Id, ct);
        crew = await CrewOfAsync(c, tx, manager, ct) ?? crew;
        if (!CrewRoles.CanManage(crew.Role))
            throw new CrewRejected("not_leader", "Solo el jefe puede hacer eso.");
        if (manager == target)
            throw new CrewRejected("invalid_target", "No podés hacerte eso a vos mismo.");
        var targetCrew = await CrewOfAsync(c, tx, target, ct);
        if (targetCrew?.Id != crew.Id)
            throw new CrewRejected("not_member", "Esa persona no está en tu cuadrilla.");
        return crew;
    }

    public Task<CrewActionResult> LeaveAsync(LeaveCrewRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);

            await using (var delete = new NpgsqlCommand(
                "DELETE FROM distrito.crew_members WHERE crew_id = @crew AND character_id = @c", c, tx))
            {
                delete.Parameters.AddWithValue("crew", crew.Id);
                delete.Parameters.AddWithValue("c", r.CharacterId);
                await delete.ExecuteNonQueryAsync(ct);
            }

            await LogAsync(c, tx, crew.Id, "left", r.CharacterId, "", ct);

            if (await MemberCountAsync(c, tx, crew.Id, ct) == 0)
            {
                await Distrito.Backend.Economy.EconomyService.EmptyWarehouseOnDisbandAsync(c, tx, crew.Id, r.CharacterId, ct);
                await Distrito.Backend.Economy.EconomyService.HandOverCrewVehiclesAsync(c, tx, crew.Id, r.CharacterId, ct);
                await using var disband = new NpgsqlCommand("DELETE FROM distrito.crews WHERE id = @crew", c, tx);
                disband.Parameters.AddWithValue("crew", crew.Id);
                await disband.ExecuteNonQueryAsync(ct);
                return "Saliste y la cuadrilla se disolvió.";
            }

            if (crew.Role == "leader")
            {
                // Provisional (ADR-004): el liderazgo pasa al miembro más antiguo. Las reglas reales de sucesión están pendientes.
                await using var promote = new NpgsqlCommand("""
                    WITH heir AS (
                        SELECT character_id FROM distrito.crew_members WHERE crew_id = @crew ORDER BY joined_at, character_id LIMIT 1
                    )
                    UPDATE distrito.crew_members SET role = 'leader' WHERE crew_id = @crew AND character_id = (SELECT character_id FROM heir);
                    UPDATE distrito.crews SET leader_character_id = (
                        SELECT character_id FROM distrito.crew_members WHERE crew_id = @crew AND role = 'leader') WHERE id = @crew;
                    """, c, tx);
                promote.Parameters.AddWithValue("crew", crew.Id);
                await promote.ExecuteNonQueryAsync(ct);
                await LogAsync(c, tx, crew.Id, "leadership_leader_left", null, "pasa al miembro más antiguo", ct);
            }
            return "Saliste de la cuadrilla.";
        });

    // ---------- Infraestructura ----------

    private async Task<CrewActionResult> RunAsync(Guid characterId, CancellationToken ct,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<string>> action)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        try
        {
            var message = await action(c, tx);
            var status = await LoadStatusAsync(c, tx, characterId, ct);
            await tx.CommitAsync(ct);
            return new CrewActionResult(true, null, message, status);
        }
        catch (CrewRejected rejected)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return new CrewActionResult(false, rejected.Code, rejected.Message, await GetStatusAsync(characterId, ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return new CrewActionResult(false, "conflict", "Otra acción simultánea cambió la cuadrilla. Probá de nuevo.",
                await GetStatusAsync(characterId, ct));
        }
    }

    private sealed record Membership(Guid Id, string Role);

    private static async Task<Membership?> CrewOfAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT crew_id, role FROM distrito.crew_members WHERE character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new Membership(reader.GetGuid(0), reader.GetString(1)) : null;
    }

    private static async Task EnsureCharacterExistsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM distrito.characters WHERE id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        if (await cmd.ExecuteScalarAsync(ct) is null)
            throw new CrewRejected("character_not_found", "El personaje no existe.");
    }

    private static async Task LockCrewAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM distrito.crews WHERE id = @id FOR UPDATE", c, tx);
        cmd.Parameters.AddWithValue("id", crewId);
        if (await cmd.ExecuteScalarAsync(ct) is null)
            throw new CrewRejected("crew_not_found", "La cuadrilla ya no existe.");
    }

    private static async Task<int> CapacityAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT level FROM distrito.crews WHERE id = @id", c, tx);
        cmd.Parameters.AddWithValue("id", crewId);
        return CrewRules.CapacityForLevel((int)(await cmd.ExecuteScalarAsync(ct) ?? 1));
    }

    private static async Task LogAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, string kind, Guid? characterId, string detail, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO distrito.crew_events (crew_id, kind, character_id, detail) VALUES (@crew, @kind, @c, @detail)", c, tx);
        cmd.Parameters.AddWithValue("crew", crewId);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("c", (object?)characterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("detail", detail);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Si el jefe no entra al juego hace más de <see cref="CrewRules.LeaderInactivityDays"/> días,
    /// el mando pasa al miembro activo más antiguo. Si nadie está activo, no cambia nada.
    /// </summary>
    private async Task ApplyInactivitySuccessionAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - TimeSpan.FromDays(CrewRules.LeaderInactivityDays);
        await using var find = new NpgsqlCommand("""
            SELECT
              (SELECT m.character_id FROM distrito.crew_members m JOIN distrito.characters ch ON ch.id = m.character_id
                WHERE m.crew_id = @crew AND m.role = 'leader' AND ch.last_seen_at < @cutoff),
              (SELECT m.character_id FROM distrito.crew_members m JOIN distrito.characters ch ON ch.id = m.character_id
                WHERE m.crew_id = @crew AND m.role IN ('member', 'second') AND ch.last_seen_at >= @cutoff
                ORDER BY m.joined_at, m.character_id LIMIT 1)
            """, c, tx);
        find.Parameters.AddWithValue("crew", crewId);
        find.Parameters.AddWithValue("cutoff", cutoff);
        Guid? oldLeader = null, heir = null;
        await using (var reader = await find.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                oldLeader = reader.IsDBNull(0) ? null : reader.GetGuid(0);
                heir = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            }
        }
        if (oldLeader is null || heir is null)
            return;

        await using var swap = new NpgsqlCommand("""
            UPDATE distrito.crew_members SET role = 'member' WHERE crew_id = @crew AND character_id IN (@old, @heir);
            UPDATE distrito.crew_members SET role = 'leader' WHERE crew_id = @crew AND character_id = @heir;
            UPDATE distrito.crews SET leader_character_id = @heir WHERE id = @crew;
            """, c, tx);
        swap.Parameters.AddWithValue("crew", crewId);
        swap.Parameters.AddWithValue("old", oldLeader.Value);
        swap.Parameters.AddWithValue("heir", heir.Value);
        await swap.ExecuteNonQueryAsync(ct);
        await LogAsync(c, tx, crewId, "leadership_inactivity", heir, $"el jefe anterior ({oldLeader}) superó {CrewRules.LeaderInactivityDays} días sin entrar", ct);
    }

    private static async Task<long> MemberCountAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM distrito.crew_members WHERE crew_id = @id", c, tx);
        cmd.Parameters.AddWithValue("id", crewId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task AddMemberAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid characterId, string role, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO distrito.crew_members (crew_id, character_id, role) VALUES (@crew, @c, @role)", c, tx);
        cmd.Parameters.AddWithValue("crew", crewId);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("role", role);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteInvitationAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM distrito.crew_invitations WHERE crew_id = @crew AND character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("crew", crewId);
        cmd.Parameters.AddWithValue("c", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteInvitationsForAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("DELETE FROM distrito.crew_invitations WHERE character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<CrewStatus> LoadStatusAsync(NpgsqlConnection c, NpgsqlTransaction? tx, Guid characterId, CancellationToken ct)
    {
        Guid? crewId = null;
        string? crewName = null;
        var isLeader = false;
        var level = 1;
        long treasury = 0;
        CrewPermissions? permissions = null;
        var members = new List<CrewMemberView>();
        var invitations = new List<CrewInvitationView>();

        await using var cmd = new NpgsqlCommand("""
            SELECT cr.id, cr.name, m.role, cr.level, COALESCE(a.balance_cents, 0),
                   cr.second_can_withdraw, cr.second_can_invite, cr.member_can_invite, cr.contract_tithe_pct
              FROM distrito.crew_members m JOIN distrito.crews cr ON cr.id = m.crew_id
              LEFT JOIN distrito.economic_accounts a ON a.id = cr.account_id
             WHERE m.character_id = @c;
            SELECT ch.id, ch.name, m.role FROM distrito.crew_members m JOIN distrito.characters ch ON ch.id = m.character_id
             WHERE m.crew_id = (SELECT crew_id FROM distrito.crew_members WHERE character_id = @c) ORDER BY m.joined_at, ch.id;
            SELECT cr.id, cr.name, inviter.name FROM distrito.crew_invitations i
              JOIN distrito.crews cr ON cr.id = i.crew_id JOIN distrito.characters inviter ON inviter.id = i.invited_by
             WHERE i.character_id = @c AND i.expires_at > @now ORDER BY i.created_at;
            SELECT e.created_at, e.kind, ch.name, e.detail FROM distrito.crew_events e
              LEFT JOIN distrito.characters ch ON ch.id = e.character_id
             WHERE e.crew_id = (SELECT crew_id FROM distrito.crew_members WHERE character_id = @c)
             ORDER BY e.id DESC LIMIT 8;
            SELECT
              (SELECT count(*) FROM distrito.contracts k
                 WHERE k.status = 'completed'
                   AND k.character_id IN (SELECT character_id FROM distrito.crew_members
                                          WHERE crew_id = (SELECT crew_id FROM distrito.crew_members WHERE character_id = @c)))::int,
              (SELECT COALESCE(sum(r.value), 0) FROM distrito.character_reputation r
                 WHERE r.character_id IN (SELECT character_id FROM distrito.crew_members
                                          WHERE crew_id = (SELECT crew_id FROM distrito.crew_members WHERE character_id = @c)))::int;
            """, c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            crewId = reader.GetGuid(0);
            crewName = reader.GetString(1);
            isLeader = reader.GetString(2) == "leader";
            level = reader.GetInt32(3);
            treasury = reader.GetInt64(4);
            permissions = new CrewPermissions(reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7), reader.GetInt32(8));
        }
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            members.Add(new CrewMemberView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            invitations.Add(new CrewInvitationView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        var history = new List<CrewEventView>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            history.Add(new CrewEventView(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        int contracts = 0, reputation = 0;
        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct))
            (contracts, reputation) = (reader.GetInt32(0), reader.GetInt32(1));

        reader.Close();
        CoalitionView? coalition = null;
        List<CoalitionInviteView>? coalitionInvites = null;
        CrewVoteView? vote = null;
        List<WarView>? wars = null;
        if (crewId is { } myCrew)
        {
            (coalition, coalitionInvites) = await LoadCoalitionAsync(c, tx, myCrew, ct);
            vote = await LoadVoteAsync(c, tx, myCrew, characterId, ct);
            wars = await LoadWarsAsync(c, tx, myCrew, ct);
        }
        return new CrewStatus(crewId, crewName, isLeader, members, invitations, level, history, contracts, reputation, treasury, permissions,
            coalition, coalitionInvites, vote, wars)
        {
            MyRole = members.FirstOrDefault(m => m.CharacterId == characterId)?.Role,
        };
    }
}
