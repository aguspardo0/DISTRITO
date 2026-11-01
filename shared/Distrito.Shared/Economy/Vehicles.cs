namespace Distrito.Shared.Economy;

/// <summary>Un tipo de vehículo (de época, sin marcas reales). Números aprobados por el equipo (2026-11-01).</summary>
public sealed record VehicleType(
    string Code, string Name, long PriceCents, int MinLevel, double CapacityKg,
    float MaxSpeed, float Acceleration, float TurnRate, float Length, float Width, float Height);

/// <summary>
/// Vehículos (inventario, etapa 6). Personales o de cuadrilla; manejan el dueño o los de su cuadrilla. La carga vive en el
/// libro central en la ubicación vehiculo:&lt;id&gt; de la cuenta dueña; queda adentro al bajarse o morir el conductor,
/// y solo se puede forzar donde la zona permite robos. El vehículo no se roba. Manejo arcade, sin combustible por ahora.
/// </summary>
public static class Vehicles
{
    public static readonly VehicleType Pickup = new("camioneta", "Camioneta de reparto", 12_000, 3, 400, 13f, 6f, 1.9f, 4.4f, 1.9f, 1.9f);
    public static readonly VehicleType Truck = new("camion", "Camión de carga", 35_000, 6, 1_500, 9f, 3.5f, 1.3f, 6.8f, 2.4f, 2.8f);

    public static readonly IReadOnlyList<VehicleType> All = [Pickup, Truck];

    public static VehicleType? Find(string code) => All.FirstOrDefault(v => v.Code == code);

    /// <summary>Distancia máxima para subir, cargar o forzar (metros desde el centro del vehículo).</summary>
    public const float UseRange = 4.5f;

    /// <summary>Segundos que tarda forzar la caja de un vehículo ajeno (hay que quedarse al lado).</summary>
    public const float ForceSeconds = 5f;

    public static string Location(Guid vehicleId) => $"vehiculo:{vehicleId}";
}

/// <summary>Un vehículo en el mundo.</summary>
public sealed record VehicleView(Guid Id, string Kind, string OwnerName, Guid? OwnerCharacterId, Guid? OwnerCrewId,
    double X, double Y, double Z, double Yaw);

/// <summary>Carga de un vehículo vista por alguien que está al lado. CanUse: dueño o de su cuadrilla.</summary>
public sealed record VehicleCargoView(Guid VehicleId, string Kind, string OwnerName, bool CanUse,
    List<InventoryEntry> Items, double CapacityKg)
{
    public double WeightKg => Items.Sum(i => i.WeightKg);
}

public sealed record VehicleBuyRequest(Guid CharacterId, string Kind, bool ForCrew, double X, double Y, double Z, double Yaw, string RequestId);
public sealed record VehicleBuyResult(EconomyActionResult Result, VehicleView? Vehicle);
public sealed record VehicleParkRequest(Guid VehicleId, double X, double Y, double Z, double Yaw);
public sealed record VehicleCargoRequest(Guid CharacterId, Guid VehicleId, string ItemCode, int Quantity, bool Load, string RequestId);
public sealed record VehicleForceRequest(Guid CharacterId, Guid VehicleId, Guid DropId, double X, double Y, double Z, int Seconds, string RequestId);
