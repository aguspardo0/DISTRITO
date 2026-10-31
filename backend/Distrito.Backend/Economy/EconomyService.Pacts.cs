using System.Globalization;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Acuerdos con las familias (Etapa E, aprobado 2026-10-31): el jefe firma en el NPC de la familia con 50 de reputación
/// sumada de la cuadrilla y $50 de la caja. Dura la temporada; uno por familia. Da +5 % en los contratos que paga la
/// familia y +2 de influencia por día en su zona.
/// </summary>
public sealed partial class EconomyService
{
    private static string NpcOfFamily(string family) => family switch
    {
        EconomyCatalog.Moretti => EconomyCatalog.DepositNpc,
        EconomyCatalog.Caruso => EconomyCatalog.WorkshopNpc,
        EconomyCatalog.Valenti => EconomyCatalog.ValentiOfficeNpc,
        _ => throw new EconomyRejected("unknown_family", "Esa familia no existe."),
    };

    public Task<EconomyActionResult> SignPactAsync(PactRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var npc = NpcOfFamily(r.FamilyCode);
            Guid crewId;
            await using (var who = new NpgsqlCommand("SELECT crew_id, role FROM distrito.crew_members WHERE character_id = @c", c, tx))
            {
                who.Parameters.AddWithValue("c", r.CharacterId);
                await using var reader = await who.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new EconomyRejected("not_in_crew", "Los acuerdos los firman las cuadrillas: necesitás una.");
                if (reader.GetString(1) != "leader")
                    throw new EconomyRejected("not_leader", "El acuerdo lo firma el jefe de la cuadrilla.");
                crewId = reader.GetGuid(0);
            }

            var season = TerritoryRules.SeasonAt(clock.GetUtcNow());
            await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", c, tx))
            {
                lockCmd.Parameters.AddWithValue("k", $"pact:{season}:{r.FamilyCode}");
                await lockCmd.ExecuteNonQueryAsync(ct);
            }
            await using (var taken = new NpgsqlCommand("SELECT 1 FROM distrito.family_pacts WHERE season = @s AND family_code = @f", c, tx))
            {
                taken.Parameters.AddWithValue("s", season);
                taken.Parameters.AddWithValue("f", r.FamilyCode);
                if (await taken.ExecuteScalarAsync(ct) is not null)
                    throw new EconomyRejected("pact_taken", "Esta familia ya firmó un acuerdo esta temporada.");
            }
            await using (var rep = new NpgsqlCommand("""
                SELECT COALESCE(sum(r.value), 0)::int FROM distrito.character_reputation r
                 JOIN distrito.crew_members m ON m.character_id = r.character_id
                WHERE m.crew_id = @crew AND r.family_code = @f
                """, c, tx))
            {
                rep.Parameters.AddWithValue("crew", crewId);
                rep.Parameters.AddWithValue("f", r.FamilyCode);
                var total = (int)(await rep.ExecuteScalarAsync(ct))!;
                if (total < PactRules.RequiredReputation)
                    throw new EconomyRejected("not_enough_reputation",
                        $"Tu cuadrilla junta {total} de reputación con los {EconomyCatalog.FamilyName(r.FamilyCode)}; hacen falta {PactRules.RequiredReputation}.");
            }

            var box = await Distrito.Backend.Crews.CrewService.EnsureCrewAccountAsync(c, tx, crewId, ct);
            var family = await AccountIdByCodeAsync(c, tx, npc, ct);
            await LockAccountsAsync(c, tx, ct, box, family);
            if (await BalanceAsync(c, tx, box, ct) < PactRules.CostCents)
                throw new EconomyRejected("insufficient_funds", $"La caja de la cuadrilla necesita {EconomyCatalog.FormatMoney(PactRules.CostCents)}.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "family_pact", $"Acuerdo con los {EconomyCatalog.FamilyName(r.FamilyCode)}", ct);
            await TransferMoneyAsync(c, tx, txId, from: box, to: family, PactRules.CostCents, ct);

            await using (var insert = new NpgsqlCommand("""
                INSERT INTO distrito.family_pacts (season, family_code, crew_id, coalition_id, signed_at)
                VALUES (@s, @f, @crew, (SELECT coalition_id FROM distrito.coalition_members WHERE crew_id = @crew), @now);
                INSERT INTO distrito.crew_events (crew_id, kind, character_id, detail) VALUES (@crew, 'pact_signed', @c, @name);
                """, c, tx))
            {
                insert.Parameters.AddWithValue("s", season);
                insert.Parameters.AddWithValue("f", r.FamilyCode);
                insert.Parameters.AddWithValue("crew", crewId);
                insert.Parameters.AddWithValue("now", clock.GetUtcNow());
                insert.Parameters.AddWithValue("c", r.CharacterId);
                insert.Parameters.AddWithValue("name", $"los {EconomyCatalog.FamilyName(r.FamilyCode)}");
                await insert.ExecuteNonQueryAsync(ct);
            }
            await GrantPactInfluenceAsync(c, tx, ct);
            return $"Firmaste el acuerdo con los {EconomyCatalog.FamilyName(r.FamilyCode)} hasta el fin de la temporada: " +
                   $"+{PactRules.ContractBonusPercent} % en sus contratos y +{PactRules.DailyInfluence} de influencia por día en su zona.";
        });

