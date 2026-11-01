using Distrito.Backend.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Inventario, etapa 6: vehículos. Comprar (propio o de la cuadrilla), dónde quedó estacionado, cargar y descargar, y
/// forzar la caja de uno ajeno (el servidor de juego valida distancia, tiempo y que la zona permita robos).
/// </summary>
public sealed partial class EconomyService
{
    private sealed record VehicleRow(Guid Id, string Kind, Guid? OwnerCharacter, Guid? OwnerCrew, Guid Account, string OwnerName);

    private static async Task<VehicleRow?> VehicleAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid id, bool forUpdate, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT v.id, v.kind, v.owner_character_id, v.owner_crew_id, v.account_id, COALESCE(cr.name, ch.name, '—')
              FROM distrito.vehicles v
              LEFT JOIN distrito.characters ch ON ch.id = v.owner_character_id
              LEFT JOIN distrito.crews cr ON cr.id = v.owner_crew_id
             WHERE v.id = @id {(forUpdate ? "FOR UPDATE OF v" : "")}
            """, c, tx);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new VehicleRow(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetGuid(2), r.IsDBNull(3) ? null : r.GetGuid(3), r.GetGuid(4), r.GetString(5))
            : null;
    }

    /// <summary>¿Puede manejarlo y usar la carga? El dueño, o cualquiera de la cuadrilla dueña.</summary>
    private static async Task<bool> CanUseVehicleAsync(NpgsqlConnection c, NpgsqlTransaction tx, VehicleRow v, Guid characterId, CancellationToken ct)
    {
        if (v.OwnerCharacter == characterId)
            return true;
        var seat = await CrewSeatAsync(c, tx, characterId, ct);
        if (seat is null)
            return false;
        if (v.OwnerCrew == seat.CrewId)
            return true;
        // Vehículo personal de alguien de la misma cuadrilla: también lo pueden usar.
        return v.OwnerCharacter is { } owner && (await CrewSeatAsync(c, tx, owner, ct))?.CrewId == seat.CrewId;
    }

    public async Task<List<VehicleView>> VehiclesAsync(CancellationToken ct)
    {
        var list = new List<VehicleView>();
        await using var c = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT v.id, v.kind, COALESCE(cr.name, ch.name, '—'), v.owner_character_id, v.owner_crew_id, v.x, v.y, v.z, v.yaw
              FROM distrito.vehicles v
              LEFT JOIN distrito.characters ch ON ch.id = v.owner_character_id
              LEFT JOIN distrito.crews cr ON cr.id = v.owner_crew_id
             ORDER BY v.created_at
            """, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new VehicleView(r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetGuid(3), r.IsDBNull(4) ? null : r.GetGuid(4),
                r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetDouble(8)));
        return list;
    }

    public async Task<VehicleBuyResult> BuyVehicleAsync(VehicleBuyRequest r, CancellationToken ct)
    {
        VehicleView? bought = null;
        var result = await RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var type = Vehicles.Find(r.Kind) ?? throw new EconomyRejected("unknown_vehicle", "Ese vehículo no se vende.");
            if ((await LoadProgressAsync(c, tx, r.CharacterId, ct)).Level < type.MinLevel)
                throw new EconomyRejected("level_too_low", $"La {type.Name.ToLowerInvariant()} pide nivel {type.MinLevel}.");
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var seller = await AccountIdByCodeAsync(c, tx, EconomyCatalog.WorkshopNpc, ct);
            Guid payer = me, owner = me;
            Guid? crewId = null;
            string ownerName;
            if (r.ForCrew)
            {
                var seat = await CrewSeatAsync(c, tx, r.CharacterId, ct) ?? throw new EconomyRejected("not_in_crew", "No estás en una cuadrilla.");
                if (seat.Role != "leader")
                    throw new EconomyRejected("not_leader", "Solo el jefe compra vehículos con la caja.");
                payer = owner = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
                crewId = seat.CrewId;
                ownerName = seat.CrewName;
            }
            else
            {
                await using var name = new NpgsqlCommand("SELECT name FROM distrito.characters WHERE id = @c", c, tx);
                name.Parameters.AddWithValue("c", r.CharacterId);
                ownerName = (string)(await name.ExecuteScalarAsync(ct))!;
            }
            await LockAccountsAsync(c, tx, ct, payer, seller);
            if (await BalanceAsync(c, tx, payer, ct) < type.PriceCents)
                throw new EconomyRejected("insufficient_funds", r.ForCrew
                    ? $"La caja no llega: la {type.Name.ToLowerInvariant()} cuesta {EconomyCatalog.FormatMoney(type.PriceCents)}."
                    : $"No te alcanza: la {type.Name.ToLowerInvariant()} cuesta {EconomyCatalog.FormatMoney(type.PriceCents)}.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "vehicle_purchase", $"Compra de {type.Name} ({ownerName})", ct);
            await TransferMoneyAsync(c, tx, txId, from: payer, to: seller, type.PriceCents, ct);
            var id = Guid.CreateVersion7();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO distrito.vehicles (id, kind, owner_character_id, owner_crew_id, account_id, x, y, z, yaw)
                VALUES (@id, @k, @oc, @cr, @acc, @x, @y, @z, @yaw)
                """, c, tx))
            {
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("k", type.Code);
                insert.Parameters.AddWithValue("oc", r.ForCrew ? DBNull.Value : (object)r.CharacterId);
                insert.Parameters.AddWithValue("cr", (object?)crewId ?? DBNull.Value);
                insert.Parameters.AddWithValue("acc", owner);
                insert.Parameters.AddWithValue("x", r.X);
                insert.Parameters.AddWithValue("y", r.Y);
                insert.Parameters.AddWithValue("z", r.Z);
                insert.Parameters.AddWithValue("yaw", r.Yaw);
                await insert.ExecuteNonQueryAsync(ct);
            }
            bought = new VehicleView(id, type.Code, ownerName, r.ForCrew ? null : r.CharacterId, crewId, r.X, r.Y, r.Z, r.Yaw);
            return $"¡{type.Name} {(r.ForCrew ? $"de «{ownerName}»" : "propia")}! Está estacionada en la puerta del taller. Subí con G.";
        });
        return new VehicleBuyResult(result, result.Ok && result.Message != "Ya estaba hecho." ? bought : null);
    }

    public async Task ParkVehicleAsync(VehicleParkRequest r, CancellationToken ct)
    {
        if (!double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Z) || !double.IsFinite(r.Yaw))
            return;
        await using var c = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE distrito.vehicles SET x = @x, y = @y, z = @z, yaw = @yaw WHERE id = @id", c);
        cmd.Parameters.AddWithValue("x", r.X);
        cmd.Parameters.AddWithValue("y", r.Y);
        cmd.Parameters.AddWithValue("z", r.Z);
        cmd.Parameters.AddWithValue("yaw", r.Yaw);
        cmd.Parameters.AddWithValue("id", r.VehicleId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<VehicleCargoView?> VehicleCargoAsync(Guid vehicleId, Guid characterId, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var v = await VehicleAsync(c, tx, vehicleId, false, ct);
        if (v is null)
            return null;
        var canUse = await CanUseVehicleAsync(c, tx, v, characterId, ct);
        var items = canUse ? await EntriesAtAsync(c, tx, v.Account, Vehicles.Location(v.Id), ct) : [];
        await tx.CommitAsync(ct);
        return new VehicleCargoView(v.Id, v.Kind, v.OwnerName, canUse, items, Vehicles.Find(v.Kind)!.CapacityKg);
    }

    /// <summary>Cargar (de encima al vehículo) o descargar. Respeta la capacidad del vehículo.</summary>
    public Task<EconomyActionResult> MoveVehicleCargoAsync(VehicleCargoRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (r.Quantity <= 0 || r.Quantity > 1_000_000)
                throw new EconomyRejected("invalid_quantity", "Cantidad inválida.");
            var v = await VehicleAsync(c, tx, r.VehicleId, true, ct) ?? throw new EconomyRejected("not_found", "Ese vehículo no existe.");
            if (!await CanUseVehicleAsync(c, tx, v, r.CharacterId, ct))
                throw new EconomyRejected("forbidden", "Ese vehículo no es tuyo ni de tu cuadrilla.");
            var (name, kind) = await ItemInfoAsync(c, tx, r.ItemCode, ct);
            if (!ItemKinds.Of(kind).Tradable)
                throw new EconomyRejected("not_tradable", $"{name} no se puede cargar.");
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            await LockAccountsAsync(c, tx, ct, me, v.Account);
            var location = Vehicles.Location(v.Id);
            if (r.Load)
            {
                var type = Vehicles.Find(v.Kind)!;
                var current = (await EntriesAtAsync(c, tx, v.Account, location, ct)).Sum(i => i.WeightKg);
                await using var unit = new NpgsqlCommand("SELECT weight_kg::float8 FROM distrito.item_definitions WHERE code = @i", c, tx);
                unit.Parameters.AddWithValue("i", r.ItemCode);
                var add = (double)(await unit.ExecuteScalarAsync(ct))! * r.Quantity;
                if (current + add > type.CapacityKg + 1e-9)
                    throw new EconomyRejected("vehicle_full", $"No entra: la {type.Name.ToLowerInvariant()} lleva {current:0.#} kg de {type.CapacityKg:0}.");
            }
            else
                await EnsureFitsAsync(c, tx, me, r.ItemCode, r.Quantity, ct);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, r.Load ? "vehicle_load" : "vehicle_unload",
                $"{(r.Load ? "Cargó en" : "Descargó de")} {(Vehicles.Find(v.Kind)!.Name.ToLowerInvariant())} de {v.OwnerName}: {r.Quantity} × {name}", ct);
            var mine = (me, EconomyCatalog.CarriedLocation);
            var cargo = (v.Account, location);
            await MoveItemsAsync(c, tx, txId, r.ItemCode, r.Quantity, from: r.Load ? mine : cargo, to: r.Load ? cargo : mine,
                notEnough: r.Load ? $"No tenés {r.Quantity} × {name}." : $"En el vehículo no hay {r.Quantity} × {name}.", ct, actor: r.CharacterId);
            return r.Load ? $"Cargaste {r.Quantity} × {name}." : $"Descargaste {r.Quantity} × {name}.";
        });

    /// <summary>Forzar la caja de un vehículo ajeno: toda la carga cae a la calle en un bulto. El vehículo queda.</summary>
    public async Task<DropItemResult> ForceVehicleAsync(VehicleForceRequest r, CancellationToken ct)
    {
        DropView? drop = null;
        var result = await RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var v = await VehicleAsync(c, tx, r.VehicleId, true, ct) ?? throw new EconomyRejected("not_found", "Ese vehículo no existe.");
            if (await CanUseVehicleAsync(c, tx, v, r.CharacterId, ct))
                throw new EconomyRejected("own_vehicle", "Es tuyo o de tu cuadrilla: descargalo desde el inventario.");
            var ground = await AccountIdByCodeAsync(c, tx, GroundAccount, ct);
            await LockAccountsAsync(c, tx, ct, v.Account, ground);
            var location = Vehicles.Location(v.Id);
            var items = await EntriesAtAsync(c, tx, v.Account, location, ct);
            if (items.Count == 0)
                throw new EconomyRejected("empty", "La caja estaba vacía.");
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "vehicle_forced", $"Forzaron la caja del vehículo de {v.OwnerName}", ct);
            foreach (var item in items)
                await MoveItemsAsync(c, tx, txId, item.ItemCode, item.Quantity, from: (v.Account, location), to: (ground, $"bulto:{r.DropId}"),
                    notEnough: "Sin carga.", ct, actor: r.CharacterId);
            var expires = await PlaceDropAsync(c, tx, r.DropId, r.CharacterId, r.X, r.Y, r.Z, r.Seconds, "soltado", ct);
            drop = new DropView(r.DropId, items.Select(i => new InventoryItem(i.ItemCode, i.Name, i.Quantity)).ToList(), r.X, r.Y, r.Z, expires);
            return $"Forzaste la caja: {string.Join(", ", items.Select(i => $"{i.Quantity} × {i.Name}"))} quedó en la calle.";
        });
        return new DropItemResult(result, result.Ok && result.Message != "Ya estaba hecho." ? drop : null);
    }

    /// <summary>Si la cuadrilla se disuelve, sus vehículos (con la carga) pasan al último miembro.</summary>
    internal static async Task HandOverCrewVehiclesAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid lastMember, CancellationToken ct)
    {
        var box = await CrewService.EnsureCrewAccountAsync(c, tx, crewId, ct);
        var me = await EnsureCharacterAccountAsync(c, tx, lastMember, ct);
        var ids = new List<Guid>();
        await using (var cmd = new NpgsqlCommand("SELECT id FROM distrito.vehicles WHERE owner_crew_id = @crew FOR UPDATE", c, tx))
        {
            cmd.Parameters.AddWithValue("crew", crewId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetGuid(0));
        }
        if (ids.Count == 0)
            return;
        await LockAccountsAsync(c, tx, ct, me, box);
        var txId = await NewLedgerTransactionAsync(c, tx, $"vehicles-disband:{crewId}", "vehicle_disband", "Cuadrilla disuelta: los vehículos pasan al último miembro", ct);
        foreach (var id in ids)
        {
            foreach (var item in await EntriesAtAsync(c, tx, box, Vehicles.Location(id), ct))
                await MoveItemsAsync(c, tx, txId, item.ItemCode, item.Quantity, from: (box, Vehicles.Location(id)), to: (me, Vehicles.Location(id)),
                    notEnough: "Sin carga.", ct, actor: lastMember);
            await using var update = new NpgsqlCommand("UPDATE distrito.vehicles SET owner_crew_id = NULL, owner_character_id = @c, account_id = @a WHERE id = @id", c, tx);
            update.Parameters.AddWithValue("c", lastMember);
            update.Parameters.AddWithValue("a", me);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(ct);
        }
    }
}
