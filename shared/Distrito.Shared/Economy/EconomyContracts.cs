using System.Globalization;

namespace Distrito.Shared.Economy;

// ---------- Estado económico de un personaje (lo que ve el jugador) ----------

public sealed record InventoryItem(string ItemCode, string Name, int Quantity);

public sealed record ContractView(Guid Id, string TemplateCode, string Status);

public sealed record ReputationView(string FamilyCode, string FamilyName, int Value);

/// <summary>
/// Estado de un negocio. No confundir: facturación (lo vendido), costos, ganancia (facturación − costos),
/// caja/liquidez (dinero disponible en la cuenta del negocio) y patrimonio (caja + mercadería en stock).
/// </summary>
public sealed record BusinessView(
    Guid Id,
    string Name,
    int Stock,
    long CashCents,
    long RevenueCents,
    long RentCents,
    int UnitsSold,
    int LastCycleUnits,
    bool HasHelper = false,
    long WagesCents = 0)
{
    public long CostOfGoodsCents => UnitsSold * EconomyCatalog.CoffeeBagPriceCents;
    public long ProfitCents => RevenueCents - RentCents - WagesCents - CostOfGoodsCents;
    public long StockValueCents => Stock * EconomyCatalog.CoffeeBagPriceCents;
    public long EquityCents => CashCents + StockValueCents;

    /// <summary>Estado operativo: sin mercadería el puesto no abre ni paga alquiler.</summary>
    public string OperatingState => Stock > 0 ? "Abierto, vendiendo" : "Cerrado: sin mercadería";
}

public sealed record EconomySnapshot(
    long BalanceCents,
    List<InventoryItem> Inventory,
    List<ContractView> ActiveContracts,
    int CompletedContracts,
    List<ReputationView>? Reputation = null,
    BusinessView? Business = null)
{
    public int ReputationWith(string familyCode) => Reputation?.FirstOrDefault(r => r.FamilyCode == familyCode)?.Value ?? 0;

    public int QuantityOf(string itemCode) => Inventory.Where(i => i.ItemCode == itemCode).Sum(i => i.Quantity);

    public ContractView? ActiveContract(string templateCode) =>
        ActiveContracts.FirstOrDefault(c => c.TemplateCode == templateCode);
}

/// <summary>Resultado de una acción económica: siempre trae el estado actualizado.</summary>
public sealed record EconomyActionResult(bool Ok, string? ErrorCode, string Message, EconomySnapshot Snapshot);

// ---------- Pedidos del servidor de juego al backend (API interna) ----------
// RequestId: identificador único de la acción. Si se reintenta con el mismo id, no se repite.

public sealed record AcceptContractRequest(Guid CharacterId, string TemplateCode, string RequestId);

public sealed record BuyItemRequest(Guid CharacterId, string NpcCode, string ItemCode, int Quantity, string RequestId);

public sealed record DeliverContractRequest(Guid CharacterId, Guid ContractId, string RequestId);

/// <summary>Comprar licencia, reponer o retirar ganancias del negocio propio.</summary>
public sealed record BusinessActionRequest(Guid CharacterId, string RequestId);

// ---------- Catálogo provisional del barrio de prueba ----------

public sealed record ReputationReward(string FamilyCode, int Delta);

public sealed record ContractTemplate(
    string Code,
    string Title,
    string ItemCode,
    int Quantity,
    string SourceNpc,
    string DestinationNpc,
    string DestinationLocation,
    long RewardCents,
    IReadOnlyList<ReputationReward> Reputation);

/// <summary>
/// Valores de prueba para el primer contrato. No son diseño de juego definitivo (ver ADR-003).
/// El precio de las mercancías vive en la base (item_definitions); acá solo se replica para mostrarlo.
/// </summary>
public static class EconomyCatalog
{
    public const long StartingFundsCents = 2_000;

    public const string City = "Bellmont";
    public const string Moretti = "moretti";
    public const string Valenti = "valenti";
    public const string Caruso = "caruso";
    public const int MaxReputation = 1000;

    public const string DepositNpc = "npc:deposito-delgado";
    public const string WorkshopNpc = "npc:taller-moreno";

    public const string SparePartsItem = "caja-repuestos";
    public const long SparePartsPriceCents = 800;

    // --- Puesto de café (primer negocio; valores provisionales, ver ADR-005) ---
    public const string ValentiOfficeNpc = "npc:oficina-valenti";
    public const string CoffeeCornerNpc = "npc:esquina-cafe"; // punto de interacción, sin cuenta económica
    public const string PopulationAccount = "system:poblacion";
    public const string CoffeeBagItem = "bolsa-cafe";
    public const long CoffeeBagPriceCents = 300;
    public const long CoffeeStandLicenseCents = 1_500;
    public const long CoffeeSalePriceCents = 500;
    public const int CoffeeDemandPerCycle = 2;
    public const long CoffeeStandRentCents = 100;
    public const string CoffeeStandLocation = "puesto-cafe";
    public const int MaxCatchUpCycles = 288;

    // Ayudante (empleado básico; decisión del equipo): cobra por ciclo abierto y amplía la venta máxima.
    public const long CoffeeHelperWageCents = 100;
    public const int CoffeeHelperExtraDemand = 1;

    /// <summary>Ubicación de las mercancías que lleva encima un personaje.</summary>
    public const string CarriedLocation = "encima";

    public static readonly ContractTemplate SparePartsDelivery = new(
        Code: "entrega-repuestos",
        Title: "Llevar una caja de repuestos al Taller Caruso",
        ItemCode: SparePartsItem,
        Quantity: 1,
        SourceNpc: DepositNpc,
        DestinationNpc: WorkshopNpc,
        DestinationLocation: "taller-moreno",
        RewardCents: 1_500,
        Reputation: [new ReputationReward(Caruso, 5), new ReputationReward(Moretti, 2)]);

    public static readonly IReadOnlyDictionary<string, ContractTemplate> Templates =
        new Dictionary<string, ContractTemplate> { [SparePartsDelivery.Code] = SparePartsDelivery };

    public static string FormatMoney(long cents) =>
        string.Create(CultureInfo.InvariantCulture, $"{(cents < 0 ? "-" : "")}${Math.Abs(cents) / 100}.{Math.Abs(cents) % 100:00}");
}
