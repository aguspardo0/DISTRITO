using Distrito.Game.Player;
using Distrito.Server.Core.Interest;
using Godot;

namespace Distrito.Game.Network;

/// <summary>
/// Movimiento de los personajes por la red, en paquetes compactos (ver <see cref="PlayerSnapshotCodec"/>).
/// Antes lo mandaba el sincronizador de Godot: un paquete por personaje y por observador, 20 veces por segundo,
/// aunque estuviera lejos o quieto. Ahora el servidor arma UN paquete por observador y por paso con los personajes
/// que le tocan (<see cref="SnapshotScheduler"/>): los cercanos 20 veces por segundo, los lejanos 10 o 5, y los
/// quietos solo un recordatorio por segundo. El sincronizador sigue mandando la posición al aparecer y los datos que
/// cambian poco (nombre, salud, arma…).
/// En el cliente solo se actualiza la posición del nodo: el suavizado visual y la predicción local siguen igual.
/// </summary>
public partial class PlayerSnapshots : Node
{
    private readonly SnapshotScheduler _scheduler = new();
    private readonly List<PlayerState> _states = new();
    private readonly List<PlayerState> _batch = new(PlayerSnapshotCodec.MaxEntriesPerPacket);
    private GameNetwork _network = null!;
    private long _tick;

    public void Initialize(GameNetwork network)
    {
        _network = network;
        Multiplayer.PeerDisconnected += peer => _scheduler.ForgetPeer(peer);
    }

    /// <summary>Peor tiempo (ms) de armar y mandar los paquetes en un cuadro, desde la última lectura. Para métricas.</summary>
    public static double TakeMaxWorkMs()
    {
        var value = _maxWorkMs;
        _maxWorkMs = 0;
        return value;
    }

    private static double _maxWorkMs;
    private readonly HashSet<long> _peers = new();
    private readonly List<PlayerCharacter> _players = new();
    private long _frame;

    /// <summary>
    /// Corre en cada cuadro de física (60 por segundo) pero atiende a un tercio de los observadores por cuadro:
    /// cada uno recibe su paquete 20 veces por segundo y el trabajo no se amontona en un solo cuadro.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer())
            return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _frame++;
        var group = _frame % Groups;
        _tick = _frame / Groups;

        _players.Clear();
        _states.Clear();
        foreach (var player in _network.AllPlayers)
            if (player.IsInsideTree())
            {
                _players.Add(player);
                _states.Add(StateOf(player));
            }
        if (_states.Count == 0)
            return;

        _peers.Clear();
        foreach (var peer in Multiplayer.GetPeers())
            _peers.Add(peer);
        var interest = _network.Interest;
        foreach (var observer in _players)
        {
            var observerId = observer.PeerId;
            if (observerId % Groups != group || !_peers.Contains(observerId))
                continue;
            var observerPosition = ToNumerics(observer.GlobalPosition);
            _batch.Clear();
            foreach (var state in _states)
            {
                if (interest is not null && !interest.Sees(observerId, state.PeerId))
                {
                    _scheduler.Forget(state.PeerId, observerId); // si vuelve a verse, se manda enseguida
                    continue;
                }
                if (!_scheduler.ShouldSend(state, observerId, observerPosition, _tick))
                    continue;
                _scheduler.MarkSent(state, observerId, _tick);
                _batch.Add(state);
                if (_batch.Count == PlayerSnapshotCodec.MaxEntriesPerPacket)
                    Flush(observerId);
            }
            Flush(observerId);
        }
        _maxWorkMs = Math.Max(_maxWorkMs, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private const int Groups = 3; // 60 cuadros de física / 20 paquetes por segundo

    private void Flush(long observer)
    {
        if (_batch.Count == 0)
            return;
        RpcId(observer, MethodName.Receive, PlayerSnapshotCodec.Encode(_batch));
        _batch.Clear();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void Receive(byte[] data)
    {
        List<PlayerState> states;
        try
        {
            states = PlayerSnapshotCodec.Decode(data);
        }
        catch (ArgumentException)
        {
            return;
        }
        foreach (var s in states)
        {
            var player = _network.GetPlayer(s.PeerId);
            if (player is null || !player.IsInsideTree())
                continue;
            player.GlobalPosition = new Vector3(s.Position.X, s.Position.Y, s.Position.Z);
            player.Rotation = new Vector3(0, s.Yaw, 0);
            player.AimPitch = s.AimPitch;
        }
    }

    private static PlayerState StateOf(PlayerCharacter p) =>
        new(p.PeerId, ToNumerics(p.GlobalPosition), p.Rotation.Y, p.AimPitch);

    private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}
