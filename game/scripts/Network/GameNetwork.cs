using Distrito.Game.Player;
using Distrito.Server.Core.Sessions;
using Distrito.Shared.Networking;
using Godot;

namespace Distrito.Game.Network;

/// <summary>
/// Conexión de red de la zona: crea el servidor ENet, el anfitrión o el cliente,
/// y (en el servidor) crea y elimina los personajes de los jugadores.
/// La replicación a los clientes la hacen MultiplayerSpawner y MultiplayerSynchronizer.
/// </summary>
public partial class GameNetwork : Node
{
    private const string PlayerScenePath = "res://scenes/Player.tscn";

    private readonly ZoneSessions _sessions = new(NetworkDefaults.MaxPlayersPerZone);
    private PackedScene _playerScene = null!;
    private Node3D _players = null!;
    private bool _hasLocalPlayer;
    private bool _botLocalPlayer;

    /// <summary>Mensajes de estado para la interfaz y los logs.</summary>
    public event Action<string>? StatusChanged;

    public event Action? Connected;

    public int PlayerCount => _players.GetChildCount();

    public PlayerCharacter? LocalPlayer =>
        _players.GetNodeOrNull<PlayerCharacter>(Multiplayer.GetUniqueId().ToString());

    public void Initialize(Node3D players, MultiplayerSpawner spawner, bool botLocalPlayer)
    {
        _players = players;
        _botLocalPlayer = botLocalPlayer;
        _playerScene = GD.Load<PackedScene>(PlayerScenePath);

        spawner.SpawnPath = spawner.GetPathTo(players);
        spawner.AddSpawnableScene(PlayerScenePath);

        _players.ChildEnteredTree += OnPlayerNodeEntered;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += () => Report("No se pudo conectar al servidor.");
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    /// <summary>Servidor dedicado: sin jugador local.</summary>
    public Error StartDedicatedServer(int port)
    {
        var error = CreateServerPeer(port);
        if (error == Error.Ok)
            Report($"Servidor dedicado escuchando en el puerto {port}.");
        return error;
    }

    /// <summary>Anfitrión: este proceso es el servidor y además juega (id de red 1).</summary>
    public Error StartHost(int port)
    {
        var error = CreateServerPeer(port);
        if (error != Error.Ok)
            return error;

        _hasLocalPlayer = true;
        Report($"Partida creada en el puerto {port}. Esperando jugadores…");
        SpawnPlayer(Multiplayer.GetUniqueId());
        Connected?.Invoke();
        return Error.Ok;
    }

    public Error ConnectTo(string host, int port)
    {
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(host, port);
        if (error != Error.Ok)
        {
            Report($"Error al crear el cliente: {error}.");
            return error;
        }

        _hasLocalPlayer = true;
        Multiplayer.MultiplayerPeer = peer;
        Report($"Conectando a {host}:{port}…");
        return Error.Ok;
    }

    private Error CreateServerPeer(int port)
    {
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, NetworkDefaults.MaxPlayersPerZone);
        if (error != Error.Ok)
        {
            Report($"No se pudo abrir el puerto {port}: {error}.");
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    private void OnPeerConnected(long id)
    {
        if (!Multiplayer.IsServer())
            return;

        if (!SpawnPlayer(id))
        {
            Report($"Zona llena: se rechaza al peer {id}.");
            Multiplayer.MultiplayerPeer.DisconnectPeer((int)id);
            return;
        }

        Report($"Jugador {id} conectado. Jugadores en la zona: {_sessions.Count}.");
    }

    private void OnPeerDisconnected(long id)
    {
        if (!Multiplayer.IsServer())
            return;

        _sessions.Leave(id);
        _players.GetNodeOrNull(id.ToString())?.QueueFree();
        Report($"Jugador {id} desconectado. Jugadores en la zona: {_sessions.Count}.");
    }

    private bool SpawnPlayer(long peerId)
    {
        var session = _sessions.TryJoin(peerId);
        if (session is null)
            return false;

        var spawn = new Vector3(session.SpawnPoint.X, session.SpawnPoint.Y, session.SpawnPoint.Z);
        var player = _playerScene.Instantiate<PlayerCharacter>();
        player.Name = peerId.ToString();
        player.SpawnPoint = spawn;
        player.Position = spawn;
        _players.AddChild(player, forceReadableName: true);
        return true;
    }

    private void OnConnectedToServer()
    {
        Report($"Conectado. Sos el jugador {Multiplayer.GetUniqueId()}.");
        Connected?.Invoke();
    }

    private void OnServerDisconnected()
    {
        Report("Se perdió la conexión con el servidor.");
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        foreach (var child in _players.GetChildren())
            child.QueueFree();
    }

    /// <summary>Cuando aparece el personaje propio, se le agrega el control local y la cámara.</summary>
    private void OnPlayerNodeEntered(Node node)
    {
        if (!_hasLocalPlayer || node is not PlayerCharacter player)
            return;
        if (player.Name.ToString() != Multiplayer.GetUniqueId().ToString())
            return;

        player.CallDeferred(Node.MethodName.AddChild, new LocalPlayerController
        {
            Name = "LocalController",
            UseBot = _botLocalPlayer,
        });
    }

    private void Report(string message)
    {
        GD.Print($"[DISTRITO] {message}");
        StatusChanged?.Invoke(message);
    }
}
