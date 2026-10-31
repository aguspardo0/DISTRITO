using System.Globalization;
using Distrito.Shared.Progression;

namespace Distrito.Shared.Economy;

// ---------- Estado económico de un personaje (lo que ve el jugador) ----------

public sealed record InventoryItem(string ItemCode, string Name, int Quantity);

/// <summary>Contrato en curso. ExpiresAt: solo en contratos con plazo.</summary>
public sealed record ContractView(Guid Id, string TemplateCode, string Status, DateTimeOffset? ExpiresAt = null)
{
    public int SecondsLeft(DateTimeOffset now) => ExpiresAt is { } e ? Math.Max(0, (int)Math.Ceiling((e - now).TotalSeconds)) : -1;
}

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
    long WagesCents = 0,
    string Kind = EconomyCatalog.CoffeeStandLocation)
{
    public BusinessType Type => EconomyCatalog.BusinessTypes[Kind];
    public long CostOfGoodsCents => UnitsSold * Type.ItemPriceCents;
    public long ProfitCents => RevenueCents - RentCents - WagesCents - CostOfGoodsCents;
    public long StockValueCents => Stock * Type.ItemPriceCents;
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
    BusinessView? Business = null,
    ProgressView? Progress = null,
    List<BusinessView>? Businesses = null)
{
    /// <summary>El negocio propio de ese tipo (el puesto de café también está en <see cref="Business"/>).</summary>
    public BusinessView? BusinessOf(string kind) =>
        Businesses?.FirstOrDefault(b => b.Kind == kind) ?? (Business?.Kind == kind ? Business : null);

    public ProgressView ProgressOrEmpty => Progress ?? ProgressView.Empty;

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

/// <summary>Companions: personajes que el servidor de juego vio cerca del NPC. El backend se queda solo con los de la misma cuadrilla.</summary>
public sealed record DeliverContractRequest(Guid CharacterId, Guid ContractId, string RequestId, IReadOnlyList<Guid>? Companions = null);

/// <summary>Comprar licencia, reponer, retirar o contratar en el negocio propio del tipo Kind.</summary>
public sealed record BusinessActionRequest(Guid CharacterId, string RequestId, string Kind = EconomyCatalog.CoffeeStandLocation);

/// <summary>
/// Un tipo de negocio administrado por jugadores. Todos funcionan igual: licencia de los Valenti, mercadería de un
/// proveedor, ventas solas por ciclo, alquiler por ciclo con ventas y un empleado opcional que vende uno más.
/// </summary>
public sealed record BusinessType(
    string Code,
    string Name,
    string SiteNpc,
    long LicenseCents,
    long RentCents,
    string ItemCode,
    string ItemName,
    long ItemPriceCents,
    string SupplierNpc,
    long SalePriceCents,
    int BaseDemand,
    string EmployeeName,
    long EmployeeWageCents,
    int EmployeeExtraDemand,
    bool NightBonus = false);

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
    IReadOnlyList<ReputationReward> Reputation,
    string? PayerNpc = null,
    int TimeLimitSeconds = 0,
    IReadOnlyList<ReputationReward>? FailureReputation = null,
    ReputationReward? RequiredReputation = null,
    bool RequiresCrewMate = false)
{
    /// <summary>Quién paga: por defecto el destinatario.</summary>
    public string Payer => PayerNpc ?? DestinationNpc;
    public bool HasGoods => Quantity > 0;
    public bool HasTimeLimit => TimeLimitSeconds > 0;
}

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

    // --- Club de jazz (segundo negocio; propuesta aprobada por el equipo, 2026-10-31) ---
    public const string JazzClubNpc = "npc:club-jazz";
    public const string JazzClubKind = "club-jazz";
    public const string DrinksCrateItem = "cajon-bebidas";

    /// <summary>Noche en Bellmont: de 20 a 4 hs, hora de Argentina (UTC−3). El club vende uno más.</summary>
    public static bool IsNight(DateTimeOffset when)
    {
        var hour = when.ToOffset(TimeSpan.FromHours(-3)).Hour;
        return hour >= 20 || hour < 4;
    }

    public static readonly BusinessType CoffeeStand = new(
        CoffeeStandLocation, "Puesto de café", CoffeeCornerNpc, CoffeeStandLicenseCents, CoffeeStandRentCents,
        CoffeeBagItem, "bolsa de café", CoffeeBagPriceCents, DepositNpc, CoffeeSalePriceCents, CoffeeDemandPerCycle,
        "ayudante", CoffeeHelperWageCents, CoffeeHelperExtraDemand);

    public static readonly BusinessType JazzClub = new(
        JazzClubKind, "Club de jazz", JazzClubNpc, LicenseCents: 3_000, RentCents: 200,
        DrinksCrateItem, "cajón de bebidas", ItemPriceCents: 400, SupplierNpc: DepositNpc, SalePriceCents: 700, BaseDemand: 2,
        "músico", EmployeeWageCents: 200, EmployeeExtraDemand: 1, NightBonus: true);

    public static readonly IReadOnlyDictionary<string, BusinessType> BusinessTypes =
        new Dictionary<string, BusinessType> { [CoffeeStand.Code] = CoffeeStand, [JazzClub.Code] = JazzClub };

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

    // Contratos 2 y 3 (propuesta aprobada por el equipo, 2026-10-31).
    public static readonly ContractTemplate UrgentCoffeeOrder = new(
        Code: "pedido-urgente",
        Title: "Pedido urgente: 2 bolsas de café a la Oficina Valenti",
        ItemCode: CoffeeBagItem,
        Quantity: 2,
        SourceNpc: DepositNpc,
        DestinationNpc: ValentiOfficeNpc,
        DestinationLocation: "oficina-valenti",
        RewardCents: 1_000,
        Reputation: [new ReputationReward(Moretti, 4)],
        PayerNpc: DepositNpc,
        TimeLimitSeconds: 180,
        FailureReputation: [new ReputationReward(Moretti, -3)]);

    public static readonly ContractTemplate CarusoCollection = new(
        Code: "cobranza-caruso",
        Title: "Cobranza en el Taller Caruso (en cuadrilla)",
        ItemCode: "",
        Quantity: 0,
        SourceNpc: ValentiOfficeNpc,
        DestinationNpc: WorkshopNpc,
        DestinationLocation: "taller-moreno",
        RewardCents: 1_200,
        Reputation: [new ReputationReward(Valenti, 6), new ReputationReward(Caruso, -2)],
        PayerNpc: ValentiOfficeNpc,
        RequiredReputation: new ReputationReward(Caruso, 5),
        RequiresCrewMate: true);

    public static readonly IReadOnlyDictionary<string, ContractTemplate> Templates =
        new Dictionary<string, ContractTemplate>
        {
            [SparePartsDelivery.Code] = SparePartsDelivery,
            [UrgentCoffeeOrder.Code] = UrgentCoffeeOrder,
            [CarusoCollection.Code] = CarusoCollection,
        };

    public static string FamilyName(string code) => $"{char.ToUpperInvariant(code[0])}{code[1..]}";

    public static string DescribeReputation(IEnumerable<ReputationReward> rewards) =>
        string.Join(", ", rewards.Select(r => $"{(r.Delta >= 0 ? "+" : "")}{r.Delta} con los {FamilyName(r.FamilyCode)}"));

    public static string FormatMoney(long cents) =>
        string.Create(CultureInfo.InvariantCulture, $"{(cents < 0 ? "-" : "")}${Math.Abs(cents) / 100}.{Math.Abs(cents) % 100:00}");
}
