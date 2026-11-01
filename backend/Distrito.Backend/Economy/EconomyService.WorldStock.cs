using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>Inventario, etapa 7: existencias reales para mostrar en el mundo (depósitos, proveedores, vehículos).</summary>
public sealed partial class EconomyService
{
    public async Task<WorldStockView> WorldStockAsync(CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        static async Task<List<StockPile>> PilesAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid account, string location, CancellationToken ct) =>
            (await EntriesAtAsync(c, tx, account, location, ct)).Select(e => new StockPile(e.ItemCode, e.Kind, e.Quantity, e.WeightKg)).ToList();

        var warehouses = new List<WarehouseStock>();
        var rows = new List<(Guid Id, Guid Crew, string Name, double? Kg)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT w.id, w.crew_id, cr.name, w.capacity_kg::float8 FROM distrito.warehouses w JOIN distrito.crews cr ON cr.id = w.crew_id
             ORDER BY w.created_at LIMIT @bays
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("bays", StockVisualRules.WarehouseBays);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) rows.Add((r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetDouble(3)));
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var box = await Crews.CrewService.EnsureCrewAccountAsync(c, tx, rows[i].Crew, ct);
            warehouses.Add(new WarehouseStock(rows[i].Id, rows[i].Name, i, await PilesAsync(c, tx, box, $"almacen:{rows[i].Id}", ct), rows[i].Kg));
        }

        var suppliers = new List<SupplierStock>();
        var current = CurrentCycle();
        foreach (var code in SupplierRules.Catalog.Keys)
        {
            var account = await AccountIdByCodeAsync(c, tx, code, ct);
            await using var orders = new NpgsqlCommand("""
                SELECT count(*) FILTER (WHERE ready_cycle <= @cycle AND delivery = 'retiro')::int, count(*) FILTER (WHERE ready_cycle > @cycle)::int
                  FROM distrito.supplier_orders WHERE supplier_code = @s AND status = 'preparando'
                """, c, tx);
            orders.Parameters.AddWithValue("cycle", current);
            orders.Parameters.AddWithValue("s", code);
            int ready = 0, preparing = 0;
            await using (var r = await orders.ExecuteReaderAsync(ct))
                if (await r.ReadAsync(ct)) { ready = r.GetInt32(0); preparing = r.GetInt32(1); }
            suppliers.Add(new SupplierStock(code, await PilesAsync(c, tx, account, StockLocation(code), ct), ready, preparing));
        }

        var vehicles = new List<VehicleStock>();
        var vrows = new List<(Guid Id, Guid Account)>();
        await using (var cmd = new NpgsqlCommand("SELECT id, account_id FROM distrito.vehicles", c, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) vrows.Add((r.GetGuid(0), r.GetGuid(1)));
        foreach (var (id, account) in vrows)
            vehicles.Add(new VehicleStock(id, await PilesAsync(c, tx, account, Vehicles.Location(id), ct)));

        await tx.CommitAsync(ct);
        return new WorldStockView(warehouses, suppliers, vehicles);
    }
}
