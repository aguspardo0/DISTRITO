using Distrito.Shared.Economy;
using Distrito.Backend.Crews;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Inventario, etapa 5: pedidos a proveedores. Nada aparece mágicamente: al pedir se paga y la mercadería sale del
/// stock real del proveedor a una ubicación apartada (pedido:&lt;id&gt;). Después se retira en el mostrador o el camión
/// del proveedor la lleva al depósito de la cuadrilla.
/// </summary>
public sealed partial class EconomyService
{
    private DateTimeOffset CycleStart(long cycle) => DateTimeOffset.FromUnixTimeSeconds(cycle * CycleSeconds);

    private static string StockLocation(string supplierCode) => supplierCode.Replace("npc:", "");

    public async Task<SupplierView> SupplierAsync(Guid characterId, string supplierCode, CancellationToken ct)
    {
        if (!SupplierRules.Catalog.TryGetValue(supplierCode, out var codes))
            return new SupplierView(supplierCode, supplierCode, [], [], false, 0);
        var current = CurrentCycle();
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var mood = await MoodOfNpcAsync(c, tx, supplierCode, ct);
        var items = new List<SupplierItem>();
        foreach (var code in codes)
        {
            await using var cmd = new NpgsqlCommand("SELECT name, description, reference_price_cents, weight_kg::float8 FROM distrito.item_definitions WHERE code = @c", c, tx);
            cmd.Parameters.AddWithValue("c", code);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
                items.Add(new SupplierItem(code, r.GetString(0), r.GetString(1), FamilyRules.SellPrice(r.GetInt64(2), mood), r.GetDouble(3)));
        }
        var orders = new List<SupplierOrderView>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT o.id, o.item_code, d.name, o.quantity, o.goods_cents, o.delivery_cents, o.delivery, o.status, o.ready_cycle, o.deliver_cycle
              FROM distrito.supplier_orders o JOIN distrito.item_definitions d ON d.code = o.item_code
             WHERE o.character_id = @c AND o.supplier_code = @s AND (o.status = 'preparando' OR o.closed_at > now() - interval '1 day')
             ORDER BY o.created_at DESC LIMIT 10
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            cmd.Parameters.AddWithValue("s", supplierCode);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                orders.Add(new SupplierOrderView(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt64(4), r.GetInt64(5),
                    r.GetString(6), r.GetString(7), r.GetString(7) == "preparando" && current >= r.GetInt64(8),
                    CycleStart(r.GetInt64(8)), CycleStart(r.GetInt64(9))));
        }
        var queue = await UnitsInQueueAsync(c, tx, supplierCode, current, ct);
        var seat = await CrewSeatAsync(c, tx, characterId, ct);
        var hasWarehouse = seat is not null && await WarehouseOfAsync(c, tx, seat.CrewId, false, ct) is not null;
        var businesses = new List<SupplierBusiness>();
        await using (var cmd = new NpgsqlCommand("SELECT kind FROM distrito.businesses WHERE owner_character_id = @c", c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                if (EconomyCatalog.BusinessTypes.TryGetValue(r.GetString(0), out var type) && type.SupplierNpc == supplierCode)
                    businesses.Add(new SupplierBusiness(type.Code, type.Name, type.ItemCode));
        }
        await tx.CommitAsync(ct);
        return new SupplierView(supplierCode, SupplierRules.Names.GetValueOrDefault(supplierCode, supplierCode), items, orders, hasWarehouse, queue, businesses);
    }

