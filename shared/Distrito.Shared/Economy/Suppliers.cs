namespace Distrito.Shared.Economy;

/// <summary>
/// Pedidos a proveedores (inventario, etapa 5). Números aprobados por el equipo (2026-11-01).
/// El pedido se paga al hacerlo, sale del stock real del proveedor y queda apartado hasta que se retira o llega al depósito.
/// </summary>
public static class SupplierRules
{
    public const int MinUnits = 5;
    public const int UnitsPerPrepCycle = 10;      // 1 ciclo de preparación cada 10 unidades
    public const int SupplierUnitsPerCycle = 50;  // lo que el proveedor prepara por ciclo entre todos los pedidos
    public const int VolumeDiscountFrom = 20;
    public const int VolumeDiscountPercent = 10;
    public const int DeliveryPercent = 10;
    public const long DeliveryMinCents = 100;
    public const int DeliveryExtraCycles = 1;
    public const int MaxUnits = 500;

    public const string Pickup = "retiro";
    public const string Delivery = "envio";

    /// <summary>Proveedores que toman pedidos y qué se les puede pedir.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Catalog = new Dictionary<string, string[]>
    {
        [EconomyCatalog.DepositNpc] = [EconomyCatalog.SparePartsItem, EconomyCatalog.CoffeeBagItem, EconomyCatalog.DrinksCrateItem],
    };

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [EconomyCatalog.DepositNpc] = "Almacén Moretti",
    };

    public static long GoodsCents(long unitPriceCents, int units) =>
        units >= VolumeDiscountFrom ? unitPriceCents * units * (100 - VolumeDiscountPercent) / 100 : unitPriceCents * units;

    public static long DeliveryCents(long goodsCents) => Math.Max(DeliveryMinCents, goodsCents * DeliveryPercent / 100);

    public static int OwnPrepCycles(int units) => Math.Max(1, (units + UnitsPerPrepCycle - 1) / UnitsPerPrepCycle);

    /// <summary>Ciclos hasta que esté listo, con <paramref name="unitsAhead"/> unidades de otros pedidos antes en la cola.</summary>
    public static int PrepCycles(int units, int unitsAhead) =>
        Math.Max(OwnPrepCycles(units), (unitsAhead + units + SupplierUnitsPerCycle - 1) / SupplierUnitsPerCycle);
}

public sealed record SupplierItem(string ItemCode, string Name, string Description, long UnitPriceCents, double UnitWeightKg);

/// <summary>Un pedido. Status: preparando (Ready=true si ya se puede retirar), entregado, cancelado.</summary>
public sealed record SupplierOrderView(
    Guid Id, string ItemCode, string ItemName, int Quantity, long GoodsCents, long DeliveryCents, string Delivery,
    string Status, bool Ready, DateTimeOffset ReadyAt, DateTimeOffset DeliverAt);

public sealed record SupplierView(
    string SupplierCode, string SupplierName, List<SupplierItem> Items, List<SupplierOrderView> Orders,
    bool HasWarehouse, int UnitsInQueue);

public sealed record SupplierOrderRequest(Guid CharacterId, string SupplierCode, string ItemCode, int Quantity, string Delivery, string RequestId);
public sealed record SupplierOrderActionRequest(Guid CharacterId, Guid OrderId, string RequestId);
