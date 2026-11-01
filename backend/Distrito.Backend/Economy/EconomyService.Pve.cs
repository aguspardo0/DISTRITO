using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Asaltos a NPC (aprobado por el equipo 2026-11-01). El camión carga mercadería real del stock del proveedor (pve:&lt;evento&gt;)
/// y, si lo asaltan, cae a la calle; si termina la ruta, vuelve al stock. La plata del cobrador y de la caja de los locales
/// sale de la cuenta real de la familia. El contrabando entra de afuera (emisión). Asaltar a una familia baja la
/// reputación con ella (y eso empeora su ánimo, que se calcula con la reputación perdida).
/// </summary>
public sealed partial class EconomyService
{
    private static string PveLocation(string eventId) => $"pve:{eventId}";

    public async Task<int> PveLoadAsync(PveLoadRequest r, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var key = $"pve-load:{r.EventId}";
        var supplier = await AccountIdByCodeAsync(c, tx, r.Supplier, ct);
        await LockAccountsAsync(c, tx, ct, supplier);
        if (await ExistsAsync(c, tx, "SELECT 1 FROM distrito.ledger_transactions WHERE idempotency_key = @k", key, ct))
        {
            var already = await QuantityAsync(c, tx, supplier, r.ItemCode, PveLocation(r.EventId), ct);
            await tx.CommitAsync(ct);
            return already;
        }
        var stock = await QuantityAsync(c, tx, supplier, r.ItemCode, StockLocation(r.Supplier), ct);
        var quantity = Math.Min(stock, Math.Clamp(r.Quantity, 0, 1000));
        if (quantity > 0)
        {
            var txId = await NewLedgerTransactionAsync(c, tx, key, "pve_load", $"Camión de reparto ({r.EventId}): carga del stock", ct);
            await MoveItemsAsync(c, tx, txId, r.ItemCode, quantity, from: (supplier, StockLocation(r.Supplier)), to: (supplier, PveLocation(r.EventId)),
                notEnough: "Sin stock.", ct);
        }
        await tx.CommitAsync(ct);
        return quantity;
    }

    public async Task PveUnloadAsync(PveUnloadRequest r, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var supplier = await AccountIdByCodeAsync(c, tx, r.Supplier, ct);
        await LockAccountsAsync(c, tx, ct, supplier);
        var items = await EntriesAtAsync(c, tx, supplier, PveLocation(r.EventId), ct);
        if (items.Count > 0)
        {
            var txId = await NewLedgerTransactionAsync(c, tx, $"pve-unload:{r.EventId}", "pve_unload", $"Camión de reparto ({r.EventId}): vuelve al stock", ct);
            foreach (var item in items)
                await MoveItemsAsync(c, tx, txId, item.ItemCode, item.Quantity, from: (supplier, PveLocation(r.EventId)), to: (supplier, StockLocation(r.Supplier)),
                    notEnough: "Sin carga.", ct);
        }
        await tx.CommitAsync(ct);
    }