    /// <summary>Cuadrilla firmante del acuerdo de una familia en la temporada actual.</summary>
    private async Task<Guid?> PactHolderAsync(NpgsqlConnection c, NpgsqlTransaction tx, string family, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT crew_id FROM distrito.family_pacts WHERE season = @s AND family_code = @f", c, tx);
        cmd.Parameters.AddWithValue("s", TerritoryRules.SeasonAt(clock.GetUtcNow()));
        cmd.Parameters.AddWithValue("f", family);
        return await cmd.ExecuteScalarAsync(ct) as Guid?;
    }

    /// <summary>+2 de influencia por día (hora de Argentina) en la zona de cada familia con acuerdo. Idempotente por día.</summary>
    private async Task GrantPactInfluenceAsync(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var day = TerritoryRules.DayStart(now).ToOffset(TerritoryRules.ArgentinaOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pacts = new List<(string Family, Guid Crew)>();
        await using (var cmd = new NpgsqlCommand("SELECT family_code, crew_id FROM distrito.family_pacts WHERE season = @s", c, tx))
        {
            cmd.Parameters.AddWithValue("s", TerritoryRules.SeasonAt(now));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) pacts.Add((reader.GetString(0), reader.GetGuid(1)));
        }
        foreach (var (familyCode, crew) in pacts)
        {
            var reason = $"pacto:{familyCode}:{day}";
            await using var exists = new NpgsqlCommand("SELECT 1 FROM distrito.zone_influence WHERE crew_id = @c AND reason = @r", c, tx);
            exists.Parameters.AddWithValue("c", crew);
            exists.Parameters.AddWithValue("r", reason);
            if (await exists.ExecuteScalarAsync(ct) is not null)
                continue;
            if (TerritoryRules.ZoneOfNpc(NpcOfFamily(familyCode)) is { } zone)
                await InsertInfluenceAsync(c, tx, crew, zone, PactRules.DailyInfluence, reason, ct);
        }
    }

    public async Task GrantPactInfluenceAsync(CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await GrantPactInfluenceAsync(c, tx, ct);
        await tx.CommitAsync(ct);
    }

    private async Task<List<FamilyPactView>> LoadPactsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var myFaction = await FactionOfCrewAsync(c, tx, await CrewOfCharacterAsync(c, tx, characterId, ct), ct);
        var list = new List<FamilyPactView>();
        var raw = new List<(string Family, Guid Crew, string Name)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT p.family_code, p.crew_id, cr.name FROM distrito.family_pacts p JOIN distrito.crews cr ON cr.id = p.crew_id WHERE p.season = @s
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("s", TerritoryRules.SeasonAt(clock.GetUtcNow()));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) raw.Add((reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));
        }
        foreach (var (family, crew, name) in raw)
            list.Add(new FamilyPactView(family, name, myFaction is not null && await FactionOfCrewAsync(c, tx, crew, ct) == myFaction));
        return list;
    }
}
