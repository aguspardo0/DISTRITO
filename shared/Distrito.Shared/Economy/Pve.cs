namespace Distrito.Shared.Economy;

/// <summary>Asaltos a NPC (eventos PvE). Lo que se roba sale de existencias y cajas reales (o del contrabando, que entra de afuera).</summary>
public sealed record PveLoadRequest(string EventId, string Supplier, string ItemCode, int Quantity);
public sealed record PveUnloadRequest(string EventId, string Supplier);
public sealed record PveRobGoodsRequest(string EventId, string Supplier, Guid CharacterId, string Family, int Reputation,
    Guid DropId, double X, double Y, double Z, int Seconds);
public sealed record PveRobCashRequest(Guid CharacterId, string Account, long MinCents, long MaxCents, string Family, int Reputation, string Reason, string RequestId);
public sealed record PveLootItem(string ItemCode, int Quantity);
public sealed record PveContrabandRequest(string EventId, List<PveLootItem> Items, Guid DropId, double X, double Y, double Z, int Seconds);
