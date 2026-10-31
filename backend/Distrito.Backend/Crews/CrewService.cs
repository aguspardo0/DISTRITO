using Distrito.Shared.Crews;
using Npgsql;

namespace Distrito.Backend.Crews;

/// <summary>
/// Cuadrillas: crear, invitar, responder invitaciones y salir.
/// Cada acción es una transacción; la fila de la cuadrilla se bloquea para que el cupo y el liderazgo
/// no se rompan con pedidos simultáneos.
/// </summary>
public sealed class CrewService(NpgsqlDataSource db, TimeProvider clock)
{
    private sealed class CrewRejected(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    public async Task<CrewStatus> GetStatusAsync(Guid characterId, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        return await LoadStatusAsync(c, null, characterId, ct);
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
            return $"Fundaste la cuadrilla {r.Name}.";
        });

    public Task<CrewActionResult> InviteAsync(InviteToCrewRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            if (r.CharacterId == r.TargetCharacterId)
                throw new CrewRejected("invalid_target", "No podés invitarte a vos mismo.");
            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);
            if (crew.Role != "leader")
                throw new CrewRejected("not_leader", "Solo el jefe de la cuadrilla puede invitar.");
            await EnsureCharacterExistsAsync(c, tx, r.TargetCharacterId, ct);
            if (await CrewOfAsync(c, tx, r.TargetCharacterId, ct) is not null)
                throw new CrewRejected("target_in_crew", "Esa persona ya está en una cuadrilla.");
            if (await MemberCountAsync(c, tx, crew.Id, ct) >= CrewRules.MaxMembers)
                throw new CrewRejected("crew_full", $"La cuadrilla está completa ({CrewRules.MaxMembers}).");

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
            if (await MemberCountAsync(c, tx, r.CrewId, ct) >= CrewRules.MaxMembers)
                throw new CrewRejected("crew_full", "La cuadrilla está completa.");

            await AddMemberAsync(c, tx, r.CrewId, r.CharacterId, "member", ct);
            await DeleteInvitationsForAsync(c, tx, r.CharacterId, ct);
            return "Te uniste a la cuadrilla.";
        });

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

            if (await MemberCountAsync(c, tx, crew.Id, ct) == 0)
            {
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
        var members = new List<CrewMemberView>();
        var invitations = new List<CrewInvitationView>();

        await using var cmd = new NpgsqlCommand("""
            SELECT cr.id, cr.name, m.role FROM distrito.crew_members m JOIN distrito.crews cr ON cr.id = m.crew_id WHERE m.character_id = @c;
            SELECT ch.id, ch.name, m.role FROM distrito.crew_members m JOIN distrito.characters ch ON ch.id = m.character_id
             WHERE m.crew_id = (SELECT crew_id FROM distrito.crew_members WHERE character_id = @c) ORDER BY m.joined_at, ch.id;
            SELECT cr.id, cr.name, inviter.name FROM distrito.crew_invitations i
              JOIN distrito.crews cr ON cr.id = i.crew_id JOIN distrito.characters inviter ON inviter.id = i.invited_by
             WHERE i.character_id = @c AND i.expires_at > @now ORDER BY i.created_at;
            """, c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            crewId = reader.GetGuid(0);
            crewName = reader.GetString(1);
            isLeader = reader.GetString(2) == "leader";
        }
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            members.Add(new CrewMemberView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            invitations.Add(new CrewInvitationView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));

        return new CrewStatus(crewId, crewName, isLeader, members, invitations);
    }
}
