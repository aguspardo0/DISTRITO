namespace Distrito.Shared.Economy;

/// <summary>Estado de PvP del barrio para el servidor de juego.</summary>
public sealed record CombatStateView(List<string> DisputedZones, List<string> EventZones);

/// <summary>Un personaje quedó herido: su mercadería pasa al suelo como el bulto DropId.</summary>
public sealed record KnockoutRequest(Guid VictimId, Guid DropId, string RequestId);

public sealed record PickupRequest(Guid CharacterId, Guid DropId, string RequestId);

/// <summary>Lo que quedó tirado (vacío si el herido no llevaba nada).</summary>
public sealed record DropView(Guid DropId, List<InventoryItem> Items);