    private static async Task PenalizeAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid character, string family, int reputation, string reason, Guid txId, CancellationToken ct)
    {
        if (reputation != 0 && family.Length > 0)
            await GrantReputationAsync(c, tx, character, new ReputationReward(family, reputation), reason, txId, ct);
    }

    public async Task<DropItemResult> PveRobGoodsAsync(PveRobGoodsRequest r, CancellationToken ct)
    {
        DropView? drop = null;
        var result = await RunAsync(r.CharacterId, $"pve-rob:{r.EventId}", ct, async (c, tx) =>
        {
            var supplier = await AccountIdByCodeAsync(c, tx, r.Supplier, ct);
            var ground = await AccountIdByCodeAsync(c, tx, GroundAccount, ct);
            await LockAccountsAsync(c, tx, ct, supplier, ground);
            var items = await EntriesAtAsync(c, tx, supplier, PveLocation(r.EventId), ct);
            if (items.Count == 0)
                throw new EconomyRejected("empty", "El camión venía vacío.");
            var txId = await NewLedgerTransactionAsync(c, tx, $"pve-rob:{r.EventId}", "pve_robbery", $"Asalto al camión de reparto ({r.EventId})", ct);
            foreach (var item in items)
                await MoveItemsAsync(c, tx, txId, item.ItemCode, item.Quantity, from: (supplier, PveLocation(r.EventId)), to: (ground, $"bulto:{r.DropId}"),
                    notEnough: "Sin carga.", ct, actor: r.CharacterId);
            var expires = await PlaceDropAsync(c, tx, r.DropId, r.CharacterId, r.X, r.Y, r.Z, r.Seconds, "soltado", ct);
            await PenalizeAsync(c, tx, r.CharacterId, r.Family, r.Reputation, "asalto al camión de reparto", txId, ct);
            drop = new DropView(r.DropId, items.Select(i => new InventoryItem(i.ItemCode, i.Name, i.Quantity)).ToList(), r.X, r.Y, r.Z, expires);
            return $"¡El chofer se rindió! {string.Join(", ", items.Select(i => $"{i.Quantity} × {i.Name}"))} quedó en la calle."
                   + (r.Reputation != 0 ? $" ({r.Reputation} de reputación con los {EconomyCatalog.FamilyName(r.Family)})" : "");
        });
        return new DropItemResult(result, result.Ok && result.Message != "Ya estaba hecho." ? drop : null);
    }

    public Task<EconomyActionResult> PveRobCashAsync(PveRobCashRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var victim = await AccountIdByCodeAsync(c, tx, r.Account, ct);
            await LockAccountsAsync(c, tx, ct, me, victim);
            var wanted = r.MinCents + (long)(Random.Shared.NextDouble() * Math.Max(0, r.MaxCents - r.MinCents));
            var amount = Math.Min(wanted, await BalanceAsync(c, tx, victim, ct));
            if (amount <= 0)
                throw new EconomyRejected("empty", "No había un peso.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "pve_robbery", r.Reason, ct);
            await TransferMoneyAsync(c, tx, txId, from: victim, to: me, amount, ct);
            await PenalizeAsync(c, tx, r.CharacterId, r.Family, r.Reputation, r.Reason, txId, ct);
            return $"Te llevaste {EconomyCatalog.FormatMoney(amount)}."
                   + (r.Reputation != 0 ? $" ({r.Reputation} de reputación con los {EconomyCatalog.FamilyName(r.Family)})" : "");
        });

    /// <summary>Cargamento de contrabando: entra de afuera (emisión) y queda en un bulto en el muelle.</summary>
    public async Task<DropView?> PveContrabandAsync(PveContrabandRequest r, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var key = $"pve-contraband:{r.EventId}";
        if (await ExistsAsync(c, tx, "SELECT 1 FROM distrito.ledger_transactions WHERE idempotency_key = @k", key, ct))
        {
            await tx.CommitAsync(ct);
            return null;
        }
        var emission = await AccountIdByCodeAsync(c, tx, "system:emision", ct);
        var ground = await AccountIdByCodeAsync(c, tx, GroundAccount, ct);
        await LockAccountsAsync(c, tx, ct, emission, ground);
        var txId = await NewLedgerTransactionAsync(c, tx, key, "pve_contraband", $"Contrabando en Los Muelles ({r.EventId})", ct);
        var items = new List<InventoryItem>();
        foreach (var line in r.Items.Where(i => i.Quantity > 0))
        {
            var (name, _) = await ItemInfoAsync(c, tx, line.ItemCode, ct);
            await using var give = new NpgsqlCommand("""
                INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity) VALUES (@id, @g, @i, @l, @q)
                ON CONFLICT (owner_account_id, item_code, location) DO UPDATE SET quantity = distrito.item_stacks.quantity + EXCLUDED.quantity;
                INSERT INTO distrito.item_movements (transaction_id, item_code, from_account_id, from_location, to_account_id, to_location, quantity)
                VALUES (@tx, @i, @e, 'contrabando', @g, @l, @q);
                """, c, tx);
            give.Parameters.AddWithValue("id", Guid.CreateVersion7());
            give.Parameters.AddWithValue("g", ground);
            give.Parameters.AddWithValue("i", line.ItemCode);
            give.Parameters.AddWithValue("l", $"bulto:{r.DropId}");
            give.Parameters.AddWithValue("q", line.Quantity);
            give.Parameters.AddWithValue("tx", txId);
            give.Parameters.AddWithValue("e", emission);
            await give.ExecuteNonQueryAsync(ct);
            items.Add(new InventoryItem(line.ItemCode, name, line.Quantity));
        }
        var expires = await PlaceDropAsync(c, tx, r.DropId, null, r.X, r.Y, r.Z, r.Seconds, "soltado", ct);
        await tx.CommitAsync(ct);
        return new DropView(r.DropId, items, r.X, r.Y, r.Z, expires);
    }
}
