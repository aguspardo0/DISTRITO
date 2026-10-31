using System.Globalization;

namespace Distrito.Shared.Economy;

// ---------- Estado económico de un personaje (lo que ve el jugador) ----------

public sealed record InventoryItem(string ItemCode, string Name, int Quantity);

public sealed record ContractView(Guid Id, string TemplateCode, string Status);

public sealed record EconomySnapshot(
    long BalanceCents,
    List<InventoryItem> Inventory,
    List<ContractView> ActiveContracts,
    int CompletedContracts)
{
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

// ---------- Catálogo provisional del barrio de prueba ----------

public sealed record ContractTemplate(
    string Code,
    string Title,
    string ItemCode,
    int Quantity,
    string SourceNpc,
    string DestinationNpc,
    string DestinationLocation,
    long RewardCents);

/// <summary>
/// Valores de prueba para el primer contrato. No son diseño de juego definitivo (ver ADR-003).
/// El precio de las mercancías vive en la base (item_definitions); acá solo se replica para mostrarlo.
/// </summary>
public static class EconomyCatalog
{
    public const long StartingFundsCents = 2_000;

    public const string DepositNpc = "npc:deposito-delgado";
    public const string WorkshopNpc = "npc:taller-moreno";

    public const string SparePartsItem = "caja-repuestos";
    public const long SparePartsPriceCents = 800;

    /// <summary>Ubicación de las mercancías que lleva encima un personaje.</summary>
    public const string CarriedLocation = "encima";

    public static readonly ContractTemplate SparePartsDelivery = new(
        Code: "entrega-repuestos",
        Title: "Llevar una caja de repuestos al Taller Moreno",
        ItemCode: SparePartsItem,
        Quantity: 1,
        SourceNpc: DepositNpc,
        DestinationNpc: WorkshopNpc,
        DestinationLocation: "taller-moreno",
        RewardCents: 1_500);

    public static readonly IReadOnlyDictionary<string, ContractTemplate> Templates =
        new Dictionary<string, ContractTemplate> { [SparePartsDelivery.Code] = SparePartsDelivery };

    public static string FormatMoney(long cents) =>
        string.Create(CultureInfo.InvariantCulture, $"{(cents < 0 ? "-" : "")}${Math.Abs(cents) / 100}.{Math.Abs(cents) % 100:00}");
}
