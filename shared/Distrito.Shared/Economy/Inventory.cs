namespace Distrito.Shared.Economy;

/// <summary>
/// Inventario, etapa 1 (documento «Sistema de Inventario, Objetos y Logística»).
/// Todo objeto vive en el libro central (item_stacks): dueño + ubicación + cantidad. El inventario personal es la
/// ubicación «encima» del personaje; negocios, almacenes, vehículos y bultos en la calle son otras ubicaciones del
/// mismo sistema. Acá solo hay la definición de clases de objeto y lo que ve el jugador.
/// </summary>
public static class ItemKinds
{
    public const string Weapon = "arma";
    public const string Ammo = "municion";
    public const string Tool = "herramienta";
    public const string Goods = "mercancia";
    public const string RawMaterial = "materia-prima";
    public const string Product = "producto";
    public const string Document = "documento";
    public const string Equipment = "equipamiento";
    public const string Quest = "mision";

    /// <summary>Comportamientos por clase. Un tipo nuevo de objeto solo necesita su fila en item_definitions.</summary>
    public sealed record Behavior(string Kind, string Name, bool Equippable, bool Consumable, bool HasDurability, bool Tradable);

    public static readonly IReadOnlyList<Behavior> All =
    [
        new(Weapon, "Armas", Equippable: true, Consumable: false, HasDurability: false, Tradable: true),
        new(Ammo, "Munición", Equippable: false, Consumable: true, HasDurability: false, Tradable: true),
        new(Tool, "Herramientas", Equippable: true, Consumable: false, HasDurability: true, Tradable: true),
        new(Goods, "Mercancías", Equippable: false, Consumable: false, HasDurability: false, Tradable: true),
        new(RawMaterial, "Materias primas", Equippable: false, Consumable: false, HasDurability: false, Tradable: true),
        new(Product, "Productos", Equippable: false, Consumable: false, HasDurability: false, Tradable: true),
        new(Document, "Documentos", Equippable: false, Consumable: false, HasDurability: false, Tradable: true),
        new(Equipment, "Equipamiento", Equippable: true, Consumable: false, HasDurability: true, Tradable: true),
        new(Quest, "Objetos de misión", Equippable: false, Consumable: false, HasDurability: false, Tradable: false),
    ];

    public static Behavior Of(string kind) => All.FirstOrDefault(b => b.Kind == kind) ?? All.First(b => b.Kind == Goods);
}

/// <summary>Un tipo de objeto en el inventario, con sus datos para mostrar.</summary>
public sealed record InventoryEntry(
    string ItemCode, string Name, string Description, string Kind, string Category, int Quantity,
    double UnitWeightKg, double UnitVolumeL, string LegalStatus, long ReferencePriceCents)
{
    public double WeightKg => UnitWeightKg * Quantity;
    public double VolumeL => UnitVolumeL * Quantity;
}

/// <summary>Contenido de un contenedor (por ahora: lo que el personaje lleva encima). Capacidad null = sin límite.</summary>
public sealed record InventoryView(List<InventoryEntry> Items, double? CapacityKg, double? CapacityL)
{
    public double WeightKg => Items.Sum(i => i.WeightKg);
    public double VolumeL => Items.Sum(i => i.VolumeL);
}

/// <summary>Capacidad de cada tipo de contenedor (config/inventario.json). Null = sin límite (decisión del equipo: por ahora sin límite).</summary>
public sealed record InventoryLimits(double? PersonalKg, double? PersonalL)
{
    public static InventoryLimits Unlimited { get; } = new(null, null);
}

public sealed record GiveItemRequest(Guid FromCharacterId, Guid ToCharacterId, string ItemCode, int Quantity, string RequestId);

public sealed record DropItemRequest(Guid CharacterId, string ItemCode, int Quantity, Guid DropId, string RequestId,
    double X, double Y, double Z, int Seconds = 300);

/// <summary>Resultado de soltar algo: el estado del personaje y, si quedó en la calle, el bulto para mostrarlo.</summary>
public sealed record DropItemResult(EconomyActionResult Result, DropView? Drop);

/// <summary>Reglas del depósito de cuadrilla (config/inventario.json → "depositoCuadrilla").</summary>
public sealed record WarehouseRules(string Place, long OpenCostCents, long RentCents, double? CapacityKg, double? CapacityL)
{
    /// <summary>Números aprobados por el equipo (2026-11-01).</summary>
    public static WarehouseRules Default { get; } = new(TerritoryRules.Office, 2_000, 100, 300, null);
}

/// <summary>Permisos del depósito: el jefe siempre ve, deposita, retira y administra; el resto según esto.</summary>
public sealed record WarehousePermissions(bool MemberCanView, bool MemberCanDeposit, bool MemberCanWithdraw, bool SecondCanWithdraw);

/// <summary>Un movimiento del historial del depósito.</summary>
public sealed record WarehouseMovement(DateTimeOffset At, string Who, string ItemName, int Quantity, bool Deposit);

/// <summary>
/// El depósito de la cuadrilla visto por un miembro. Exists=false: la cuadrilla todavía no lo abrió.
/// Items e History solo vienen si el que pregunta puede ver.
/// </summary>
public sealed record WarehouseView(
    Guid? Id, string CrewName, bool Exists, string Place, string PlaceName,
    List<InventoryEntry> Items, double? CapacityKg, double? CapacityL, long DebtCents,
    WarehousePermissions Permissions, bool CanView, bool CanDeposit, bool CanWithdraw, bool CanAdmin,
    List<WarehouseMovement> History, long OpenCostCents, long RentCents)
{
    public double WeightKg => Items.Sum(i => i.WeightKg);
    public double VolumeL => Items.Sum(i => i.VolumeL);
}

public sealed record WarehouseOpenRequest(Guid CharacterId, string RequestId);
public sealed record WarehouseMoveRequest(Guid CharacterId, string ItemCode, int Quantity, bool Deposit, string RequestId);
public sealed record WarehousePermissionsRequest(Guid CharacterId, WarehousePermissions Permissions, string RequestId);
