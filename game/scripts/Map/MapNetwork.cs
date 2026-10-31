using Distrito.Game.Network;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.Map;

/// <summary>
/// Presencia en el mapa (Etapa C): el servidor manda una vez por segundo nombre y posición de todos los jugadores
/// a cada cliente. Es independiente del interés de red 3D: alguien lejano aparece en el mapa sin cargar su modelo.
/// </summary>
public partial class MapNetwork : Node
{
    private GameNetwork _network = null!;
    private double _elapsed;

    /// <summary>Cliente: lo último que mandó el servidor.</summary>
    public IReadOnlyList<MapMarker> Markers { get; private set; } = [];
    public event Action? Updated;

    public void Initialize(GameNetwork network) => _network = network;

    public override void _Process(double delta)
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer || !Multiplayer.IsServer())
            return;
        _elapsed += delta;
        if (_elapsed < MapPresence.IntervalSeconds)
            return;
        _elapsed = 0;

        var everyone = new List<MapMarker>();
        foreach (var peer in Multiplayer.GetPeers().Select(p => (long)p).Append(Multiplayer.GetUniqueId()))
            if (_network.GetPlayer(peer) is { } body)
                everyone.Add(new MapMarker(peer, body.DisplayName.Length > 0 ? body.DisplayName : $"Jugador {peer}",
                    body.GlobalPosition.X, body.GlobalPosition.Z));

        foreach (var peer in Multiplayer.GetPeers())
        {
            var (peers, names, coords) = MapPresence.Pack(MapPresence.VisibleTo(peer, everyone));
            RpcId(peer, MethodName.ReceiveMap, peers, names, coords);
        }
        // Modo anfitrión: el propio servidor también juega.
        if (_network.GetPlayer(Multiplayer.GetUniqueId()) is not null)
            Apply(MapPresence.VisibleTo(Multiplayer.GetUniqueId(), everyone));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReceiveMap(long[] peers, string[] names, float[] coords) => Apply(MapPresence.Unpack(peers, names, coords));

    private void Apply(IReadOnlyList<MapMarker> markers)
    {
        Markers = markers;
        Updated?.Invoke();
    }
}
