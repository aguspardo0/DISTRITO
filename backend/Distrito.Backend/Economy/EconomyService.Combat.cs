using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>PvP: estado de zonas para el servidor de juego y la mercadería que queda tirada al quedar herido.</summary>
public sealed partial class EconomyService
{
    public const string GroundAccount = "system:suelo";
    public const int DisputeInfluence = 10;

    public async Task<CombatStateView> CombatStateAsync(CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var standings = await StandingsAsync(c, tx, TerritoryRules.SeasonAt(clock.GetUtcNow()), ct);
        await tx.CommitAsync(ct);
        var disputed = standings.Where(z => z.Value.Count(s => s.Points >= DisputeInfluence) >= 2).Select(z => z.Key).ToList();
        var now = clock.GetUtcNow();
        var events = DistrictEvents.Occurrences(DistrictEventList(), now, now.AddSeconds(1)).Where(e => e.IsActive(now)).Select(e => e.Zone).Distinct().ToList();
        var rulesFile = config.GetValue<string>("Zones:File") ?? Path.Combine(AppContext.BaseDirectory, "zonas.json");
        return new CombatStateView(disputed, events, ZoneRulesJson: File.Exists(rulesFile) ? await File.ReadAllTextAsync(rulesFile, ct) : null);
    }

    public async Task<DropView> KnockoutAsync(KnockoutRequest r, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var victim = await EnsureCharacterAccountAsync(c, tx, r.VictimId, ct);
        var ground = await AccountIdByCodeAsync(c, tx, GroundAccount, ct);
        await LockAccountsAsync(c, tx, ct, victim, ground);
        if (await ExistsAsync(c, tx, "SELECT 1 FROM distrito.ledger_transactions WHERE idempotency_key = @k", r.RequestId, ct))
        {
            await tx.CommitAsync(ct);
            return new DropView(r.DropId, await ItemsAtAsync(ground, $"bulto:{r.DropId}", ct));
        }

        var carried = new List<InventoryItem>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT s.item_code, d.name, sum(s.quantity)::int FROM distrito.item_stacks s JOIN distrito.item_definitions d ON d.code = s.item_code
             WHERE s.owner_account_id = @a AND s.location = @carried AND s.quantity > 0 GROUP BY s.item_code, d.name
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("a", victim);
            cmd.Parameters.AddWithValue("carried", EconomyCatalog.CarriedLocation);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) carried.Add(new InventoryItem(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }
        // Las armas y la munición se pierden siempre al morir; la mercadería depende de la regla de zona.
        if (!r.IncludeGoods)
            carried = carried.Where(i => Weapons.Find(i.ItemCode) is not null || Weapons.All.Any(w => w.AmmoItem == i.ItemCode)).ToList();
        DateTimeOffset? expires = null;
        if (carried.Count > 0)
        {
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "combat_drop", "Objetos tirados al morir", ct);
            foreach (var item in carried)
                await MoveItemsAsync(c, tx, txId, item.ItemCode, item.Quantity,
                    from: (victim, EconomyCatalog.CarriedLocation), to: (ground, $"bulto:{r.DropId}"), notEnough: "Sin mercadería.", ct);
            expires = await PlaceDropAsync(c, tx, r.DropId, r.VictimId, r.X, r.Y, r.Z, r.Seconds, "muerte", ct);
        }
        await tx.CommitAsync(ct);
        return new DropView(r.DropId, carried, r.X, r.Y, r.Z, expires);
    }

    /// <summary>Bultos que siguen en la calle (para el servidor de juego al arrancar).</summary>
    public async Task<List<DropView>> ActiveDropsAsync(CancellationToken ct)
    {
        var ground = Guid.Empty;
        var drops = new List<(Guid Id, double X, double Y, double Z, DateTimeOffset Exp)>();
        await using (var c = await db.OpenConnectionAsync(ct))
        {
            await using var cmd = new NpgsqlCommand("""
                SELECT id, x, y, z, expires_at FROM distrito.world_drops WHERE picked_at IS NULL AND expires_at > @now ORDER BY created_at;
                SELECT id FROM distrito.economic_accounts WHERE code = 'system:suelo';
                """, c);
            cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                drops.Add((reader.GetGuid(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetFieldValue<DateTimeOffset>(4)));
            await reader.NextResultAsync(ct);
            if (await reader.ReadAsync(ct)) ground = reader.GetGuid(0);
        }
        var result = new List<DropView>();
        foreach (var d in drops)
        {
            var items = await ItemsAtAsync(ground, $"bulto:{d.Id}", ct);
            if (items.Count > 0)
                result.Add(new DropView(d.Id, items, d.X, d.Y, d.Z, d.Exp));
        }
        return result;
    }

    public Task<EconomyActionResult> PickupAsync(PickupRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var picker = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var ground = await AccountIdByCodeAsync(c, tx, GroundAccount, ct);
            await LockAccountsAsync(c, tx, ct, picker, ground);
            var location = $"bulto:{r.DropId}";
            await using (var expiry = new NpgsqlCommand("SELECT expires_at FROM distrito.world_drops WHERE id = @id FOR UPDATE", c, tx))
            {
                expiry.Parameters.AddWithValue("id", r.DropId);
                if (await expiry.ExecuteScalarAsync(ct) is DateTime exp && new DateTimeOffset(DateTime.SpecifyKind(exp, DateTimeKind.Utc)) <= clock.GetUtcNow())
                    throw new EconomyRejected("drop_expired", "Eso ya no está: se lo llevó la calle.");
            }
            var items = new List<(string Code, int Qty)>();
            await using (var cmd = new NpgsqlCommand(
                "SELECT item_code, sum(quantity)::int FROM distrito.item_stacks WHERE owner_account_id = @g AND location = @l AND quantity > 0 GROUP BY item_code", c, tx))
            {
                cmd.Parameters.AddWithValue("g", ground);
                cmd.Parameters.AddWithValue("l", location);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) items.Add((reader.GetString(0), reader.GetInt32(1)));
            }
            if (items.Count == 0)
                throw new EconomyRejected("drop_empty", "Ya no queda nada: alguien lo levantó antes.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "combat_pickup", "Objetos levantados de la calle", ct);
            await using (var mark = new NpgsqlCommand("UPDATE distrito.world_drops SET picked_by = @c, picked_at = @now WHERE id = @id", c, tx))
            {
                mark.Parameters.AddWithValue("c", r.CharacterId);
                mark.Parameters.AddWithValue("now", clock.GetUtcNow());
                mark.Parameters.AddWithValue("id", r.DropId);
                await mark.ExecuteNonQueryAsync(ct);
            }
            foreach (var (code, qty) in items)
                await EnsureFitsAsync(c, tx, picker, code, qty, ct);
            foreach (var (code, qty) in items)
                await MoveItemsAsync(c, tx, txId, code, qty, from: (ground, location), to: (picker, EconomyCatalog.CarriedLocation), notEnough: "Sin mercadería.", ct, actor: r.CharacterId);
            var names = new List<string>();
            foreach (var (code, qty) in items)
                names.Add($"{qty} × {(await ItemInfoAsync(c, tx, code, ct)).Name}");
            return $"Levantaste {string.Join(", ", names)}.";
        });

    /// <summary>Munición gastada (la descuenta el servidor de juego en tandas). Si hay menos, se descuenta lo que haya.</summary>
    public async Task<int> UseAmmoAsync(AmmoUseRequest r, CancellationToken ct)
    {
        if (r.Rounds <= 0 || Weapons.All.All(w => w.AmmoItem != r.AmmoItem))
            return 0;
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var shooter = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
        var ground = await AccountIdByCodeAsync(c, tx, GroundAccount, ct);
        await LockAccountsAsync(c, tx, ct, shooter, ground);
        if (await ExistsAsync(c, tx, "SELECT 1 FROM distrito.ledger_transactions WHERE idempotency_key = @k", r.RequestId, ct))
        {
            await tx.CommitAsync(ct);
            return 0;
        }
        await using var have = new NpgsqlCommand(
            "SELECT COALESCE(sum(quantity), 0)::int FROM distrito.item_stacks WHERE owner_account_id = @a AND item_code = @i AND location = @l", c, tx);
        have.Parameters.AddWithValue("a", shooter);
        have.Parameters.AddWithValue("i", r.AmmoItem);
        have.Parameters.AddWithValue("l", EconomyCatalog.CarriedLocation);
        var used = Math.Min(r.Rounds, (int)(await have.ExecuteScalarAsync(ct))!);
        if (used > 0)
        {
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "ammo_used", "Munición gastada", ct);
            await MoveItemsAsync(c, tx, txId, r.AmmoItem, used, from: (shooter, EconomyCatalog.CarriedLocation), to: (ground, "disparado"),
                notEnough: "Sin munición.", ct);
        }
        await tx.CommitAsync(ct);
        return used;
    }

    private async Task<List<InventoryItem>> ItemsAtAsync(Guid account, string location, CancellationToken ct)
    {
        var list = new List<InventoryItem>();
        await using var cmd = db.CreateCommand("""
            SELECT s.item_code, d.name, sum(s.quantity)::int FROM distrito.item_stacks s JOIN distrito.item_definitions d ON d.code = s.item_code
             WHERE s.owner_account_id = @a AND s.location = @l AND s.quantity > 0 GROUP BY s.item_code, d.name
            """);
        cmd.Parameters.AddWithValue("a", account);
        cmd.Parameters.AddWithValue("l", location);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(new InventoryItem(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return list;
    }
}
