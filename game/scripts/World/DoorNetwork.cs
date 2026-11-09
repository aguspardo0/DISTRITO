using Distrito.Game.Network;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Puertas de los locales (desde el 2026-11-09): cada una es un objeto con física que se abre y se cierra con E, y se
/// entra caminando (el salón está adentro del edificio, sin pasar a otro lado). Lo decide el servidor: verifica que el
/// jugador esté al lado, a pie y en pie, y avisa a todos; cada equipo gira la hoja (con su colisión). Se cierran solas a
/// los <see cref="AutoCloseSeconds"/> si no hay nadie en el paso.
/// </summary>
public partial class DoorNetwork : Node
{
    public const double AutoCloseSeconds = 12;
    private const float OpenAngle = Mathf.Pi / 2 * 0.95f; // hacia adentro del salón
    private const float Speed = 4f;                       // radianes por segundo

    public static DoorNetwork? Instance { get; private set; }

    /// <summary>¿Hay un habitante a menos de tantos metros de este punto? (lo conecta Main con la vida de los NPC): abren la puerta para pasar.</summary>
    public static Func<System.Numerics.Vector2, float, bool>? ResidentNear { get; set; }
    private double _residentCheck;

    private GameNetwork _network = null!;
    private readonly Dictionary<long, double> _lastUse = new();
    private readonly Dictionary<string, double> _openSince = new(); // puertas abiertas y desde cuándo
    private double _clock, _nextSync;

    public void Initialize(GameNetwork network)
    {
        Instance = this;
        _network = network;
        _network.PlayerAdmitted += (peer, _) =>
        {
            if (Multiplayer.IsServer())
                RpcId(peer, MethodName.ReceiveAll, string.Join(',', _openSince.Keys));
        };
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Para las capturas de prueba: deja una puerta abierta sin pasar por el servidor.</summary>
    public void PreviewOpen(string code) => _openSince[code] = _clock + 1000;

    public static bool IsOpen(string code) => Instance?._openSince.ContainsKey(code) == true;

    /// <summary>Texto para el cartel de abajo, si hay una puerta al lado.</summary>
    public static string? PromptAt(Vector3 p) => InteriorLayout.DoorNear(p.X, p.Z) is { } door
        ? IsOpen(door.Plan.Code) ? "[E] Cerrar la puerta" : door.Inside ? "[E] Abrir la puerta" : $"[E] Abrir la puerta de {door.Plan.Name}"
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
        if (_lastUse.TryGetValue(peer, out var last) && _clock - last < 0.4)
            return;
        var p = player.GlobalPosition;
        if (InteriorLayout.DoorNear(p.X, p.Z) is not { } door)
            return;
        _lastUse[peer] = _clock;
        var open = !_openSince.ContainsKey(door.Plan.Code);
        GD.Print($"[DISTRITO] PUERTA {player.DisplayName} {(open ? "abre" : "cierra")} {door.Plan.Name}");
        Set(door.Plan.Code, open);
    }

    private void Set(string code, bool open)
    {
        if (open) _openSince[code] = _clock; else _openSince.Remove(code);
        foreach (var peer in Multiplayer.GetPeers())
            RpcId(peer, MethodName.ReceiveDoor, code, open);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveDoor(string code, bool open)
    {
        if (open) _openSince[code] = _clock; else _openSince.Remove(code);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveAll(string openCodes)
    {
        _openSince.Clear();
        foreach (var code in openCodes.Split(',', StringSplitOptions.RemoveEmptyEntries))
            _openSince[code] = _clock;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        // Todos (servidor y clientes) giran la hoja hacia donde tiene que estar: así la colisión es la misma en todos lados.
        foreach (var (code, leaf) in InteriorBuilder.Leaves)
        {
            var target = _openSince.ContainsKey(code) ? OpenAngle : 0f;
            var y = Mathf.MoveToward(leaf.Rotation.Y, target, Speed * (float)delta);
            if (!Mathf.IsEqualApprox(y, leaf.Rotation.Y))
                leaf.Rotation = new Vector3(0, y, 0);
        }
        if (!Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer())
            return;
        // Los habitantes que entran o salen de un local abren la puerta (después se cierra sola).
        if (ResidentNear is { } near && (_residentCheck += delta) >= 0.5)
        {
            _residentCheck = 0;
            foreach (var plan in InteriorLayout.All)
                if (!_openSince.ContainsKey(plan.Code) && (near(plan.Door, 2.2f) || near(plan.InsideDoor, 2.2f)))
                    Set(plan.Code, true);
        }
        // Se cierran solas si nadie está en el paso.
        foreach (var (code, since) in _openSince.ToList())
        {
            if (_clock - since < AutoCloseSeconds || InteriorLayout.Find(code) is not { } plan)
                continue;
            var blocked = _network.AllPlayers.Any(pl =>
            {
                var at = new System.Numerics.Vector2(pl.GlobalPosition.X, pl.GlobalPosition.Z);
                return System.Numerics.Vector2.Distance(at, plan.Door) < plan.DoorWidth + 0.6f || System.Numerics.Vector2.Distance(at, plan.InsideDoor) < plan.DoorWidth + 0.6f;
            });
            if (blocked)
                _openSince[code] = _clock - AutoCloseSeconds + 2; // prueba de nuevo en 2 segundos
            else
                Set(code, false);
        }
        if (_clock >= _nextSync)
        {
            _nextSync = _clock + 5;
            foreach (var peer in Multiplayer.GetPeers())
                RpcId(peer, MethodName.ReceiveAll, string.Join(',', _openSince.Keys));
        }
    }
}
