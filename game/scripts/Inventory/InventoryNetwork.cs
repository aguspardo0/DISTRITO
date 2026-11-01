using System.Text.Json;
using Distrito.Game.Combat;
using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Inventory;

/// <summary>Jugador cerca al que se le puede dar algo en mano.</summary>
public sealed record NearbyPlayer(long Peer, string Name);

/// <summary>
/// Inventario por red. El cliente pide ver, dar o soltar; el servidor identifica al personaje por su sesión, valida
/// distancias y reglas, y el backend mueve los objetos en el libro central (nada vive en el cliente).
/// </summary>
public partial class InventoryNetwork : Node
{
    public const float GiveRange = 3f;
    private static readonly JsonSerializerOptions Json = DistritoApiClient.Json;

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private CombatNetwork _combat = null!;
    private readonly HashSet<long> _busy = new();

    // ---- Cliente ----
    public event Action? InventoryReceived;
    public event Action<string>? MessageReceived;
    public InventoryView? Last { get; private set; }
    public IReadOnlyList<NearbyPlayer> Nearby { get; private set; } = [];

    public void Initialize(GameNetwork network, EconomyNetwork economy, CombatNetwork combat)
    {
        _network = network;
        _economy = economy;
        _combat = combat;
    }

    /// <summary>Solo para la prueba visual: muestra un inventario de ejemplo sin servidor.</summary>
    public void Preview()
    {
        Last = new InventoryView(
        [
            new("pistola", "Pistola", "Pistola semiautomática. Liviana y fácil de esconder.", ItemKinds.Weapon, "armas", 1, 1.1, 1, "illegal", 4000),
            new("bala-pistola", "Bala de pistola", "Bala para pistola. Se gasta al disparar.", ItemKinds.Ammo, "municion", 14, 0.02, 0, "illegal", 29),
            new("cajon-bebidas", "Cajón de bebidas", "Un cajón de bebidas para la barra del club.", ItemKinds.Goods, "alimentos", 3, 15, 20, "legal", 400),
            new("bolsa-cafe", "Bolsa de café", "Café en grano. Con esto el puesto de café prepara lo que vende.", ItemKinds.RawMaterial, "alimentos", 2, 5, 8, "legal", 300),
        ], null, null);
        Nearby = [new NearbyPlayer(0, "Sal Moretti")];
        InventoryReceived?.Invoke();
    }

    // ================= Cliente → servidor =================

    public void Request() => Send(MethodName.ServerInventory);
    public void Give(long targetPeer, string itemCode, int quantity) => Send(MethodName.ServerGive, targetPeer, itemCode, quantity);
    public void Drop(string itemCode, int quantity) => Send(MethodName.ServerDrop, itemCode, quantity);

    private void Send(StringName method, params Variant[] args)
    {
        if (Multiplayer.IsServer())
            MessageReceived?.Invoke("El inventario necesita un servidor con cuentas.");
        else
            RpcId(1, method, args);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerInventory()
    {
        if (Multiplayer.IsServer())
            _ = SendInventoryAsync(Multiplayer.GetRemoteSenderId());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerGive(long targetPeer, string itemCode, int quantity)
    {
        if (Multiplayer.IsServer())
            _ = GuardAsync(Multiplayer.GetRemoteSenderId(), peer => GiveAsync(peer, targetPeer, itemCode ?? "", quantity));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerDrop(string itemCode, int quantity)
    {
        if (Multiplayer.IsServer())
            _ = GuardAsync(Multiplayer.GetRemoteSenderId(), peer => DropAsync(peer, itemCode ?? "", quantity));
    }

    // ================= Servidor =================

    /// <summary>Un pedido por jugador a la vez (evita repetir por doble clic o pedidos en ráfaga).</summary>
    private async Task GuardAsync(long peer, Func<long, Task> action)
    {
        if (!_busy.Add(peer))
            return;
        try { await action(peer); }
        finally { _busy.Remove(peer); }
    }

    private async Task GiveAsync(long peer, long targetPeer, string itemCode, int quantity)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "Para usar el inventario necesitás entrar con una cuenta.");
            return;
        }
        if (_network.GetPlayer(peer) is not { Downed: false } giver || _network.GetPlayer(targetPeer) is not { Dead: false } receiver
            || !_network.TryGetSession(targetPeer, out var target))
        {
            SendMessage(peer, "No hay nadie para dárselo.");
            return;
        }
        if (giver.GlobalPosition.DistanceTo(receiver.GlobalPosition) > GiveRange)
        {
            SendMessage(peer, $"{target.CharacterName} está muy lejos: acercate para dárselo en mano.");
            return;
        }
        await _combat.BeforeItemLeavesAsync(peer, itemCode);
        var result = await api.GiveItemAsync(_network.ServerKey, new GiveItemRequest(me.CharacterId, target.CharacterId, itemCode, quantity, Guid.NewGuid().ToString()));
        if (!result.IsSuccess)
        {
            SendMessage(peer, $"Hubo un problema: {result.Error!.Message}");
            return;
        }
        SendMessage(peer, result.Value!.Message);
        if (result.Value.Ok)
        {
            GD.Print($"[DISTRITO] INVENTORY_GIVE {me.CharacterName} → {target.CharacterName}: {quantity} × {itemCode}");
            _economy.RefreshPeer(targetPeer);
            var theirs = await SendInventoryAsync(targetPeer);
            SendMessage(targetPeer, $"{me.CharacterName} te dio {quantity} × {theirs?.Items.FirstOrDefault(i => i.ItemCode == itemCode)?.Name ?? itemCode}.");
        }
        _economy.RefreshPeer(peer);
        await SendInventoryAsync(peer);
    }

