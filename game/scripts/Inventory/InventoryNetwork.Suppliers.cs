using System.Text.Json;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Inventory;

/// <summary>Pedidos a proveedores por red: pedir y retirar se hacen en el mostrador (lo valida el servidor).</summary>
public partial class InventoryNetwork
{
    public event Action? SupplierReceived;
    public SupplierView? Supplier { get; private set; }

    public void RequestSupplier(string npc) => Send(MethodName.ServerSupplier, npc);
    public void PlaceOrder(string npc, string itemCode, int quantity, string delivery) => Send(MethodName.ServerSupplierOrder, npc, itemCode, quantity, delivery);
    public void CancelOrder(string npc, Guid orderId) => Send(MethodName.ServerSupplierAction, npc, orderId.ToString(), false);
    public void PickupOrder(string npc, Guid orderId) => Send(MethodName.ServerSupplierAction, npc, orderId.ToString(), true);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerSupplier(string npc)
    {
        if (Multiplayer.IsServer())
            _ = SendSupplierAsync(Multiplayer.GetRemoteSenderId(), npc ?? "");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerSupplierOrder(string npc, string itemCode, int quantity, string delivery)
    {
        if (Multiplayer.IsServer())
            _ = GuardAsync(Multiplayer.GetRemoteSenderId(), peer => SupplierActionAsync(peer, npc ?? "",
                (api, me) => api.PlaceSupplierOrderAsync(_network.ServerKey,
                    new SupplierOrderRequest(me, npc ?? "", itemCode ?? "", quantity, delivery ?? "", Guid.NewGuid().ToString()))));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerSupplierAction(string npc, string orderId, bool pickup)
    {
        if (!Multiplayer.IsServer() || !Guid.TryParse(orderId, out var id))
            return;
        _ = GuardAsync(Multiplayer.GetRemoteSenderId(), peer => SupplierActionAsync(peer, npc ?? "", (api, me) => pickup
            ? api.PickupSupplierOrderAsync(_network.ServerKey, new SupplierOrderActionRequest(me, id, Guid.NewGuid().ToString()))
            : api.CancelSupplierOrderAsync(_network.ServerKey, new SupplierOrderActionRequest(me, id, Guid.NewGuid().ToString())),
            needsCounter: pickup, orderId: id));
    }

    private bool AtNpc(long peer, string npc) =>
        NpcInteractionRules.Find(npc) is { } def && _network.GetPlayer(peer) is { } p
        && NpcInteractionRules.InRange(new System.Numerics.Vector3(p.GlobalPosition.X, p.GlobalPosition.Y, p.GlobalPosition.Z), def);

    private async Task SupplierActionAsync(long peer, string npc,
        Func<DistritoApiClient, Guid, Task<ApiResult<EconomyActionResult>>> action, bool needsCounter = true, Guid? orderId = null)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "Los pedidos necesitan un servidor con cuentas.");
            return;
        }
        if (!SupplierRules.Catalog.ContainsKey(npc))
            return;
        if (needsCounter && !AtNpc(peer, npc))
        {
            SendMessage(peer, $"Eso se hace en el mostrador de {SupplierRules.Names.GetValueOrDefault(npc, npc)}.");
            return;
        }
        // El pedido tiene que ser de este proveedor (así no se retira desde otro mostrador).
        if (orderId is { } id && (await api.GetSupplierAsync(_network.ServerKey, me.CharacterId, npc)).Value?.Orders.All(o => o.Id != id) != false)
        {
            SendMessage(peer, "Ese pedido no es de este proveedor.");
            return;
        }
        var result = await action(api, me.CharacterId);
        SendMessage(peer, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
        if (result is { IsSuccess: true, Value.Ok: true })
            GD.Print($"[DISTRITO] SUPPLIER {me.CharacterName}: {result.Value.Message}");
        _economy.RefreshPeer(peer);
        Distrito.Game.World.VisualStock.RequestRefresh();
        await SendSupplierAsync(peer, npc);
    }

    private async Task SendSupplierAsync(long peer, string npc)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "Los pedidos necesitan un servidor con cuentas.");
            return;
        }
        var view = await api.GetSupplierAsync(_network.ServerKey, me.CharacterId, npc);
        if (view.IsSuccess && Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveSupplier, JsonSerializer.Serialize(view.Value, Json));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveSupplier(string json)
    {
        Supplier = JsonSerializer.Deserialize<SupplierView>(json, Json);
        SupplierReceived?.Invoke();
    }

    /// <summary>Solo para la prueba visual.</summary>
    public void PreviewSupplier()
    {
        var now = DateTimeOffset.UtcNow;
        Supplier = new SupplierView(EconomyCatalog.DepositNpc, "Almacén Moretti",
        [
            new(EconomyCatalog.SparePartsItem, "Caja de repuestos", "Repuestos para autos y camiones.", 800, 12),
            new(EconomyCatalog.CoffeeBagItem, "Bolsa de café", "Café en grano.", 300, 5),
            new(EconomyCatalog.DrinksCrateItem, "Cajón de bebidas", "Un cajón de bebidas para la barra del club.", 400, 15),
        ],
        [
            new(Guid.NewGuid(), EconomyCatalog.CoffeeBagItem, "Bolsa de café", 20, 5400, 540, SupplierRules.Delivery, "preparando", false, now.AddMinutes(10), now.AddMinutes(15)),
            new(Guid.NewGuid(), EconomyCatalog.DrinksCrateItem, "Cajón de bebidas", 5, 2000, 0, SupplierRules.Pickup, "preparando", true, now, now),
        ], true, 25);
        SupplierReceived?.Invoke();
    }
}