    private static async Task<int> UnitsInQueueAsync(NpgsqlConnection c, NpgsqlTransaction tx, string supplierCode, long current, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT COALESCE(sum(quantity), 0)::int FROM distrito.supplier_orders WHERE supplier_code = @s AND status = 'preparando' AND ready_cycle > @cycle
            """, c, tx);
        cmd.Parameters.AddWithValue("s", supplierCode);
        cmd.Parameters.AddWithValue("cycle", current);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public Task<EconomyActionResult> PlaceSupplierOrderAsync(SupplierOrderRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (!SupplierRules.Catalog.TryGetValue(r.SupplierCode, out var codes) || !codes.Contains(r.ItemCode))
                throw new EconomyRejected("not_sold", "Ese proveedor no toma pedidos de eso.");
            if (r.Quantity < SupplierRules.MinUnits || r.Quantity > SupplierRules.MaxUnits)
                throw new EconomyRejected("invalid_quantity", $"Los pedidos son de {SupplierRules.MinUnits} a {SupplierRules.MaxUnits} unidades.");
            if (r.Delivery is not (SupplierRules.Pickup or SupplierRules.Delivery or SupplierRules.ToBusiness))
                throw new EconomyRejected("invalid_delivery", "Elegí retiro o envío.");

            Guid? warehouseId = null, businessId = null;
            if (r.Delivery == SupplierRules.ToBusiness)
            {
                await using var find = new NpgsqlCommand("SELECT id, kind FROM distrito.businesses WHERE owner_character_id = @c", c, tx);
                find.Parameters.AddWithValue("c", r.CharacterId);
                await using var reader = await find.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    if (EconomyCatalog.BusinessTypes.TryGetValue(reader.GetString(1), out var type) && type.ItemCode == r.ItemCode && type.SupplierNpc == r.SupplierCode)
                        businessId = reader.GetGuid(0);
                if (businessId is null)
                    throw new EconomyRejected("no_business", "No tenés un negocio que venda eso para mandárselo.");
            }
            var buyer = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var seller = await AccountIdByCodeAsync(c, tx, r.SupplierCode, ct);
            if (r.Delivery == SupplierRules.Delivery)
            {
                var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("no_warehouse", "Para el envío hace falta el depósito de una cuadrilla.");
                var row = await WarehouseOfAsync(c, tx, seat.CrewId, false, ct) ?? throw new EconomyRejected("no_warehouse", "Tu cuadrilla no tiene depósito: abrilo en La Oficina o retiralo vos.");
                var (_, canDeposit, _, _) = Rights(seat.Role, row.Permissions);
                if (!canDeposit)
                    throw new EconomyRejected("forbidden", "Tu jefe no te dejó guardar en el depósito.");
                var box = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
                await EnsureWarehouseFitsAsync(c, tx, box, row, r.ItemCode, r.Quantity, ct);
                warehouseId = row.Id;
            }
            await LockAccountsAsync(c, tx, ct, buyer, seller);

            var (name, basePrice) = await LoadItemAsync(c, tx, r.ItemCode, ct);
            var unit = FamilyRules.SellPrice(basePrice, await MoodOfNpcAsync(c, tx, r.SupplierCode, ct));
            var goods = SupplierRules.GoodsCents(unit, r.Quantity);
            var fee = SupplierRules.IsShipped(r.Delivery) ? SupplierRules.DeliveryCents(goods) : 0;
            var total = goods + fee;
            if (await BalanceAsync(c, tx, buyer, ct) < total)
                throw new EconomyRejected("insufficient_funds", $"No te alcanza: el pedido cuesta {EconomyCatalog.FormatMoney(total)}.");

            var current = CurrentCycle();
            var ahead = await UnitsInQueueAsync(c, tx, r.SupplierCode, current, ct);
            var ready = current + SupplierRules.PrepCycles(r.Quantity, ahead);
            var deliver = ready + (SupplierRules.IsShipped(r.Delivery) ? SupplierRules.DeliveryExtraCycles : 0);
            var id = Guid.CreateVersion7();

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "supplier_order",
                $"Pedido de {r.Quantity} × {name} a {SupplierRules.Names.GetValueOrDefault(r.SupplierCode, r.SupplierCode)}", ct);
            await TransferMoneyAsync(c, tx, txId, from: buyer, to: seller, total, ct);
            await MoveItemsAsync(c, tx, txId, r.ItemCode, r.Quantity, from: (seller, StockLocation(r.SupplierCode)), to: (seller, $"pedido:{id}"),
                notEnough: "El proveedor no tiene tanto en existencia.", ct, actor: r.CharacterId);
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO distrito.supplier_orders (id, character_id, supplier_code, item_code, quantity, goods_cents, delivery_cents, delivery, warehouse_id, business_id, status, ready_cycle, deliver_cycle)
                VALUES (@id, @c, @s, @i, @q, @g, @f, @d, @w, @b, 'preparando', @ready, @deliver)
                """, c, tx))
            {
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("c", r.CharacterId);
                insert.Parameters.AddWithValue("s", r.SupplierCode);
                insert.Parameters.AddWithValue("i", r.ItemCode);
                insert.Parameters.AddWithValue("q", r.Quantity);
                insert.Parameters.AddWithValue("g", goods);
                insert.Parameters.AddWithValue("f", fee);
                insert.Parameters.AddWithValue("d", r.Delivery);
                insert.Parameters.AddWithValue("w", (object?)warehouseId ?? DBNull.Value);
                insert.Parameters.AddWithValue("b", (object?)businessId ?? DBNull.Value);
                insert.Parameters.AddWithValue("ready", ready);
                insert.Parameters.AddWithValue("deliver", deliver);
                await insert.ExecuteNonQueryAsync(ct);
            }
            var minutes = (int)Math.Ceiling((CycleStart(deliver) - clock.GetUtcNow()).TotalMinutes);
            return $"Pedido hecho: {r.Quantity} × {name} por {EconomyCatalog.FormatMoney(total)}"
                   + (r.Quantity >= SupplierRules.VolumeDiscountFrom ? $" ({SupplierRules.VolumeDiscountPercent} % de descuento)" : "")
                   + (r.Delivery switch
                   {
                       SupplierRules.Delivery => $". Llega al depósito en unos {minutes} min.",
                       SupplierRules.ToBusiness => $". Llega a tu negocio en unos {minutes} min.",
                       _ => $". Listo para retirar en unos {minutes} min.",
                   });
        });

    private sealed record OrderRow(Guid Id, Guid CharacterId, string Supplier, string ItemCode, int Quantity, long Goods, long Fee,
        string Delivery, Guid? WarehouseId, string Status, long Ready, long Deliver, Guid? BusinessId = null);

    private static async Task<OrderRow?> LockOrderAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid id, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, character_id, supplier_code, item_code, quantity, goods_cents, delivery_cents, delivery, warehouse_id, status, ready_cycle, deliver_cycle, business_id
              FROM distrito.supplier_orders WHERE id = @id FOR UPDATE
            """, c, tx);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new OrderRow(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt64(5), r.GetInt64(6),
                r.GetString(7), r.IsDBNull(8) ? null : r.GetGuid(8), r.GetString(9), r.GetInt64(10), r.GetInt64(11), r.IsDBNull(12) ? null : r.GetGuid(12))
            : null;
    }

    private static async Task CloseOrderAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid id, string status, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE distrito.supplier_orders SET status = @s, closed_at = now() WHERE id = @id", c, tx);
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Cancelar mientras se prepara: vuelve la plata entera y la mercadería al stock del proveedor.</summary>
    public Task<EconomyActionResult> CancelSupplierOrderAsync(SupplierOrderActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var order = await LockOrderAsync(c, tx, r.OrderId, ct);
            if (order is null || order.CharacterId != r.CharacterId)
                throw new EconomyRejected("not_found", "Ese pedido no es tuyo.");
            if (order.Status != "preparando")
                throw new EconomyRejected("closed", "Ese pedido ya está cerrado.");
            if (CurrentCycle() >= order.Ready)
                throw new EconomyRejected("already_ready", "Ya está preparado: no se puede cancelar.");
            var buyer = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var seller = await AccountIdByCodeAsync(c, tx, order.Supplier, ct);
            await LockAccountsAsync(c, tx, ct, buyer, seller);
            var (name, _) = await ItemInfoAsync(c, tx, order.ItemCode, ct);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "supplier_cancel", $"Pedido cancelado: {order.Quantity} × {name}", ct);
            await TransferMoneyAsync(c, tx, txId, from: seller, to: buyer, order.Goods + order.Fee, ct);
            await MoveItemsAsync(c, tx, txId, order.ItemCode, order.Quantity, from: (seller, $"pedido:{order.Id}"), to: (seller, StockLocation(order.Supplier)),
                notEnough: "Sin stock.", ct, actor: r.CharacterId);
            await CloseOrderAsync(c, tx, order.Id, "cancelado", ct);
            return $"Pedido cancelado: te devolvieron {EconomyCatalog.FormatMoney(order.Goods + order.Fee)}.";
        });

    /// <summary>Retirar en el mostrador (el servidor de juego valida que esté ahí).</summary>
    public Task<EconomyActionResult> PickupSupplierOrderAsync(SupplierOrderActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var order = await LockOrderAsync(c, tx, r.OrderId, ct);
            if (order is null || order.CharacterId != r.CharacterId)
                throw new EconomyRejected("not_found", "Ese pedido no es tuyo.");
            if (order.Status != "preparando")
                throw new EconomyRejected("closed", "Ese pedido ya está cerrado.");
            if (order.Delivery != SupplierRules.Pickup)
                throw new EconomyRejected("is_delivery", "Ese pedido va por envío.");
            if (CurrentCycle() < order.Ready)
                throw new EconomyRejected("not_ready", "Todavía lo están preparando.");
            var buyer = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var seller = await AccountIdByCodeAsync(c, tx, order.Supplier, ct);
            await LockAccountsAsync(c, tx, ct, buyer, seller);
            await EnsureFitsAsync(c, tx, buyer, order.ItemCode, order.Quantity, ct);
            var (name, _) = await ItemInfoAsync(c, tx, order.ItemCode, ct);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "supplier_pickup", $"Retiro del pedido: {order.Quantity} × {name}", ct);
            await MoveItemsAsync(c, tx, txId, order.ItemCode, order.Quantity, from: (seller, $"pedido:{order.Id}"), to: (buyer, EconomyCatalog.CarriedLocation),
                notEnough: "Sin stock.", ct, actor: r.CharacterId);
            await CloseOrderAsync(c, tx, order.Id, "entregado", ct);
            return $"Retiraste tu pedido: {order.Quantity} × {name}.";
        });

    /// <summary>Envío al negocio: la mercadería entra al stock del local (si el negocio ya no existe, queda para retirar).</summary>
    private async Task DeliverToBusinessAsync(NpgsqlConnection c, NpgsqlTransaction tx, OrderRow order, CancellationToken ct)
    {
        var seller = await AccountIdByCodeAsync(c, tx, order.Supplier, ct);
        var (name, _) = await ItemInfoAsync(c, tx, order.ItemCode, ct);
        Guid? account = null;
        string? location = null;
        if (order.BusinessId is { } bid)
        {
            await using var find = new NpgsqlCommand("SELECT account_id, location FROM distrito.businesses WHERE id = @id FOR UPDATE", c, tx);
            find.Parameters.AddWithValue("id", bid);
            await using var r = await find.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) (account, location) = (r.GetGuid(0), r.GetString(1));
        }
        if (account is { } shop && location is not null)
        {
            await LockAccountsAsync(c, tx, ct, seller, shop);
            var txId = await NewLedgerTransactionAsync(c, tx, $"supplier-delivery:{order.Id}", "supplier_delivery",
                $"Envío de {SupplierRules.Names.GetValueOrDefault(order.Supplier, order.Supplier)} al negocio: {order.Quantity} × {name}", ct);
            await MoveItemsAsync(c, tx, txId, order.ItemCode, order.Quantity, from: (seller, $"pedido:{order.Id}"), to: (shop, location),
                notEnough: "Sin stock.", ct, actor: order.CharacterId);
            await CloseOrderAsync(c, tx, order.Id, "entregado", ct);
            return;
        }
        var buyer = await EnsureCharacterAccountAsync(c, tx, order.CharacterId, ct);
        await LockAccountsAsync(c, tx, ct, seller, buyer);
        var refund = await NewLedgerTransactionAsync(c, tx, $"supplier-undeliverable:{order.Id}", "supplier_refund_delivery",
            $"Envío imposible (negocio cerrado): {order.Quantity} × {name} queda para retirar", ct);
        if (order.Fee > 0)
            await TransferMoneyAsync(c, tx, refund, from: seller, to: buyer, order.Fee, ct);
        await using var toPickup = new NpgsqlCommand("UPDATE distrito.supplier_orders SET delivery = 'retiro', delivery_cents = 0 WHERE id = @id", c, tx);
        toPickup.Parameters.AddWithValue("id", order.Id);
        await toPickup.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// El camión del proveedor lleva los envíos que ya salieron al depósito de la cuadrilla. Si el depósito ya no existe
    /// o no entra, el pedido pasa a retiro en el mostrador y se devuelve el costo del envío. Devuelve cuántos procesó.
    /// </summary>
    public async Task<int> DeliverSupplierOrdersAsync(long currentCycle, CancellationToken ct, Guid? onlyOrder = null)
    {
        var due = new List<Guid>();
        await using (var c = await db.OpenConnectionAsync(ct))
        await using (var cmd = new NpgsqlCommand("""
            SELECT id FROM distrito.supplier_orders WHERE status = 'preparando' AND delivery IN ('envio', 'negocio') AND deliver_cycle <= @cycle
               AND (@only::uuid IS NULL OR id = @only)
            """, c))
        {
            cmd.Parameters.AddWithValue("cycle", currentCycle);
            cmd.Parameters.AddWithValue("only", (object?)onlyOrder ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) due.Add(r.GetGuid(0));
        }
        foreach (var id in due)
        {
            await using var c = await db.OpenConnectionAsync(ct);
            await using var tx = await c.BeginTransactionAsync(ct);
            var order = await LockOrderAsync(c, tx, id, ct);
            if (order is not { Status: "preparando" } || !SupplierRules.IsShipped(order.Delivery))
                continue;
            if (order.Delivery == SupplierRules.ToBusiness)
            {
                await DeliverToBusinessAsync(c, tx, order, ct);
                await tx.CommitAsync(ct);
                continue;
            }
            var seller = await AccountIdByCodeAsync(c, tx, order.Supplier, ct);
            var (name, _) = await ItemInfoAsync(c, tx, order.ItemCode, ct);
            Guid? crewId = null;
            if (order.WarehouseId is { } wid)
            {
                await using var find = new NpgsqlCommand("SELECT crew_id FROM distrito.warehouses WHERE id = @id", c, tx);
                find.Parameters.AddWithValue("id", wid);
                crewId = await find.ExecuteScalarAsync(ct) as Guid?;
            }
            var row = crewId is { } cid ? await WarehouseOfAsync(c, tx, cid, true, ct) : null;
            var delivered = false;
            if (row is not null)
            {
                var box = await CrewService.EnsureCrewAccountAsync(c, tx, crewId!.Value, ct);
                await LockAccountsAsync(c, tx, ct, seller, box);
                try
                {
                    await EnsureWarehouseFitsAsync(c, tx, box, row, order.ItemCode, order.Quantity, ct);
                    var txId = await NewLedgerTransactionAsync(c, tx, $"supplier-delivery:{order.Id}", "supplier_delivery",
                        $"Envío de {SupplierRules.Names.GetValueOrDefault(order.Supplier, order.Supplier)} al depósito: {order.Quantity} × {name}", ct);
                    await MoveItemsAsync(c, tx, txId, order.ItemCode, order.Quantity, from: (seller, $"pedido:{order.Id}"), to: (box, row.Location),
                        notEnough: "Sin stock.", ct, actor: order.CharacterId);
                    await CloseOrderAsync(c, tx, order.Id, "entregado", ct);
                    delivered = true;
                }
                catch (EconomyRejected)
                {
                    // No entra: sigue abajo como retiro.
                }
            }
            if (!delivered)
            {
                var buyer = await EnsureCharacterAccountAsync(c, tx, order.CharacterId, ct);
                await LockAccountsAsync(c, tx, ct, seller, buyer);
                var txId = await NewLedgerTransactionAsync(c, tx, $"supplier-undeliverable:{order.Id}", "supplier_refund_delivery",
                    $"Envío imposible (depósito lleno o cerrado): {order.Quantity} × {name} queda para retirar", ct);
                if (order.Fee > 0)
                    await TransferMoneyAsync(c, tx, txId, from: seller, to: buyer, order.Fee, ct);
                await using var toPickup = new NpgsqlCommand("UPDATE distrito.supplier_orders SET delivery = 'retiro', delivery_cents = 0 WHERE id = @id", c, tx);
                toPickup.Parameters.AddWithValue("id", order.Id);
                await toPickup.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        return due.Count;
    }
}
