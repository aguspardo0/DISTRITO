namespace Distrito.Shared.Economy;

/// <summary>Estado de PvP del barrio para el servidor de juego.</summary>
/// <summary>Estado de PvP del barrio para el servidor de juego. WarMembers: personaje → cuadrilla, de las que están en guerra.</summary>
public sealed record CombatStateView(List<string> DisputedZones, List<string> EventZones,
    List<WarPair>? Wars = null, Dictionary<Guid, Guid>? WarMembers = null, string? ZoneRulesJson = null);

public sealed record WarPair(Guid CrewA, Guid CrewB);

/// <summary>Un personaje quedó herido: su mercadería pasa al suelo como el bulto DropId.</summary>
/// <summary>Un personaje murió: sus armas y munición (y la mercadería si IncludeGoods) pasan al suelo como el bulto DropId.</summary>
public sealed record KnockoutRequest(Guid VictimId, Guid DropId, string RequestId, bool IncludeGoods = true,
    double X = 0, double Y = 0, double Z = 0, int Seconds = 300);

public sealed record PickupRequest(Guid CharacterId, Guid DropId, string RequestId);

/// <summary>Lo que quedó tirado (vacío si el herido no llevaba nada).</summary>
public sealed record DropView(Guid DropId, List<InventoryItem> Items, double X = 0, double Y = 0, double Z = 0, DateTimeOffset? ExpiresAt = null);
