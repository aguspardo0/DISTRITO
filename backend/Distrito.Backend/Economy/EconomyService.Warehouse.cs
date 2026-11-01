using System.Text.Json;
using Distrito.Backend.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Inventario, etapa 3: depósito de la cuadrilla. Mismo libro de objetos que todo lo demás: lo guardado es de la caja
/// de la cuadrilla en la ubicación almacen:&lt;id&gt;. Abrirlo y el alquiler salen de la caja común; el jefe administra
/// los permisos. Con alquiler adeudado se puede guardar pero no retirar hasta que la caja pague.
/// </summary>
public sealed partial class EconomyService
{
    public WarehouseRules CrewWarehouseRules()
    {
        var d = WarehouseRules.Default;
        var file = config.GetValue<string>("Inventory:File") ?? Path.Combine(AppContext.BaseDirectory, "inventario.json");
        if (!File.Exists(file))
            return d;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("depositoCuadrilla", out var w))
                return d;
            double? Num(string n, double? fallback) => w.TryGetProperty(n, out var v)
                ? v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null
                : fallback;
            return new WarehouseRules(
                w.TryGetProperty("lugar", out var place) && place.ValueKind == JsonValueKind.String ? place.GetString()! : d.Place,
                (long)(Num("apertura", d.OpenCostCents) ?? d.OpenCostCents),
                (long)(Num("alquiler", d.RentCents) ?? d.RentCents),
                Num("kg", d.CapacityKg), Num("litros", d.CapacityL));
        }
        catch (JsonException)
        {
            return d;
        }
    }

    private sealed record CrewSeat(Guid CrewId, string CrewName, string Role);

    private sealed record WarehouseRow(Guid Id, string Place, double? CapacityKg, double? CapacityL, long DebtCents, WarehousePermissions Permissions)
    {
        public string Location => $"almacen:{Id}";
    }

    private static async Task<CrewSeat?> CrewSeatAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT cr.id, cr.name, m.role FROM distrito.crew_members m JOIN distrito.crews cr ON cr.id = m.crew_id WHERE m.character_id = @c
            """, c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new CrewSeat(r.GetGuid(0), r.GetString(1), r.GetString(2)) : null;
    }

    private static async Task<WarehouseRow?> WarehouseOfAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, bool forUpdate, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, place, capacity_kg::float8, capacity_l::float8, debt_cents, member_can_view, member_can_deposit, member_can_withdraw, second_can_withdraw
              FROM distrito.warehouses WHERE crew_id = @crew {(forUpdate ? "FOR UPDATE" : "")}
            """, c, tx);
        cmd.Parameters.AddWithValue("crew", crewId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return new WarehouseRow(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetDouble(2), r.IsDBNull(3) ? null : r.GetDouble(3),
            r.GetInt64(4), new WarehousePermissions(r.GetBoolean(5), r.GetBoolean(6), r.GetBoolean(7), r.GetBoolean(8)));
    }

    private static (bool View, bool Deposit, bool Withdraw, bool Admin) Rights(string role, WarehousePermissions p) => role switch
    {
        "leader" => (true, true, true, true),
        "second" => (true, true, p.SecondCanWithdraw, false),
        _ => (p.MemberCanView, p.MemberCanDeposit && p.MemberCanView, p.MemberCanWithdraw && p.MemberCanView, false),
    };

    /// <summary>El depósito de la cuadrilla del personaje (null si no está en ninguna).</summary>
    public async Task<WarehouseView?> WarehouseAsync(Guid characterId, CancellationToken ct)
    {
        var rules = CrewWarehouseRules();
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var seat = await CrewSeatAsync(c, tx, characterId, ct);
        if (seat is null)
        {
            await tx.CommitAsync(ct);
            return null;
        }
        var row = await WarehouseOfAsync(c, tx, seat.CrewId, false, ct);
        var perms = row?.Permissions ?? new WarehousePermissions(true, true, true, true);
        var (view, deposit, withdraw, admin) = Rights(seat.Role, perms);
        var items = new List<InventoryEntry>();
        var history = new List<WarehouseMovement>();
        if (row is not null && view)
        {
            var account = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
            items = await EntriesAtAsync(c, tx, account, row.Location, ct);
            await using var h = new NpgsqlCommand("""
                SELECT m.created_at, COALESCE(ch.name, '—'), d.name, m.quantity, m.to_location = @loc
                  FROM distrito.item_movements m
                  JOIN distrito.item_definitions d ON d.code = m.item_code
                  LEFT JOIN distrito.characters ch ON ch.id = m.actor_character_id
                 WHERE m.to_location = @loc OR m.from_location = @loc
                 ORDER BY m.id DESC LIMIT 12
                """, c, tx);
            h.Parameters.AddWithValue("loc", row.Location);
            await using var r = await h.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                history.Add(new WarehouseMovement(r.GetFieldValue<DateTimeOffset>(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetBoolean(4)));
        }
        await tx.CommitAsync(ct);
        var place = row?.Place ?? rules.Place;
        return new WarehouseView(row?.Id, seat.CrewName, row is not null, place, TerritoryRules.NameOf(place), items,
            row?.CapacityKg ?? rules.CapacityKg, row?.CapacityL ?? rules.CapacityL, row?.DebtCents ?? 0, perms,
            view, row is not null && deposit, row is not null && withdraw, admin, history, rules.OpenCostCents, rules.RentCents);
    }

    /// <summary>El jefe abre el depósito pagando de la caja común.</summary>
    public Task<EconomyActionResult> OpenWarehouseAsync(WarehouseOpenRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var rules = CrewWarehouseRules();
            var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("not_in_crew", "No estás en una cuadrilla.");
            if (seat.Role != "leader")
                throw new EconomyRejected("not_leader", "Solo el jefe puede abrir el depósito.");
            if (await WarehouseOfAsync(c, tx, seat.CrewId, true, ct) is not null)
                throw new EconomyRejected("already_open", "La cuadrilla ya tiene depósito.");
            var box = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
            var landlord = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
            await LockAccountsAsync(c, tx, ct, box, landlord);
            if (await BalanceAsync(c, tx, box, ct) < rules.OpenCostCents)
                throw new EconomyRejected("insufficient_funds", $"La caja de la cuadrilla no llega: abrir el depósito cuesta {EconomyCatalog.FormatMoney(rules.OpenCostCents)}.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "warehouse_open", $"Apertura del depósito de {seat.CrewName}", ct);
            if (rules.OpenCostCents > 0)
                await TransferMoneyAsync(c, tx, txId, from: box, to: landlord, rules.OpenCostCents, ct);
            await using var create = new NpgsqlCommand("""
                INSERT INTO distrito.warehouses (id, crew_id, place, capacity_kg, capacity_l, last_rent_cycle) VALUES (@id, @crew, @place, @kg, @l, @cycle)
                """, c, tx);
            create.Parameters.AddWithValue("id", Guid.CreateVersion7());
            create.Parameters.AddWithValue("crew", seat.CrewId);
            create.Parameters.AddWithValue("place", rules.Place);
            create.Parameters.AddWithValue("kg", (object?)rules.CapacityKg ?? DBNull.Value);
            create.Parameters.AddWithValue("l", (object?)rules.CapacityL ?? DBNull.Value);
            create.Parameters.AddWithValue("cycle", CurrentCycle());
            await create.ExecuteNonQueryAsync(ct);
            return $"Depósito abierto en {TerritoryRules.NameOf(rules.Place)} por {EconomyCatalog.FormatMoney(rules.OpenCostCents)} de la caja.";
        });

    /// <summary>Guardar o retirar (el servidor de juego ya validó que esté en el lugar del depósito).</summary>
    public Task<EconomyActionResult> MoveWarehouseAsync(WarehouseMoveRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (r.Quantity <= 0 || r.Quantity > 1_000_000)
                throw new EconomyRejected("invalid_quantity", "Cantidad inválida.");
            var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("not_in_crew", "No estás en una cuadrilla.");
            var row = await WarehouseOfAsync(c, tx, seat.CrewId, true, ct) ?? throw new EconomyRejected("no_warehouse", "Tu cuadrilla no tiene depósito.");
            var (_, canDeposit, canWithdraw, _) = Rights(seat.Role, row.Permissions);
            if (r.Deposit && !canDeposit)
                throw new EconomyRejected("forbidden", "Tu jefe no te dejó guardar en el depósito.");
            if (!r.Deposit && !canWithdraw)
                throw new EconomyRejected("forbidden", "Tu jefe no te dejó retirar del depósito.");
            if (!r.Deposit && row.DebtCents > 0)
                throw new EconomyRejected("rent_due",
                    $"Valenti retiene el depósito: se deben {EconomyCatalog.FormatMoney(row.DebtCents)} de alquiler. Pongan plata en la caja y se cobra en el próximo ciclo.");
            var (name, kind) = await ItemInfoAsync(c, tx, r.ItemCode, ct);
            if (!ItemKinds.Of(kind).Tradable)
                throw new EconomyRejected("not_tradable", $"{name} no se puede guardar.");
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var box = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
            await LockAccountsAsync(c, tx, ct, me, box);
            if (r.Deposit)
                await EnsureWarehouseFitsAsync(c, tx, box, row, r.ItemCode, r.Quantity, ct);
            else
                await EnsureFitsAsync(c, tx, me, r.ItemCode, r.Quantity, ct);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, r.Deposit ? "warehouse_deposit" : "warehouse_withdraw",
                $"{(r.Deposit ? "Guardó en" : "Retiró de")} el depósito de {seat.CrewName}: {r.Quantity} × {name}", ct);
            var mine = (me, EconomyCatalog.CarriedLocation);
            var stored = (box, row.Location);
            await MoveItemsAsync(c, tx, txId, r.ItemCode, r.Quantity, from: r.Deposit ? mine : stored, to: r.Deposit ? stored : mine,
                notEnough: r.Deposit ? $"No tenés {r.Quantity} × {name}." : $"En el depósito no hay {r.Quantity} × {name}.", ct, actor: r.CharacterId);
            return r.Deposit ? $"Guardaste {r.Quantity} × {name} en el depósito." : $"Retiraste {r.Quantity} × {name} del depósito.";
        });

    private static async Task EnsureWarehouseFitsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid box, WarehouseRow row, string itemCode, int quantity, CancellationToken ct)
    {
        if (row.CapacityKg is null && row.CapacityL is null)
            return;
        var current = await EntriesAtAsync(c, tx, box, row.Location, ct);
        await using var cmd = new NpgsqlCommand("SELECT weight_kg::float8, volume_l::float8 FROM distrito.item_definitions WHERE code = @i", c, tx);
        cmd.Parameters.AddWithValue("i", itemCode);
        double unitKg = 0, unitL = 0;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            if (await r.ReadAsync(ct)) { unitKg = r.GetDouble(0); unitL = r.GetDouble(1); }
        var kg = current.Sum(i => i.WeightKg);
        var l = current.Sum(i => i.VolumeL);
        if (row.CapacityKg is { } maxKg && kg + unitKg * quantity > maxKg + 1e-9)
            throw new EconomyRejected("warehouse_full", $"No entra: el depósito tiene {kg:0.#} kg de {maxKg:0.#}.");
        if (row.CapacityL is { } maxL && l + unitL * quantity > maxL + 1e-9)
            throw new EconomyRejected("warehouse_full", $"No entra: el depósito tiene {l:0.#} L de {maxL:0.#}.");
    }

    /// <summary>El jefe cambia quién puede ver, guardar y retirar.</summary>
    public Task<EconomyActionResult> SetWarehousePermissionsAsync(WarehousePermissionsRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("not_in_crew", "No estás en una cuadrilla.");
            if (seat.Role != "leader")
                throw new EconomyRejected("not_leader", "Solo el jefe administra el depósito.");
            var row = await WarehouseOfAsync(c, tx, seat.CrewId, true, ct) ?? throw new EconomyRejected("no_warehouse", "Tu cuadrilla no tiene depósito.");
            await NewLedgerTransactionAsync(c, tx, r.RequestId, "warehouse_permissions", $"Permisos del depósito de {seat.CrewName}", ct);
            await using var cmd = new NpgsqlCommand("""
                UPDATE distrito.warehouses SET member_can_view = @v, member_can_deposit = @d, member_can_withdraw = @w, second_can_withdraw = @s WHERE id = @id
                """, c, tx);
            cmd.Parameters.AddWithValue("v", r.Permissions.MemberCanView);
            cmd.Parameters.AddWithValue("d", r.Permissions.MemberCanDeposit);
            cmd.Parameters.AddWithValue("w", r.Permissions.MemberCanWithdraw);
            cmd.Parameters.AddWithValue("s", r.Permissions.SecondCanWithdraw);
            cmd.Parameters.AddWithValue("id", row.Id);
            await cmd.ExecuteNonQueryAsync(ct);
            return "Permisos del depósito actualizados.";
        });

    /// <summary>
    /// Alquiler de los depósitos: por cada ciclo pasado con algo guardado se cobra el alquiler de la caja común.
    /// Lo que la caja no puede pagar queda como deuda (sin deuda no se puede retirar). Devuelve cuántos depósitos procesó.
    /// </summary>
    public async Task<int> RunWarehouseRentAsync(long currentCycle, CancellationToken ct, Guid? onlyWarehouse = null)
    {
        var rent = CrewWarehouseRules().RentCents;
        var pending = new List<Guid>();
        await using (var c = await db.OpenConnectionAsync(ct))
        await using (var cmd = new NpgsqlCommand("SELECT id FROM distrito.warehouses WHERE last_rent_cycle < @cycle AND (@only::uuid IS NULL OR id = @only)", c))
        {
            cmd.Parameters.AddWithValue("cycle", currentCycle);
            cmd.Parameters.AddWithValue("only", (object?)onlyWarehouse ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) pending.Add(r.GetGuid(0));
        }
        foreach (var id in pending)
        {
            await using var c = await db.OpenConnectionAsync(ct);
            await using var tx = await c.BeginTransactionAsync(ct);
            Guid crewId;
            long last, debt;
            await using (var lockRow = new NpgsqlCommand("SELECT crew_id, last_rent_cycle, debt_cents FROM distrito.warehouses WHERE id = @id FOR UPDATE", c, tx))
            {
                lockRow.Parameters.AddWithValue("id", id);
                await using var r = await lockRow.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) continue;
                crewId = r.GetGuid(0); last = r.GetInt64(1); debt = r.GetInt64(2);
            }
            if (last >= currentCycle)
                continue;
            var box = await CrewService.EnsureCrewAccountAsync(c, tx, crewId, ct);
            var landlord = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
            await LockAccountsAsync(c, tx, ct, box, landlord);
            await using (var stock = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM distrito.item_stacks WHERE owner_account_id = @a AND location = @l AND quantity > 0)", c, tx))
            {
                stock.Parameters.AddWithValue("a", box);
                stock.Parameters.AddWithValue("l", $"almacen:{id}");
                // Sin nada guardado no se cobra (como el puesto sin mercadería). No se acumulan más ciclos que el tope de recuperación.
                if ((bool)(await stock.ExecuteScalarAsync(ct))!)
                    debt += rent * Math.Min(currentCycle - last, EconomyCatalog.MaxCatchUpCycles);
            }
            var paid = Math.Min(debt, await BalanceAsync(c, tx, box, ct));
            if (paid > 0)
            {
                var txId = await NewLedgerTransactionAsync(c, tx, $"warehouse-rent:{id}:{currentCycle}", "warehouse_rent", "Alquiler del depósito de la cuadrilla", ct);
                await TransferMoneyAsync(c, tx, txId, from: box, to: landlord, paid, ct);
            }
            await using (var save = new NpgsqlCommand("UPDATE distrito.warehouses SET last_rent_cycle = @cycle, debt_cents = @debt WHERE id = @id", c, tx))
            {
                save.Parameters.AddWithValue("cycle", currentCycle);
                save.Parameters.AddWithValue("debt", debt - paid);
                save.Parameters.AddWithValue("id", id);
                await save.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        return pending.Count;
    }

    /// <summary>
    /// Si la cuadrilla se disuelve, lo guardado no se pierde: pasa a manos del último que se va.
    /// Lo llama CrewService dentro de la misma transacción, antes de borrar la cuadrilla.
    /// </summary>
    internal static async Task EmptyWarehouseOnDisbandAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid lastMember, CancellationToken ct)
    {
        var row = await WarehouseOfAsync(c, tx, crewId, true, ct);
        if (row is null)
            return;
        var box = await CrewService.EnsureCrewAccountAsync(c, tx, crewId, ct);
        var me = await EnsureCharacterAccountAsync(c, tx, lastMember, ct);
        await LockAccountsAsync(c, tx, ct, me, box);
        var items = await EntriesAtAsync(c, tx, box, row.Location, ct);
        if (items.Count == 0)
            return;
        var txId = await NewLedgerTransactionAsync(c, tx, $"warehouse-disband:{row.Id}", "warehouse_disband", "Cuadrilla disuelta: lo del depósito pasa al último miembro", ct);
        foreach (var item in items)
            await MoveItemsAsync(c, tx, txId, item.ItemCode, item.Quantity, from: (box, row.Location), to: (me, EconomyCatalog.CarriedLocation),
                notEnough: "Sin stock.", ct, actor: lastMember);
    }
}
