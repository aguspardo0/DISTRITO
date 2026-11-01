using System.Text.Json;
using Distrito.Game.Combat;
using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Game.Player;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Transport;

/// <summary>
/// Vehículos por red. El servidor carga los vehículos del backend, simula el manejo con la entrada del conductor y
/// manda el estado a todos (pocos vehículos: sin filtro de interés). Subir/bajar (G), comprar en el Taller Caruso y
/// forzar cajas ajenas pasan por acá; cargar y descargar va por el inventario.
/// </summary>
public partial class VehicleNetwork : Node
{
    private const double StateInterval = 0.05;
    private const double ParkSaveInterval = 10;

    private static VehicleNetwork? _instance;

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private CombatNetwork _combat = null!;
    private Node3D _root = null!;
    private readonly Dictionary<Guid, VehicleBody> _vehicles = new();
    private readonly Dictionary<long, (Guid Vehicle, double Until)> _forcing = new();
    private readonly Dictionary<Guid, Vector3> _lastSaved = new();
    private readonly HashSet<long> _busy = new();
    private bool _loaded;
    private double _clock, _nextState, _nextSave;

    public event Action<string>? MessageReceived;

    public static VehicleBody? Find(string id) =>
        _instance is not null && Guid.TryParse(id, out var guid) && _instance._vehicles.TryGetValue(guid, out var v) ? v : null;

