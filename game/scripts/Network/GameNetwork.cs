using Distrito.Game.Player;
using Distrito.Server.Core.Sessions;
using Distrito.Shared.Api;
using Distrito.Shared.Networking;
using Godot;

namespace Distrito.Game.Network;

/// <summary>Credenciales con las que un cliente pide entrar. Invitado = sin token.</summary>
public sealed record ClientCredentials(string Token, Guid CharacterId)
{
    public static ClientCredentials Guest { get; } = new("", Guid.Empty);
    public bool IsGuest => Token.Length == 0;
}

/// <summary>Configuración del bot de pruebas del jugador local.</summary>
public sealed record BotSettings(bool Enabled, double StopAfterSeconds)
{
    public static BotSettings Off { get; } = new(false, 0);
}

/// <summary>
/// Conexión de red de la zona.
/// Servidor: acepta conexiones, exige autenticación (o acepta invitados si no hay backend),
/// crea/elimina personajes y guarda su posición.
/// Cliente: se conecta, se autentica y recibe los personajes replicados.
/// </summary>
public partial class GameNetwork : Node
{
    private const string PlayerScenePath = "res://scenes/Player.tscn";
    private const double SaveIntervalSeconds = 15;

    private readonly ZoneSessions _sessions = new(NetworkDefaults.MaxPlayersPerZone);
    private readonly PendingConnections _pending = new();
    private readonly Dictionary<long, ResolvedSession> _authenticated = new();
    private PackedScene _playerScene = null!;
    private Node3D _players = null!;
    private BotSettings _bot = BotSettings.Off;
    private ClientCredentials _credentials = ClientCredentials.Guest;
    private bool _hasLocalPlayer;

    // Reconexión automática (cliente)
    private const int MaxReconnectAttempts = 10;
    private const double ReconnectDelaySeconds = 2.0;
    private string _lastHost = "";
    private int _lastPort;
    private int _reconnectAttempt;
    private bool _rejected;
    private double _clock;
    private double _nextSave = SaveIntervalSeconds;

    /// <summary>Servidor: interés de red (qué personajes recibe cada cliente).</summary>
    public InterestManager? Interest { get; set; }

    // Solo en el servidor con cuentas:
    private DistritoApiClient? _backend;
    private string _serverKey = "";

    public event Action<string>? StatusChanged;
    public event Action? Connected;

    /// <summary>Servidor: un jugador con cuenta entró a la zona.</summary>
    public event Action<long, ResolvedSession>? PlayerAdmitted;

    public DistritoApiClient? Backend => _backend;
    public string ServerKey => _serverKey;

    public bool TryGetSession(long peerId, out ResolvedSession session) =>
        _authenticated.TryGetValue(peerId, out session!);

    /// <summary>Servidor: jugadores con cuenta conectados ahora.</summary>
    public IEnumerable<(long Peer, ResolvedSession Session)> AuthenticatedPlayers =>
        _authenticated.Select(kv => (kv.Key, kv.Value));

    public long? PeerOfCharacter(Guid characterId) =>
        _authenticated.Where(kv => kv.Value.CharacterId == characterId).Select(kv => (long?)kv.Key).FirstOrDefault();

    public PlayerCharacter? GetPlayer(long peerId) => _players.GetNodeOrNull<PlayerCharacter>(peerId.ToString());

    public int PlayerCount => _players.GetChildCount();

    public PlayerCharacter? LocalPlayer =>
        _players.GetNodeOrNull<PlayerCharacter>(Multiplayer.GetUniqueId().ToString());

