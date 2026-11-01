using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Operaciones de cuadrilla (propuesta aprobada por el equipo, 2026-11-01). La del día se calcula (CrewOperations.Pick);
/// al empezarla se guarda y se crea la primera etapa como misión del tablero; al cumplir cada etapa se crea la siguiente.
/// Solo la última paga: $120–250 a la caja (de la cuenta de la familia que encarga) y 60–120 de prestigio.
/// </summary>
public sealed partial class EconomyService
{
    private static int OperationLuck(Guid crewId, int day) =>
        (int)((uint)EncargoRules.StableId(crewId, "operacion-paga", day).GetHashCode() % 21u);

    private async Task<CrewOperationView> OperationViewAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, int rank, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var day = CrewOperations.DayOf(now);
        var plan = CrewOperations.Pick(crewId, day);
        var (reward, prestige) = CrewOperations.Reward(rank, OperationLuck(crewId, day));
        string? status = null;
        await using (var cmd = new NpgsqlCommand("SELECT status, reward_cents, prestige FROM distrito.crew_operations WHERE crew_id = @c AND day = @d", c, tx))
        {
            cmd.Parameters.AddWithValue("c", crewId);
            cmd.Parameters.AddWithValue("d", day);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
                (status, reward, prestige) = (r.GetString(0), r.GetInt64(1), r.GetInt32(2));
        }
        var rows = new List<CrewMissionView>();
        var stages = new Dictionary<Guid, int>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT id, kind, title, detail, item_code, npc_code, target, progress, reward_cents, prestige, status, expires_at, payer_family, operation_stage
              FROM distrito.crew_missions WHERE crew_id = @c AND operation_day = @d ORDER BY operation_stage
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("c", crewId);
            cmd.Parameters.AddWithValue("d", day);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var m = new CrewMissionView(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5), r.GetInt32(6), r.GetInt32(7), r.GetInt64(8), r.GetInt32(9), r.GetString(10),
                    r.GetFieldValue<DateTimeOffset>(11), r.GetString(12));
                rows.Add(m);
                stages[m.Id] = r.GetInt32(13);
            }
        }
        var current = rows.FirstOrDefault(m => !m.Done);
        var currentStage = current is null ? -1 : stages[current.Id];
        var views = plan.Stages.Select((s, i) => new CrewOperationStageView(s.Title, s.Detail,
            rows.Any(m => m.Done && stages[m.Id] == i), i == currentStage)).ToList();
        var shown = status switch
        {
            null => CrewOperations.Available,
            "cumplida" => CrewOperations.Done,
            _ => CrewOperations.Running,
        };
        return new CrewOperationView(plan.Code, plan.Title, plan.Intro, plan.Family, reward, prestige, shown, views, current,
            CrewOperations.DayEnd(now), CrewOperations.MinMembers);
    }

    /// <summary>Crea la misión de una etapa (la última lleva la paga y el prestigio de la operación).</summary>
    private static async Task InsertOperationStageAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, int day, CrewOperations.Template plan,
        int stage, long reward, int prestige, DateTimeOffset expires, CancellationToken ct)
    {
        var s = plan.Stages[stage];
        var last = stage == plan.Stages.Count - 1;
        await using var insert = new NpgsqlCommand("""
            INSERT INTO distrito.crew_missions (id, crew_id, slot, idx, kind, title, detail, item_code, npc_code, target, reward_cents, prestige,
                payer_family, expires_at, operation_day, operation_stage)
            VALUES (@id, @crew, @slot, @idx, @kind, @title, @detail, @item, @npc, @target, @reward, @prestige, @payer, @exp, @day, @stage)
            """, c, tx);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("crew", crewId);
        insert.Parameters.AddWithValue("slot", -(long)day);
        insert.Parameters.AddWithValue("idx", 100 + stage);
        insert.Parameters.AddWithValue("kind", s.Kind);
        insert.Parameters.AddWithValue("title", $"{plan.Title} ({stage + 1}/{plan.Stages.Count}): {s.Title}");
        insert.Parameters.AddWithValue("detail", s.Detail);
        insert.Parameters.AddWithValue("item", (object?)s.Item ?? DBNull.Value);
        insert.Parameters.AddWithValue("npc", (object?)s.Npc ?? DBNull.Value);
        insert.Parameters.AddWithValue("target", s.Target);
        insert.Parameters.AddWithValue("reward", last ? reward : 0L);
        insert.Parameters.AddWithValue("prestige", last ? prestige : 0);
        insert.Parameters.AddWithValue("payer", plan.Family);
        insert.Parameters.AddWithValue("exp", expires);
        insert.Parameters.AddWithValue("day", day);
        insert.Parameters.AddWithValue("stage", stage);
        await insert.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Empezar la operación del día (cualquiera de la cuadrilla; hacen falta al menos 2 integrantes).</summary>
    public Task<EconomyActionResult> StartCrewOperationAsync(CrewOperationStartRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("not_in_crew", "No estás en una cuadrilla.");
            await using (var members = new NpgsqlCommand("SELECT count(*) FROM distrito.crew_members WHERE crew_id = @c", c, tx))
            {
                members.Parameters.AddWithValue("c", seat.CrewId);
                if ((long)(await members.ExecuteScalarAsync(ct) ?? 0L) < CrewOperations.MinMembers)
                    throw new EconomyRejected("too_few", $"Una operación pide al menos {CrewOperations.MinMembers} integrantes en la cuadrilla.");
            }
            var now = clock.GetUtcNow();
            var day = CrewOperations.DayOf(now);
            if (await ExistsAsync(c, tx, $"SELECT 1 FROM distrito.crew_operations WHERE crew_id = '{seat.CrewId}' AND day = @k::int", day.ToString(), ct))
                throw new EconomyRejected("already", "La operación de hoy ya está empezada (o hecha). Mañana hay otra.");
            var (_, rank) = await PrestigeAsync(c, tx, seat.CrewId, ct);
            var plan = CrewOperations.Pick(seat.CrewId, day);
            var (reward, prestige) = CrewOperations.Reward(rank, OperationLuck(seat.CrewId, day));
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO distrito.crew_operations (crew_id, day, code, status, reward_cents, prestige, started_by, started_at)
                VALUES (@c, @d, @code, 'en curso', @reward, @prestige, @by, @now)
                """, c, tx))
            {
                insert.Parameters.AddWithValue("c", seat.CrewId);
                insert.Parameters.AddWithValue("d", day);
                insert.Parameters.AddWithValue("code", plan.Code);
                insert.Parameters.AddWithValue("reward", reward);
                insert.Parameters.AddWithValue("prestige", prestige);
                insert.Parameters.AddWithValue("by", r.CharacterId);
                insert.Parameters.AddWithValue("now", now);
                await insert.ExecuteNonQueryAsync(ct);
            }
            await InsertOperationStageAsync(c, tx, seat.CrewId, day, plan, 0, reward, prestige, CrewOperations.DayEnd(now), ct);
            await NewLedgerTransactionAsync(c, tx, r.RequestId, "crew_operation", $"Operación de {seat.CrewName}: {plan.Title}", ct);
            return $"Operación en marcha: «{plan.Title}». {plan.Intro}\nPrimera etapa: {plan.Stages[0].Title}. {plan.Stages[0].Detail}";
        });

    /// <summary>
    /// Después de cumplir una misión: si era etapa de una operación, crea la siguiente (o la da por terminada).
    /// Devuelve el texto para el jugador, o null si la misión no era de una operación.
    /// </summary>
    private async Task<string?> AdvanceOperationAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid missionId, CancellationToken ct)
    {
        int? day = null, stage = null;
        await using (var cmd = new NpgsqlCommand("SELECT operation_day, operation_stage FROM distrito.crew_missions WHERE id = @id", c, tx))
        {
            cmd.Parameters.AddWithValue("id", missionId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct) && !r.IsDBNull(0))
                (day, stage) = (r.GetInt32(0), r.GetInt32(1));
        }
        if (day is not { } d || stage is not { } s)
            return null;
        string code;
        long reward;
        int prestige;
        await using (var cmd = new NpgsqlCommand("SELECT code, reward_cents, prestige FROM distrito.crew_operations WHERE crew_id = @c AND day = @d FOR UPDATE", c, tx))
        {
            cmd.Parameters.AddWithValue("c", crewId);
            cmd.Parameters.AddWithValue("d", d);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return null;
            (code, reward, prestige) = (r.GetString(0), r.GetInt64(1), r.GetInt32(2));
        }
        var plan = CrewOperations.Templates.First(t => t.Code == code);
        if (s + 1 < plan.Stages.Count)
        {
            await InsertOperationStageAsync(c, tx, crewId, d, plan, s + 1, reward, prestige, CrewOperations.DayEnd(clock.GetUtcNow()), ct);
            var next = plan.Stages[s + 1];
            return $"«{plan.Title}»: etapa {s + 1} de {plan.Stages.Count} cumplida. Sigue: {next.Title}. {next.Detail}";
        }
        await using (var done = new NpgsqlCommand("UPDATE distrito.crew_operations SET status = 'cumplida', finished_at = @now WHERE crew_id = @c AND day = @d", c, tx))
        {
            done.Parameters.AddWithValue("now", clock.GetUtcNow());
            done.Parameters.AddWithValue("c", crewId);
            done.Parameters.AddWithValue("d", d);
            await done.ExecuteNonQueryAsync(ct);
        }
        return $"¡Operación cumplida! «{plan.Title}» salió perfecta.";
    }
}