    public void Initialize(GameNetwork network, EconomyNetwork economy, CombatNetwork combat)
    {
        _instance = this;
        _network = network;
        _economy = economy;
        _combat = combat;
        _root = new Node3D { Name = "VehicleBodies" };
        AddChild(_root);
        _network.PlayerAdmitted += (peer, _) => SendAllTo(peer);
        LocalPlayerController.VehicleKey += () => Send(MethodName.ServerToggle);
        LocalPlayerController.DriveInput += (throttle, steer, brake) =>
        {
            if (Multiplayer.MultiplayerPeer is null)
                return;
            if (Multiplayer.IsServer())
                HandleInput(Multiplayer.GetUniqueId(), throttle, steer, brake);
            else
                RpcId(1, MethodName.ServerInput, throttle, steer, brake);
        };
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    // ================= Cliente → servidor =================

    public void Buy(string kind, bool forCrew) => Send(MethodName.ServerBuy, kind, forCrew);
    public void Force(Guid vehicleId) => Send(MethodName.ServerForce, vehicleId.ToString());

    private void Send(StringName method, params Variant[] args)
    {
        if (Multiplayer.MultiplayerPeer is null)
            return;
        if (Multiplayer.IsServer())
            Callable.From(() => Call(method, args)).CallDeferred(); // anfitrión: se atiende acá mismo
        else
            RpcId(1, method, args);
    }

    private long Sender => Multiplayer.GetRemoteSenderId() == 0 ? Multiplayer.GetUniqueId() : Multiplayer.GetRemoteSenderId();

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerToggle()
    {
        if (Multiplayer.IsServer())
            _ = GuardAsync(Sender, ToggleAsync);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ServerInput(float throttle, float steer, bool brake)
    {
        if (Multiplayer.IsServer())
            HandleInput(Multiplayer.GetRemoteSenderId(), throttle, steer, brake);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerBuy(string kind, bool forCrew)
    {
        if (Multiplayer.IsServer())
            _ = GuardAsync(Sender, peer => BuyAsync(peer, kind ?? "", forCrew));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerForce(string vehicleId)
    {
        if (Multiplayer.IsServer() && Guid.TryParse(vehicleId, out var id))
            StartForce(Sender, id);
    }

    // ================= Servidor =================

    private async Task GuardAsync(long peer, Func<long, Task> action)
    {
        if (!_busy.Add(peer))
            return;
        try { await action(peer); }
        finally { _busy.Remove(peer); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Multiplayer.MultiplayerPeer is null || !Multiplayer.IsServer())
            return;
        _clock += delta;
        if (!_loaded && _network.Backend is { } backend)
        {
            _loaded = true;
            _ = LoadAsync(backend);
        }

        foreach (var v in _vehicles.Values)
        {
            if (v.DriverPeer != 0 && _network.GetPlayer(v.DriverPeer) is not { Downed: false })
                Eject(v, announce: true); // se desconectó, quedó herido o murió: el vehículo queda ahí con la carga
            v.Simulate((float)delta);
            if (v.DriverPeer != 0 && _network.GetPlayer(v.DriverPeer) is { } driver)
            {
                driver.GlobalPosition = v.GlobalPosition + Vector3.Up * 0.3f;
                driver.Velocity = Vector3.Zero;
            }
        }

        if (_clock >= _nextState)
        {
            _nextState = _clock + StateInterval;
            BroadcastState();
        }
        if (_clock >= _nextSave)
        {
            _nextSave = _clock + ParkSaveInterval;
            foreach (var v in _vehicles.Values.Where(v => v.DriverPeer != 0))
                SavePark(v);
        }
        ProcessForcing();
    }

    private async Task LoadAsync(DistritoApiClient backend)
    {
        var result = await backend.GetVehiclesAsync(_network.ServerKey);
        if (!result.IsSuccess)
        {
            _loaded = false;
            return;
        }
        foreach (var view in result.Value!)
            Spawn(view);
        if (result.Value.Count > 0)
            GD.Print($"[DISTRITO] {result.Value.Count} vehículos estacionados");
    }

    private void Spawn(VehicleView view)
    {
        if (_vehicles.ContainsKey(view.Id))
            return;
        var body = VehicleBody.Create(view);
        _root.AddChild(body);
        _vehicles[view.Id] = body;
        _lastSaved[view.Id] = body.GlobalPosition;
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ShowVehicle, JsonSerializer.Serialize(view, DistritoApiClient.Json));
    }

    private void SendAllTo(long peer)
    {
        if (!Multiplayer.IsServer())
            return;
        foreach (var v in _vehicles.Values)
            RpcId(peer, MethodName.ShowVehicle, JsonSerializer.Serialize(ViewOf(v), DistritoApiClient.Json));
    }

    private static VehicleView ViewOf(VehicleBody v) =>
        new(v.Id, v.Type.Code, v.OwnerName, null, null, v.GlobalPosition.X, v.GlobalPosition.Y, v.GlobalPosition.Z, v.Rotation.Y);

    private void BroadcastState()
    {
        if (_vehicles.Count == 0 || Multiplayer.GetPeers().Length == 0)
            return;
        var ids = _vehicles.Keys.Select(k => k.ToString()).ToArray();
        var data = _vehicles.Values.SelectMany(v => new[] { v.GlobalPosition.X, v.GlobalPosition.Y, v.GlobalPosition.Z, v.Rotation.Y }).ToArray();
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ReceiveState, ids, data);
    }

    private void HandleInput(long peer, float throttle, float steer, bool brake)
    {
        if (_network.GetPlayer(peer) is { } player && player.Vehicle.Length > 0 && Guid.TryParse(player.Vehicle, out var id)
            && _vehicles.TryGetValue(id, out var v) && v.DriverPeer == peer)
            v.SetInput(throttle, steer, brake, _clock);
    }

    /// <summary>El más cercano a menos de <see cref="Vehicles.UseRange"/>.</summary>
    public VehicleBody? NearestTo(Vector3 position) =>
        _vehicles.Values.Where(v => v.GlobalPosition.DistanceTo(position) <= Vehicles.UseRange)
            .OrderBy(v => v.GlobalPosition.DistanceTo(position)).FirstOrDefault();

    /// <summary>El vehículo que maneja este jugador, o el más cercano.</summary>
    public VehicleBody? AccessibleBy(long peer) =>
        _network.GetPlayer(peer) is not { } p ? null
        : p.Vehicle.Length > 0 && Guid.TryParse(p.Vehicle, out var id) && _vehicles.TryGetValue(id, out var driven) ? driven
        : NearestTo(p.GlobalPosition);

    private async Task ToggleAsync(long peer)
    {
        if (_network.GetPlayer(peer) is not { } player)
            return;
        if (player.Vehicle.Length > 0)
        {
            if (Guid.TryParse(player.Vehicle, out var current) && _vehicles.TryGetValue(current, out var mine))
                Eject(mine, announce: false);
            else
                player.Vehicle = "";
            return;
        }
        if (player.Downed)
            return;
        var v = NearestTo(player.GlobalPosition);
        if (v is null)
            return;
        if (v.DriverPeer != 0)
        {
            SendMessage(peer, "Ya lo está manejando otro.");
            return;
        }
        if (_network.Backend is { } api)
        {
            if (!_network.TryGetSession(peer, out var me))
                return;
            var cargo = await api.GetVehicleCargoAsync(_network.ServerKey, v.Id, me.CharacterId);
            if (cargo is not { IsSuccess: true, Value.CanUse: true })
            {
                SendMessage(peer, $"Es de {v.OwnerName}: solo lo manejan el dueño y su cuadrilla.");
                return;
            }
        }
        if (v.DriverPeer != 0 || player.Downed || player.Vehicle.Length > 0)
            return;
        v.DriverPeer = peer;
        player.Vehicle = v.Id.ToString();
        GD.Print($"[DISTRITO] VEHICLE_ENTER {player.DisplayName} → {v.Type.Name} de {v.OwnerName}");
        SendMessage(peer, $"Manejás la {v.Type.Name.ToLowerInvariant()}: W/S acelerar y frenar, A/D doblar, Espacio freno, G para bajar.");
    }

    private void Eject(VehicleBody v, bool announce)
    {
        var peer = v.DriverPeer;
        v.DriverPeer = 0;
        v.ClearInput();
        if (_network.GetPlayer(peer) is { } player)
        {
            player.Vehicle = "";
            // Se baja por la puerta izquierda.
            var left = -v.GlobalTransform.Basis.X;
            player.GlobalPosition = v.GlobalPosition + left * (v.Type.Width / 2 + 0.8f) + Vector3.Up * 0.2f;
            player.Velocity = Vector3.Zero;
            if (announce)
                SendMessage(peer, "Te bajaste del vehículo.");
        }
        SavePark(v);
    }

    private void SavePark(VehicleBody v)
    {
        if (_network.Backend is not { } api)
            return;
        if (_lastSaved.TryGetValue(v.Id, out var last) && last.DistanceTo(v.GlobalPosition) < 0.5f)
            return;
        _lastSaved[v.Id] = v.GlobalPosition;
        var p = v.GlobalPosition;
        _ = api.ParkVehicleAsync(_network.ServerKey, new VehicleParkRequest(v.Id, p.X, p.Y, p.Z, v.Rotation.Y));
    }

    /// <summary>Lugares de estacionamiento en la puerta del taller, sobre la calle.</summary>
    private static readonly Vector3[] ParkingSpots =
    [
        new(14, 0.2f, 0), new(19, 0.2f, 0), new(24, 0.2f, 0), new(14, 0.2f, -2.5f), new(19, 0.2f, -2.5f), new(24, 0.2f, -2.5f),
        new(36, 0.2f, 0), new(42, 0.2f, 0), new(48, 0.2f, 0),
    ];

    private async Task BuyAsync(long peer, string kind, bool forCrew)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "Los vehículos necesitan un servidor con cuentas.");
            return;
        }
        if (_network.GetPlayer(peer) is not { } player
            || !NpcInteractionRules.InRange(new System.Numerics.Vector3(player.GlobalPosition.X, player.GlobalPosition.Y, player.GlobalPosition.Z), NpcInteractionRules.Workshop))
        {
            SendMessage(peer, "Los vehículos se compran en el Taller Caruso.");
            return;
        }
        var spot = ParkingSpots.FirstOrDefault(s => _vehicles.Values.All(v => v.GlobalPosition.DistanceTo(s) > 4f), ParkingSpots[^1] + new Vector3(0, 0, 3));
        var result = await api.BuyVehicleAsync(_network.ServerKey,
            new VehicleBuyRequest(me.CharacterId, kind, forCrew, spot.X, spot.Y, spot.Z, -Mathf.Pi / 2, Guid.NewGuid().ToString()));
        if (!result.IsSuccess)
        {
            SendMessage(peer, $"Hubo un problema: {result.Error!.Message}");
            return;
        }
        SendMessage(peer, result.Value!.Result.Message);
        if (result.Value.Vehicle is { } bought)
        {
            GD.Print($"[DISTRITO] VEHICLE_BOUGHT {me.CharacterName}: {bought.Kind} ({bought.OwnerName})");
            Spawn(bought);
        }
        _economy.RefreshPeer(peer);
    }

    private void StartForce(long peer, Guid id)
    {
        if (_network.GetPlayer(peer) is not { Downed: false } player || !_vehicles.TryGetValue(id, out var v))
            return;
        if (v.GlobalPosition.DistanceTo(player.GlobalPosition) > Vehicles.UseRange)
        {
            SendMessage(peer, "Acercate al vehículo.");
            return;
        }
        if (v.DriverPeer != 0)
        {
            SendMessage(peer, "Lo están manejando: no se puede forzar.");
            return;
        }
        if (!_combat.PermissionsAt(v.GlobalPosition).Robbery)
        {
            SendMessage(peer, "Acá no se puede robar: solo en zonas en disputa, eventos o Los Muelles.");
            return;
        }
        _forcing[peer] = (id, _clock + Vehicles.ForceSeconds);
        SendMessage(peer, $"Forzando la caja… quedate al lado {Vehicles.ForceSeconds:0} segundos.");
    }

    private void ProcessForcing()
    {
        foreach (var (peer, job) in _forcing.ToList())
        {
            var player = _network.GetPlayer(peer);
            if (player is null || player.Downed || !_vehicles.TryGetValue(job.Vehicle, out var v)
                || v.GlobalPosition.DistanceTo(player.GlobalPosition) > Vehicles.UseRange + 0.5f || v.DriverPeer != 0)
            {
                _forcing.Remove(peer);
                if (player is not null)
                    SendMessage(peer, "Se cortó: no terminaste de forzar la caja.");
                continue;
            }
            if (_clock < job.Until)
                continue;
            _forcing.Remove(peer);
            _ = FinishForceAsync(peer, v);
        }
    }

    private async Task FinishForceAsync(long peer, VehicleBody v)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var back = v.GlobalTransform.Basis.Z;
        var spot = v.GlobalPosition + back * (v.Type.Length / 2 + 1f);
        spot.Y = 0.05f;
        var seconds = _combat.DropSecondsAt(spot);
        var result = await api.ForceVehicleAsync(_network.ServerKey,
            new VehicleForceRequest(me.CharacterId, v.Id, Guid.NewGuid(), spot.X, spot.Y, spot.Z, seconds, Guid.NewGuid().ToString()));
        if (!result.IsSuccess)
        {
            SendMessage(peer, $"Hubo un problema: {result.Error!.Message}");
            return;
        }
        SendMessage(peer, result.Value!.Result.Message);
        if (result.Value.Drop is { } drop)
        {
            GD.Print($"[DISTRITO] VEHICLE_FORCED {me.CharacterName} forzó el vehículo de {v.OwnerName}");
            _combat.PlaceDrop(drop, seconds);
        }
    }

    private void SendMessage(long peer, string text)
    {
        if (peer == Multiplayer.GetUniqueId())
            MessageReceived?.Invoke(text);
        else if (Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveMessage, text);
    }

    // ================= Servidor → clientes =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ShowVehicle(string json)
    {
        if (JsonSerializer.Deserialize<VehicleView>(json, DistritoApiClient.Json) is not { } view || _vehicles.ContainsKey(view.Id))
            return;
        var body = VehicleBody.Create(view);
        _root.AddChild(body);
        _vehicles[view.Id] = body;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReceiveState(string[] ids, float[] data)
    {
        for (var i = 0; i < ids.Length && i * 4 + 3 < data.Length; i++)
            if (Guid.TryParse(ids[i], out var id) && _vehicles.TryGetValue(id, out var v))
                v.SetTarget(new Vector3(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]), data[i * 4 + 3]);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);

    /// <summary>Solo para la prueba visual: una camioneta y un camión al lado del jugador.</summary>
    public void Preview(Vector3 near)
    {
        ShowVehicle(JsonSerializer.Serialize(new VehicleView(Guid.NewGuid(), Vehicles.Pickup.Code, "Sal Moretti", null, null, near.X - 2.5, 0, near.Z - 6, 0.6), DistritoApiClient.Json));
        ShowVehicle(JsonSerializer.Serialize(new VehicleView(Guid.NewGuid(), Vehicles.Truck.Code, "Los del Puerto", null, null, near.X + 3, 0, near.Z - 7, -0.4), DistritoApiClient.Json));
    }
}
