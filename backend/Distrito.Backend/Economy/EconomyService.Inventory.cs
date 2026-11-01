using System.Text.Json;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Inventario, etapas 1–2: ver lo que se lleva encima con sus datos, darle algo a otro jugador y soltarlo en la calle.
/// Todo pasa por el mismo libro de objetos (item_stacks + item_movements) que ya usa la economía: nada se crea
/// ni se duplica, cada movimiento es atómico, idempotente y queda registrado con quién lo hizo.
/// </summary>
public sealed partial class EconomyService
{
    public InventoryLimits Limits()
    {
        var file = config.GetValue<string>("Inventory:File") ?? Path.Combine(AppContext.BaseDirectory, "inventario.json");
        if (!File.Exists(file))
            return InventoryLimits.Unlimited;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("personal", out var p))
                return InventoryLimits.Unlimited;
            static double? Num(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            return new InventoryLimits(Num(p, "kg"), Num(p, "litros"));
        }
        catch (JsonException)
        {
            return InventoryLimits.Unlimited;
        }
    }

    public async Task<InventoryView> InventoryAsync(Guid characterId, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var account = await EnsureCharacterAccountAsync(c, tx, characterId, ct);
        var items = await EntriesAtAsync(c, tx, account, EconomyCatalog.CarriedLocation, ct);
        var history = new List<InventoryMovement>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT m.created_at, d.name, m.quantity, (m.to_account_id = @a AND m.to_location = @l), t.description
              FROM distrito.item_movements m
              JOIN distrito.item_definitions d ON d.code = m.item_code
              JOIN distrito.ledger_transactions t ON t.id = m.transaction_id
             WHERE (m.to_account_id = @a AND m.to_location = @l) OR (m.from_account_id = @a AND m.from_location = @l)
             ORDER BY m.id DESC LIMIT 20
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("a", account);
            cmd.Parameters.AddWithValue("l", EconomyCatalog.CarriedLocation);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                history.Add(new InventoryMovement(r.GetFieldValue<DateTimeOffset>(0), r.GetString(1), r.GetInt32(2), r.GetBoolean(3), r.GetString(4)));
        }
        await tx.CommitAsync(ct);
        var limits = Limits();
        return new InventoryView(items, limits.PersonalKg, limits.PersonalL, history);
    }

    private static async Task<List<InventoryEntry>> EntriesAtAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid account, string location, CancellationToken ct)
    {
        var list = new List<InventoryEntry>();
        await using var cmd = new NpgsqlCommand("""
            SELECT d.code, d.name, d.description, d.kind, d.category, sum(s.quantity)::int, d.weight_kg::float8, d.volume_l::float8,
                   d.legal_status, d.reference_price_cents
              FROM distrito.item_stacks s JOIN distrito.item_definitions d ON d.code = s.item_code
             WHERE s.owner_account_id = @a AND s.location = @l AND s.quantity > 0
             GROUP BY d.code ORDER BY d.kind, d.name
            """, c, tx);
        cmd.Parameters.AddWithValue("a", account);
        cmd.Parameters.AddWithValue("l", location);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new InventoryEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5),
                r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetInt64(9)));
        return list;
    }

    /// <summary>Rechaza si lo que entra no cabe encima del personaje (con capacidad null no limita).</summary>
    private async Task EnsureFitsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid account, string itemCode, int quantity, CancellationToken ct)
    {
        var limits = Limits();
        if (limits.PersonalKg is null && limits.PersonalL is null)
            return;
        await using var cmd = new NpgsqlCommand("""
            SELECT COALESCE(sum(s.quantity * d.weight_kg), 0)::float8, COALESCE(sum(s.quantity * d.volume_l), 0)::float8
              FROM distrito.item_stacks s JOIN distrito.item_definitions d ON d.code = s.item_code
             WHERE s.owner_account_id = @a AND s.location = @l;
            SELECT weight_kg::float8, volume_l::float8 FROM distrito.item_definitions WHERE code = @i;
            """, c, tx);
        cmd.Parameters.AddWithValue("a", account);
        cmd.Parameters.AddWithValue("l", EconomyCatalog.CarriedLocation);
        cmd.Parameters.AddWithValue("i", itemCode);
        double kg, l, unitKg = 0, unitL = 0;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            kg = r.GetDouble(0);
            l = r.GetDouble(1);
            await r.NextResultAsync(ct);
            if (await r.ReadAsync(ct)) { unitKg = r.GetDouble(0); unitL = r.GetDouble(1); }
        }
        if (limits.PersonalKg is { } maxKg && kg + unitKg * quantity > maxKg + 1e-9)
            throw new EconomyRejected("too_heavy", $"No podés con tanto peso: llevás {kg:0.#} kg de {maxKg:0.#}.");
        if (limits.PersonalL is { } maxL && l + unitL * quantity > maxL + 1e-9)
            throw new EconomyRejected("too_bulky", $"No te entra: llevás {l:0.#} L de {maxL:0.#}.");
    }

    private static async Task<(string Name, string Kind)> ItemInfoAsync(NpgsqlConnection c, NpgsqlTransaction tx, string itemCode, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT name, kind FROM distrito.item_definitions WHERE code = @i", c, tx);
        cmd.Parameters.AddWithValue("i", itemCode);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new EconomyRejected("unknown_item", "Ese objeto no existe.");
        return (r.GetString(0), r.GetString(1));
    }

    /// <summary>Le da objetos a otro personaje (el servidor de juego ya validó que estén cerca).</summary>
    public Task<EconomyActionResult> GiveAsync(GiveItemRequest r, CancellationToken ct) =>
        RunAsync(r.FromCharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (r.Quantity <= 0 || r.Quantity > 1_000_000)
                throw new EconomyRejected("invalid_quantity", "Cantidad inválida.");
            if (r.FromCharacterId == r.ToCharacterId)
                throw new EconomyRejected("same_character", "No te podés dar cosas a vos mismo.");
            var (name, kind) = await ItemInfoAsync(c, tx, r.ItemCode, ct);
            if (!ItemKinds.Of(kind).Tradable)
                throw new EconomyRejected("not_tradable", $"{name} no se puede pasar a otro.");
            var from = await EnsureCharacterAccountAsync(c, tx, r.FromCharacterId, ct);
            var to = await EnsureCharacterAccountAsync(c, tx, r.ToCharacterId, ct);
            await LockAccountsAsync(c, tx, ct, from, to);
            await EnsureFitsAsync(c, tx, to, r.ItemCode, r.Quantity, ct);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "item_give", $"Entrega en mano de {r.Quantity} × {name}", ct);
            await MoveItemsAsync(c, tx, txId, r.ItemCode, r.Quantity,
                from: (from, EconomyCatalog.CarriedLocation), to: (to, EconomyCatalog.CarriedLocation),
                notEnough: $"No tenés {r.Quantity} × {name}.", ct, actor: r.FromCharacterId);
            return $"Le diste {r.Quantity} × {name}.";
        });

    /// <summary>Suelta objetos en la calle: quedan en un bulto con lugar y vencimiento que cualquiera puede levantar si la zona lo permite.</summary>
    public async Task<DropItemResult> DropAsync(DropItemRequest r, CancellationToken ct)
    {
        DropView? drop = null;
        var result = await RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (r.Quantity <= 0 || r.Quantity > 1_000_000)
                throw new EconomyRejected("invalid_quantity", "Cantidad inválida.");
            var (name, kind) = await ItemInfoAsync(c, tx, r.ItemCode, ct);
            if (!ItemKinds.Of(kind).Tradable)
                throw new EconomyRejected("not_tradable", $"{name} no se puede soltar.");
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var ground = await AccountIdByCodeAsync(c, tx, GroundAccount, ct);
            await LockAccountsAsync(c, tx, ct, me, ground);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "item_drop", $"Soltó {r.Quantity} × {name} en la calle", ct);
            await MoveItemsAsync(c, tx, txId, r.ItemCode, r.Quantity,
                from: (me, EconomyCatalog.CarriedLocation), to: (ground, $"bulto:{r.DropId}"),
                notEnough: $"No tenés {r.Quantity} × {name}.", ct, actor: r.CharacterId);
            var expires = await PlaceDropAsync(c, tx, r.DropId, r.CharacterId, r.X, r.Y, r.Z, r.Seconds, "soltado", ct);
            drop = new DropView(r.DropId, [new InventoryItem(r.ItemCode, name, r.Quantity)], r.X, r.Y, r.Z, expires);
            return $"Soltaste {r.Quantity} × {name}.";
        });
        return new DropItemResult(result, result.Ok && result.Message != "Ya estaba hecho." ? drop : null);
    }

    private async Task<DateTimeOffset> PlaceDropAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid dropId, Guid? characterId,
        double x, double y, double z, int seconds, string reason, CancellationToken ct)
    {
        var expires = clock.GetUtcNow().AddSeconds(Math.Clamp(seconds, 10, 86400));
        await using var place = new NpgsqlCommand("""
            INSERT INTO distrito.world_drops (id, victim_id, x, y, z, created_at, expires_at, reason) VALUES (@id, @v, @x, @y, @z, @now, @exp, @reason)
            ON CONFLICT (id) DO NOTHING
            """, c, tx);
        place.Parameters.AddWithValue("id", dropId);
        place.Parameters.AddWithValue("v", (object?)characterId ?? DBNull.Value);
        place.Parameters.AddWithValue("x", x);
        place.Parameters.AddWithValue("y", y);
        place.Parameters.AddWithValue("z", z);
        place.Parameters.AddWithValue("now", clock.GetUtcNow());
        place.Parameters.AddWithValue("exp", expires);
        place.Parameters.AddWithValue("reason", reason);
        await place.ExecuteNonQueryAsync(ct);
        return expires;
    }
}
