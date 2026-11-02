using Distrito.Game.Network;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Puertas de los interiores: con E cerca de una puerta se entra (o se sale). Lo decide el servidor: verifica que el
/// jugador esté en la puerta, a pie y en pie, y lo mueve adentro o afuera. El cliente solo gira la cámara.
/// </summary>
public partial class DoorNetwork : Node
{
    public static DoorNetwork? Instance { get; private set; }

    private GameNetwork _network = null!;
    private readonly Dictionary<long, double> _lastUse = new();
    private double _clock;

    public void Initialize(GameNetwork network)
    {
        Instance = this;
        _network = network;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta) => _clock += delta;

    /// <summary>Texto para el cartel de abajo («[E] Entrar a…»), si hay una puerta al lado.</summary>
    public static string? PromptAt(Vector3 p) => InteriorLayout.DoorNear(p.X, p.Z) is { } door
        ? door.Inside ? "[E] Salir a la calle" : $"[E] Entrar a {door.Plan.Name}"
        : null;

    public void Use()
    {
        if (Multiplayer.MultiplayerPeer is null)
            return;
        if (Multiplayer.IsServer())
            ServerUse(1);
        else
            RpcId(1, MethodName.RequestUse);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestUse()
    {
        if (Multiplayer.IsServer())
            ServerUse(Multiplayer.GetRemoteSenderId());
    }

    private void ServerUse(long peer)
    {
        if (_network.GetPlayer(peer) is not { } player || player.Downed || player.Vehicle.Length > 0)
            return;
        if (_lastUse.TryGetValue(peer, out var last) && _clock - last < 0.6)
            return; // evita entrar y salir de golpe con dos toques
        var p = player.GlobalPosition;
        if (InteriorLayout.DoorNear(p.X, p.Z) is not { } door)
            return;
        _lastUse[peer] = _clock;
        var (spot, yaw) = door.Inside ? (door.Plan.ExitPoint, door.Plan.ExitYaw) : (door.Plan.EntryPoint, door.Plan.EntryYaw);
        player.Velocity = Vector3.Zero;
        player.GlobalPosition = new Vector3(spot.X, 0.15f, spot.Y);
        player.Rotation = new Vector3(0, yaw, 0);
        GD.Print($"[DISTRITO] PUERTA {player.DisplayName} {(door.Inside ? "sale de" : "entra a")} {door.Plan.Name}");
        if (peer != 1 && Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveFacing, yaw);
        else
            ReceiveFacing(yaw);
    }

    /// <summary>Al cruzar la puerta, la cámara mira hacia adentro del salón (o hacia la calle al salir).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveFacing(float yaw) =>
        _network.LocalPlayer?.GetNodeOrNull<Distrito.Game.Player.LocalPlayerController>("LocalController")?.LookToward(yaw);
}
