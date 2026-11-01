using Distrito.Backend.Crews;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Sede de cuadrilla, prestigio y misiones generadas (aprobado por el equipo 2026-11-01). El cupo de integrantes no cambia.
/// Las misiones se arman solas cada 3 horas (MissionGenerator) y se guardan la primera vez que alguien mira el tablero.
/// La paga va a la caja de la cuadrilla y sale de la cuenta real de la familia que encarga (hasta donde le alcance).
/// </summary>
public sealed partial class EconomyService
{
    private static async Task<(int Prestige, int Rank)> PrestigeAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT prestige FROM distrito.crews WHERE id = @id", c, tx);
        cmd.Parameters.AddWithValue("id", crewId);
        var prestige = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return (prestige, PrestigeRules.RankFor(prestige));
    }

    /// <summary>Rango de prestigio de la cuadrilla del personaje (1 si no tiene).</summary>
    private static async Task<int> RankOfCharacterAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var seat = await CrewSeatAsync(c, tx, characterId, ct);
        return seat is null ? 1 : (await PrestigeAsync(c, tx, seat.CrewId, ct)).Rank;
    }

    private static async Task<int?> ShedOfAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT shed FROM distrito.crew_bases WHERE crew_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", crewId);
        return await cmd.ExecuteScalarAsync(ct) as int?;
    }

    public async Task<List<CrewBaseView>> CrewBasesAsync(CancellationToken ct)
    {
        var list = new List<CrewBaseView>();
        await using var c = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT b.crew_id, cr.name, b.shed, cr.prestige FROM distrito.crew_bases b JOIN distrito.crews cr ON cr.id = b.crew_id ORDER BY b.shed
            """, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CrewBaseView(r.GetGuid(0), r.GetString(1), r.GetInt32(2), PrestigeRules.RankFor(r.GetInt32(3))));
        return list;
    }

    /// <summary>Tablero de la cuadrilla: sede, prestigio y las misiones del turno (las genera si todavía no existen).</summary>
    public async Task<CrewBoardView> CrewBoardAsync(Guid characterId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var slot = MissionGenerator.SlotOf(now);
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var taken = new List<int>();
        await using (var cmd = new NpgsqlCommand("SELECT shed FROM distrito.crew_bases ORDER BY shed", c, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) taken.Add(r.GetInt32(0));
        var seat = await CrewSeatAsync(c, tx, characterId, ct);
        if (seat is null)
        {
            await tx.CommitAsync(ct);
            return new CrewBoardView(false, null, 0, 1, PrestigeRules.ForRank(2), null, 0, [], taken, false, MissionGenerator.SlotEnd(slot));
        }
        var (prestige, rank) = await PrestigeAsync(c, tx, seat.CrewId, ct);
        var shed = await ShedOfAsync(c, tx, seat.CrewId, ct);
        int salt;
        await using (var s = new NpgsqlCommand("SELECT mission_salt FROM distrito.crews WHERE id = @c", c, tx))
        {
            s.Parameters.AddWithValue("c", seat.CrewId);
            salt = (int)(await s.ExecuteScalarAsync(ct) ?? 0);
        }
        var drafts = MissionGenerator.Generate(seat.CrewId, slot, rank, salt);
        for (var i = 0; i < drafts.Count; i++)
        {
            var d = drafts[i];
            await using var insert = new NpgsqlCommand("""
                INSERT INTO distrito.crew_missions (id, crew_id, slot, idx, kind, title, detail, item_code, npc_code, target, reward_cents, prestige, payer_family, expires_at)
                VALUES (@id, @crew, @slot, @idx, @kind, @title, @detail, @item, @npc, @target, @reward, @prestige, @payer, @exp)
                ON CONFLICT (crew_id, slot, idx) DO NOTHING
                """, c, tx);
            insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("crew", seat.CrewId);
            insert.Parameters.AddWithValue("slot", slot);
            insert.Parameters.AddWithValue("idx", i);
            insert.Parameters.AddWithValue("kind", d.Kind);
            insert.Parameters.AddWithValue("title", d.Title);
            insert.Parameters.AddWithValue("detail", d.Detail);
            insert.Parameters.AddWithValue("item", (object?)d.ItemCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("npc", (object?)d.NpcCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("target", d.Target);
            insert.Parameters.AddWithValue("reward", d.RewardCents);
            insert.Parameters.AddWithValue("prestige", d.Prestige);
            insert.Parameters.AddWithValue("payer", d.PayerFamily);
            insert.Parameters.AddWithValue("exp", MissionGenerator.SlotEnd(slot));
            await insert.ExecuteNonQueryAsync(ct);
        }
        var missions = await MissionsAsync(c, tx, seat.CrewId, slot, ct);
        long debt = 0;
        await using (var w = new NpgsqlCommand("SELECT debt_cents FROM distrito.warehouses WHERE crew_id = @c", c, tx))
        {
            w.Parameters.AddWithValue("c", seat.CrewId);
            debt = await w.ExecuteScalarAsync(ct) is long d0 ? d0 : 0;
        }
        var operation = await OperationViewAsync(c, tx, seat.CrewId, rank, ct);
        await tx.CommitAsync(ct);
        return new CrewBoardView(true, seat.CrewName, prestige, rank, PrestigeRules.ForRank(Math.Min(rank + 1, PrestigeRules.MaxRank)),
            shed, debt, missions, taken, seat.Role == "leader", MissionGenerator.SlotEnd(slot), operation);
    }

    private static async Task<List<CrewMissionView>> MissionsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, long slot, CancellationToken ct)
    {
        var list = new List<CrewMissionView>();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, kind, title, detail, item_code, npc_code, target, progress, reward_cents, prestige, status, expires_at, payer_family
              FROM distrito.crew_missions WHERE crew_id = @c AND slot = @slot ORDER BY idx
            """, c, tx);
        cmd.Parameters.AddWithValue("c", crewId);
        cmd.Parameters.AddWithValue("slot", slot);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CrewMissionView(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetInt32(6), r.GetInt32(7), r.GetInt64(8), r.GetInt32(9), r.GetString(10),
                r.GetFieldValue<DateTimeOffset>(11), r.GetString(12)));
        return list;
    }

    /// <summary>El jefe compra la sede: un galpón libre de Los Muelles. Trae el depósito de 500 kg (se crea o se agranda).</summary>
    public Task<EconomyActionResult> BuyCrewBaseAsync(CrewBaseBuyRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (r.Shed < 0 || r.Shed >= CrewBaseRules.Sheds.Count)
                throw new EconomyRejected("invalid_shed", "Ese galpón no se vende.");
            var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("not_in_crew", "No estás en una cuadrilla.");
            if (seat.Role != "leader")
                throw new EconomyRejected("not_leader", "Solo el jefe compra la sede.");
            if (await ShedOfAsync(c, tx, seat.CrewId, ct) is not null)
                throw new EconomyRejected("has_base", "La cuadrilla ya tiene sede.");
            if (await ExistsAsync(c, tx, "SELECT 1 FROM distrito.crew_bases WHERE shed = @k::int", r.Shed.ToString(), ct))
                throw new EconomyRejected("shed_taken", "Ese galpón ya es de otra cuadrilla.");
            var box = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
            var landlord = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
            await LockAccountsAsync(c, tx, ct, box, landlord);
            if (await BalanceAsync(c, tx, box, ct) < CrewBaseRules.PriceCents)
                throw new EconomyRejected("insufficient_funds", $"La caja no llega: la sede cuesta {EconomyCatalog.FormatMoney(CrewBaseRules.PriceCents)}.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "crew_base", $"Compra de la sede de {seat.CrewName} (galpón {r.Shed + 1})", ct);
            await TransferMoneyAsync(c, tx, txId, from: box, to: landlord, CrewBaseRules.PriceCents, ct);
            await using (var insert = new NpgsqlCommand("INSERT INTO distrito.crew_bases (id, crew_id, shed) VALUES (@id, @c, @s)", c, tx))
            {
                insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
                insert.Parameters.AddWithValue("c", seat.CrewId);
                insert.Parameters.AddWithValue("s", r.Shed);
                await insert.ExecuteNonQueryAsync(ct);
            }
            var (_, rank) = await PrestigeAsync(c, tx, seat.CrewId, ct);
            // El depósito pasa a ser el de la sede: 500 kg (+ extra por rango); si no había, se crea sin costo de apertura.
            await using (var depot = new NpgsqlCommand("""
                INSERT INTO distrito.warehouses (id, crew_id, place, capacity_kg, capacity_l, last_rent_cycle) VALUES (@id, @c, 'sede', @kg, NULL, @cycle)
                ON CONFLICT (crew_id) DO UPDATE SET place = 'sede', capacity_kg = GREATEST(distrito.warehouses.capacity_kg, @kg)
                """, c, tx))
            {
                depot.Parameters.AddWithValue("id", Guid.CreateVersion7());
                depot.Parameters.AddWithValue("c", seat.CrewId);
                depot.Parameters.AddWithValue("kg", CrewBaseRules.DepotKg + PrestigeRules.ExtraDepotKg(rank));
                depot.Parameters.AddWithValue("cycle", CurrentCycle());
                await depot.ExecuteNonQueryAsync(ct);
            }
            return $"¡Sede comprada! El galpón {r.Shed + 1} de Los Muelles es de «{seat.CrewName}». Adentro es zona segura, el depósito "
                   + $"pasa a {CrewBaseRules.DepotKg:0} kg y reaparecen ahí. Alquiler: {EconomyCatalog.FormatMoney(CrewBaseRules.RentCents)} por ciclo.";
        });

    /// <summary>Suma prestigio y paga una misión cumplida (dentro de la transacción que la cumple).</summary>
    private async Task<string> CompleteMissionAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid txId, Guid crewId, Guid missionId,
        string title, long reward, int prestige, string payerFamily, Guid by, CancellationToken ct)
    {
        await using (var done = new NpgsqlCommand("""
            UPDATE distrito.crew_missions SET status = 'cumplida', completed_by = @by, completed_at = @now WHERE id = @id;
            UPDATE distrito.crews SET prestige = prestige + @p WHERE id = @crew;
            INSERT INTO distrito.crew_events (crew_id, kind, character_id, detail) VALUES (@crew, 'mission', @by, @title);
            """, c, tx))
        {
            done.Parameters.AddWithValue("by", by);
            done.Parameters.AddWithValue("now", clock.GetUtcNow());
            done.Parameters.AddWithValue("id", missionId);
            done.Parameters.AddWithValue("p", prestige);
            done.Parameters.AddWithValue("crew", crewId);
            done.Parameters.AddWithValue("title", title);
            await done.ExecuteNonQueryAsync(ct);
        }
        var box = await CrewService.EnsureCrewAccountAsync(c, tx, crewId, ct);
        var payerNpc = payerFamily switch
        {
            EconomyCatalog.Moretti => EconomyCatalog.DepositNpc,
            EconomyCatalog.Caruso => EconomyCatalog.WorkshopNpc,
            _ => EconomyCatalog.ValentiOfficeNpc,
        };
        var payer = await AccountIdByCodeAsync(c, tx, payerNpc, ct);
        await LockAccountsAsync(c, tx, ct, box, payer);
        var paid = Math.Min(reward, await BalanceAsync(c, tx, payer, ct));
        if (paid > 0)
            await TransferMoneyAsync(c, tx, txId, from: payer, to: box, paid, ct);
        var (_, rankAfter) = await PrestigeAsync(c, tx, crewId, ct);
        // Beneficios de depósito por rango: se aplican apenas se sube.
        await using (var grow = new NpgsqlCommand("""
            UPDATE distrito.warehouses SET capacity_kg = GREATEST(capacity_kg, @kg) WHERE crew_id = @c AND place = 'sede'
            """, c, tx))
        {
            grow.Parameters.AddWithValue("kg", CrewBaseRules.DepotKg + PrestigeRules.ExtraDepotKg(rankAfter));
            grow.Parameters.AddWithValue("c", crewId);
            await grow.ExecuteNonQueryAsync(ct);
        }
        if (await AdvanceOperationAsync(c, tx, crewId, missionId, ct) is { } operation)
            return reward > 0
                ? $"{operation} {EconomyCatalog.FormatMoney(paid)} a la caja y +{prestige} de prestigio (rango {rankAfter})."
                : operation;
        return $"¡Misión cumplida! {title}: {EconomyCatalog.FormatMoney(paid)} a la caja y +{prestige} de prestigio (rango {rankAfter}).";
    }

    /// <summary>Misiones que se cumplen en un lugar (entrega, acopio, cobranza). El servidor de juego validó la presencia.</summary>
    public Task<EconomyActionResult> TurnInCrewMissionAsync(CrewMissionTurnInRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("not_in_crew", "No estás en una cuadrilla.");
            string kind, title, payer;
            string? item, npc;
            int target;
            long reward;
            int prestige;
            await using (var find = new NpgsqlCommand("""
                SELECT kind, title, item_code, npc_code, target, reward_cents, prestige, payer_family, status, expires_at
                  FROM distrito.crew_missions WHERE id = @id AND crew_id = @c FOR UPDATE
                """, c, tx))
            {
                find.Parameters.AddWithValue("id", r.MissionId);
                find.Parameters.AddWithValue("c", seat.CrewId);
                await using var reader = await find.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new EconomyRejected("not_found", "Esa misión no es de tu cuadrilla.");
                if (reader.GetString(8) != "abierta")
                    throw new EconomyRejected("done", "Esa misión ya está cumplida.");
                if (reader.GetFieldValue<DateTimeOffset>(9) <= clock.GetUtcNow())
                    throw new EconomyRejected("expired", "Esa misión venció: ya hay tablero nuevo.");
                (kind, title, item, npc, target, reward, prestige, payer) = (reader.GetString(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetString(7));
            }
            if (!MissionKinds.IsTurnIn(kind))
                throw new EconomyRejected("not_turn_in", "Esa misión se cumple haciendo lo que pide, no con un botón.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "crew_mission", $"Misión de {seat.CrewName}: {title}", ct);
            switch (kind)
            {
                case MissionKinds.Delivery:
                {
                    var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
                    var receiver = await AccountIdByCodeAsync(c, tx, npc!, ct);
                    await LockAccountsAsync(c, tx, ct, me, receiver);
                    await MoveItemsAsync(c, tx, txId, item!, target, from: (me, EconomyCatalog.CarriedLocation), to: (receiver, StockLocation(npc!)),
                        notEnough: $"Te faltan: hacen falta {target} encima.", ct, actor: r.CharacterId);
                    break;
                }
                case MissionKinds.Gather:
                {
                    var row = await WarehouseOfAsync(c, tx, seat.CrewId, true, ct) ?? throw new EconomyRejected("no_warehouse", "La cuadrilla no tiene depósito.");
                    var box = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
                    var valenti = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
                    await LockAccountsAsync(c, tx, ct, box, valenti);
                    await MoveItemsAsync(c, tx, txId, item!, target, from: (box, row.Location), to: (valenti, "acopio"),
                        notEnough: $"En el depósito no hay {target}.", ct, actor: r.CharacterId);
                    break;
                }
                case MissionKinds.Collection:
                {
                    var mates = await CrewMatesAmongAsync(c, tx, r.CharacterId, r.Companions ?? [], ct);
                    if (mates.Count < target)
                        throw new EconomyRejected("need_crew_mate", $"Para cobrar tienen que estar cerca {target} de la cuadrilla.");
                    break;
                }
            }
            return await CompleteMissionAsync(c, tx, txId, seat.CrewId, r.MissionId, title, reward, prestige, payer, r.CharacterId, ct);
        });

    /// <summary>
    /// Progreso por algo que pasó en el mundo (asaltos, guardias, vigilancia). Suma en la primera misión abierta de ese tipo,
    /// empezando por la etapa de la operación del día si la hay.
    /// </summary>
    public Task<EconomyActionResult> CrewMissionProgressAsync(CrewMissionProgressRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct);
            if (seat is null || MissionKinds.IsTurnIn(r.Kind) || r.Amount <= 0)
                return "";
            Guid id;
            string title, payer;
            int target, progress, prestige;
            long reward;
            await using (var find = new NpgsqlCommand("""
                SELECT id, title, target, progress, reward_cents, prestige, payer_family FROM distrito.crew_missions
                 WHERE crew_id = @c AND kind = @k AND status = 'abierta' AND expires_at > @now
                   AND (npc_code IS NULL OR npc_code = @npc) ORDER BY operation_day IS NULL, slot DESC, idx LIMIT 1 FOR UPDATE
                """, c, tx))
            {
                find.Parameters.AddWithValue("c", seat.CrewId);
                find.Parameters.AddWithValue("k", r.Kind);
                find.Parameters.AddWithValue("now", clock.GetUtcNow());
                find.Parameters.AddWithValue("npc", (object?)r.NpcCode ?? DBNull.Value);
                await using var reader = await find.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return "";
                (id, title, target, progress, reward, prestige, payer) = (reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetInt64(4), reader.GetInt32(5), reader.GetString(6));
            }
            var now = Math.Min(target, progress + r.Amount);
            await using (var update = new NpgsqlCommand("UPDATE distrito.crew_missions SET progress = @p WHERE id = @id", c, tx))
            {
                update.Parameters.AddWithValue("p", now);
                update.Parameters.AddWithValue("id", id);
                await update.ExecuteNonQueryAsync(ct);
            }
            if (now < target)
                return $"Misión «{title}»: {now}/{target}.";
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "crew_mission", $"Misión de {seat.CrewName}: {title}", ct);
            return await CompleteMissionAsync(c, tx, txId, seat.CrewId, id, title, reward, prestige, payer, r.CharacterId, ct);
        });
}