    private async Task DropAsync(long peer, string itemCode, int quantity)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "Para usar el inventario necesitás entrar con una cuenta.");
            return;
        }
        if (_network.GetPlayer(peer) is not { Downed: false } player)
            return;
        // Cae un paso adelante del personaje, en el piso.
        var forward = -player.GlobalTransform.Basis.Z with { Y = 0 };
        var spot = player.GlobalPosition + (forward.LengthSquared() > 0.01f ? forward.Normalized() : Vector3.Forward) * 0.9f;
        await _combat.BeforeItemLeavesAsync(peer, itemCode);
        var seconds = _combat.DropSecondsAt(spot);
        var result = await api.DropItemAsync(_network.ServerKey,
            new DropItemRequest(me.CharacterId, itemCode, quantity, Guid.NewGuid(), Guid.NewGuid().ToString(), spot.X, spot.Y, spot.Z, seconds));
        if (!result.IsSuccess)
        {
            SendMessage(peer, $"Hubo un problema: {result.Error!.Message}");
            return;
        }
        SendMessage(peer, result.Value!.Result.Message);
        if (result.Value.Drop is { } drop)
        {
            GD.Print($"[DISTRITO] INVENTORY_DROP {me.CharacterName}: {quantity} × {itemCode}");
            _combat.PlaceDrop(drop, seconds, droppedBy: peer);
        }
        _economy.RefreshPeer(peer);
        await SendInventoryAsync(peer);
    }

    private async Task<InventoryView?> SendInventoryAsync(long peer)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var session))
        {
            SendMessage(peer, "El inventario necesita un servidor con cuentas.");
            return null;
        }
        var view = await api.GetInventoryAsync(_network.ServerKey, session.CharacterId);
        if (!view.IsSuccess || !Multiplayer.GetPeers().Contains((int)peer))
            return null;
        var me = _network.GetPlayer(peer);
        var nearby = _network.AuthenticatedPlayers
            .Where(p => p.Peer != peer && me is not null && _network.GetPlayer(p.Peer) is { Dead: false } other
                        && other.GlobalPosition.DistanceTo(me.GlobalPosition) <= GiveRange)
            .Select(p => new NearbyPlayer(p.Peer, p.Session.CharacterName))
            .ToList();
        RpcId(peer, MethodName.ReceiveInventory, JsonSerializer.Serialize(view.Value, Json),
            nearby.Select(n => n.Peer).ToArray(), nearby.Select(n => n.Name).ToArray());
        return view.Value;
    }

    private void SendMessage(long peer, string text)
    {
        if (Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveMessage, text);
    }

    // ================= Servidor → cliente =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveInventory(string viewJson, long[] nearbyPeers, string[] nearbyNames)
    {
        var view = JsonSerializer.Deserialize<InventoryView>(viewJson, Json);
        if (view is null)
            return;
        Last = view;
        Nearby = nearbyPeers.Zip(nearbyNames, (p, n) => new NearbyPlayer(p, n)).ToList();
        InventoryReceived?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);
}