    public void Initialize(Node3D players, MultiplayerSpawner spawner, BotSettings bot)
    {
        _players = players;
        _bot = bot;
        _playerScene = GD.Load<PackedScene>(PlayerScenePath);

        spawner.SpawnPath = spawner.GetPathTo(players);
        spawner.AddSpawnableScene(PlayerScenePath);

        _players.ChildEnteredTree += OnPlayerNodeEntered;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    // ---------- Arranque ----------

    /// <summary>Servidor dedicado. Con backend exige cuenta; sin backend acepta invitados.</summary>
    public Error StartDedicatedServer(int port, DistritoApiClient? backend, string serverKey)
    {
        _backend = backend;
        _serverKey = serverKey;
        var error = CreateServerPeer(port);
        if (error == Error.Ok)
            Report($"Servidor dedicado escuchando en el puerto {port} ({(backend is null ? "modo invitado" : "con cuentas")}).");
        return error;
    }

    /// <summary>Anfitrión: este proceso es el servidor y además juega (id de red 1). Siempre modo invitado.</summary>
    public Error StartHost(int port)
    {
        var error = CreateServerPeer(port);
        if (error != Error.Ok)
            return error;

        _hasLocalPlayer = true;
        Report($"Partida creada en el puerto {port}. Esperando jugadores…");
        SpawnPlayer(Multiplayer.GetUniqueId(), null);
        Connected?.Invoke();
        return Error.Ok;
    }

    public Error ConnectTo(string host, int port, ClientCredentials credentials)
    {
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(host, port);
        if (error != Error.Ok)
        {
            Report($"No se pudo crear el cliente: {error}.");
            return error;
        }

        _credentials = credentials;
        _hasLocalPlayer = true;
        _lastHost = host;
        _lastPort = port;
        _rejected = false;
        Multiplayer.MultiplayerPeer = peer;
        if (_reconnectAttempt == 0)
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

    // ---------- Servidor ----------

    public override void _Process(double delta)
    {
        if (!IsServerActive())
            return;

        _clock += delta;

        foreach (var peer in _pending.TakeExpired(_clock))
            Reject(peer, "No se recibió la autenticación a tiempo.");

        if (_backend is not null && _clock >= _nextSave)
        {
            _nextSave = _clock + SaveIntervalSeconds;
            foreach (var peerId in _authenticated.Keys.ToList())
                _ = SaveAsync(peerId);
        }
    }

    private bool IsServerActive() =>
        Multiplayer.MultiplayerPeer is ENetMultiplayerPeer && Multiplayer.IsServer();

    private void OnPeerConnected(long id)
    {
        if (!Multiplayer.IsServer())
            return;
        _pending.Add(id, _clock);
    }

    private void OnPeerDisconnected(long id)
    {
        if (!Multiplayer.IsServer())
            return;

        _pending.TryTake(id);
        if (_authenticated.ContainsKey(id))
            _ = SaveAsync(id, removeAfter: true); // guarda con la última posición antes de borrar el nodo
        else
            RemovePlayer(id);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void Authenticate(string token, string characterId)
    {
        if (!Multiplayer.IsServer())
            return;

        var peer = (long)Multiplayer.GetRemoteSenderId();
        if (!_pending.TryTake(peer))
            return; // ya autenticado o desconocido: se ignora

        _ = AuthenticateAsync(peer, token, characterId);
    }

    private async Task AuthenticateAsync(long peer, string token, string characterIdText)
    {
        if (_backend is null)
        {
            Admit(peer, null);
            return;
        }

        if (string.IsNullOrEmpty(token) || !Guid.TryParse(characterIdText, out var characterId))
        {
            Reject(peer, "Este servidor requiere iniciar sesión con una cuenta.");
            return;
        }

        var result = await _backend.ResolveSessionAsync(_serverKey, token, characterId);

        if (!Multiplayer.GetPeers().Contains((int)peer))
            return; // se desconectó mientras se validaba

        if (!result.IsSuccess)
        {
            Reject(peer, result.Error!.Message);
            return;
        }

        Admit(peer, result.Value);
    }

    private void Admit(long peer, ResolvedSession? session)
    {
        if (!SpawnPlayer(peer, session, out var rejection))
        {
            Reject(peer, rejection switch
            {
                JoinRejection.CharacterAlreadyOnline => "Ese personaje ya está conectado.",
                JoinRejection.ZoneFull => "El barrio está lleno.",
                _ => "No se pudo entrar a la zona.",
            });
            return;
        }

        if (session is not null)
        {
            _authenticated[peer] = session;
            PlayerAdmitted?.Invoke(peer, session);
        }

        Report($"{(session?.CharacterName ?? $"Invitado {peer}")} entró. Jugadores en la zona: {_sessions.Count}.");
    }

    private void Reject(long peer, string reason)
    {
        Report($"Conexión {peer} rechazada: {reason}");
        RpcId(peer, MethodName.AuthRejected, reason);
        // Se espera un instante para que el mensaje llegue antes de cortar.
        GetTree().CreateTimer(0.5).Timeout += () =>
        {
            if (Multiplayer.GetPeers().Contains((int)peer))
                Multiplayer.MultiplayerPeer.DisconnectPeer((int)peer);
        };
    }

    private bool SpawnPlayer(long peerId, ResolvedSession? session) => SpawnPlayer(peerId, session, out _);

    private bool SpawnPlayer(long peerId, ResolvedSession? session, out JoinRejection rejection)
    {
        var zoneSession = _sessions.TryJoin(peerId, session?.CharacterId, out rejection);
        if (zoneSession is null)
            return false;

        var spawnPoint = new Vector3(zoneSession.SpawnPoint.X, zoneSession.SpawnPoint.Y, zoneSession.SpawnPoint.Z);
        var position = session is null
            ? spawnPoint
            : SafePosition(new Vector3((float)session.State.X, (float)session.State.Y, (float)session.State.Z), spawnPoint, session.CharacterName);

        var player = _playerScene.Instantiate<PlayerCharacter>();
        player.Name = peerId.ToString();
        player.DisplayName = session?.CharacterName ?? $"Invitado {peerId}";
        player.SpawnPoint = new Vector3(zoneSession.SpawnPoint.X, zoneSession.SpawnPoint.Y, zoneSession.SpawnPoint.Z);
        player.Position = position;
        player.Rotation = new Vector3(0, (float)(session?.State.Yaw ?? 0), 0);
        Interest?.Register(player); // antes de entrar al árbol, así el primer envío ya respeta el filtro
        _players.AddChild(player, forceReadableName: true);
        return true;
    }

    /// <summary>
    /// Ubicación segura: la posición guardada se usa solo si está dentro del barrio y no queda adentro de un edificio.
    /// Si no, el personaje aparece en su punto de aparición.
    /// </summary>
    private Vector3 SafePosition(Vector3 saved, Vector3 fallback, string who)
    {
        if (!Distrito.Server.Core.World.SpawnSafety.IsWithinWorld(new System.Numerics.Vector3(saved.X, saved.Y, saved.Z)))
        {
            Report($"Posición guardada de {who} fuera del barrio ({saved.X:0.0}, {saved.Y:0.0}, {saved.Z:0.0}): aparece en un lugar seguro.");
            return fallback;
        }

        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Transform = new Transform3D(Basis.Identity, saved + new Vector3(0, 1.0f, 0)),
            CollisionMask = 1,
        };
        foreach (var hit in _players.GetWorld3D().DirectSpaceState.IntersectShape(query, 8))
        {
            if (hit["collider"].AsGodotObject() is StaticBody3D)
            {
                Report($"Posición guardada de {who} dentro de un edificio: aparece en un lugar seguro.");
                return fallback;
            }
        }
        return saved;
    }

    private void RemovePlayer(long peerId)
    {
        _sessions.Leave(peerId);
        _authenticated.Remove(peerId);
        _players.GetNodeOrNull(peerId.ToString())?.QueueFree();
        Report($"Jugador {peerId} salió. Jugadores en la zona: {_sessions.Count}.");
    }

    private async Task SaveAsync(long peerId, bool removeAfter = false)
    {
        if (_backend is null || !_authenticated.TryGetValue(peerId, out var session))
        {
            if (removeAfter) RemovePlayer(peerId);
            return;
        }

        var node = _players.GetNodeOrNull<PlayerCharacter>(peerId.ToString());
        if (node is null)
        {
            if (removeAfter) RemovePlayer(peerId);
            return;
        }

        var p = node.GlobalPosition;
        var state = new CharacterState(p.X, p.Y, p.Z, node.Rotation.Y);
        if (removeAfter)
            RemovePlayer(peerId); // el lugar se libera ya; el guardado sigue en segundo plano

        var result = await _backend.SaveCharacterStateAsync(_serverKey, session.CharacterId, state);
        if (result.IsSuccess)
            GD.Print($"[DISTRITO] Guardado {session.CharacterName}: ({state.X:0.00}, {state.Y:0.00}, {state.Z:0.00})");
        else
            GD.PushError($"[DISTRITO] No se pudo guardar {session.CharacterName}: {result.Error!.Message}");
    }

    // ---------- Cliente ----------

    private void OnConnectedToServer()
    {
        Report(_reconnectAttempt > 0 ? "Reconectado. Volviendo al barrio…" : "Conectado. Entrando al barrio…");
        _reconnectAttempt = 0;
        RpcId(1, MethodName.Authenticate, _credentials.Token, _credentials.IsGuest ? "" : _credentials.CharacterId.ToString());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void AuthRejected(string reason)
    {
        _rejected = true; // si el servidor nos rechazó, no tiene sentido reconectar solo
        Report($"No se pudo entrar: {reason}");
    }

    private void OnServerDisconnected()
    {
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        foreach (var child in _players.GetChildren())
            child.QueueFree();

        if (_rejected || _lastHost.Length == 0)
        {
            Report("Se perdió la conexión con el servidor.");
            return;
        }
        ScheduleReconnect();
    }

    private void OnConnectionFailed()
    {
        if (_reconnectAttempt > 0)
            ScheduleReconnect();
        else
            Report("No se pudo conectar al servidor.");
    }

    /// <summary>Reintenta con las mismas credenciales (el token de sesión sigue vigente) unas cuantas veces.</summary>
    private void ScheduleReconnect()
    {
        if (_reconnectAttempt >= MaxReconnectAttempts)
        {
            _reconnectAttempt = 0;
            Report("No se pudo reconectar con el servidor.");
            return;
        }
        _reconnectAttempt++;
        Report($"Conexión perdida. Reconectando ({_reconnectAttempt}/{MaxReconnectAttempts})…");
        GetTree().CreateTimer(ReconnectDelaySeconds).Timeout += () =>
        {
            if (ConnectTo(_lastHost, _lastPort, _credentials) != Error.Ok)
                ScheduleReconnect();
        };
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
            Bot = _bot,
        });
        Connected?.Invoke();
        CallDeferred(MethodName.ReportEntered, player);
    }

    public void ReportEntered(PlayerCharacter player) => Report($"Estás en el barrio como {player.DisplayName}.");

    private void Report(string message)
    {
        GD.Print($"[DISTRITO] {message}");
        StatusChanged?.Invoke(message);
    }
}
