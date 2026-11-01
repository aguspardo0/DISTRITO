using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Game.Player;
using Distrito.Server.Core.Combat;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Combat;

/// <summary>
/// PvP a piñas, con autoridad del servidor (decisión del equipo: PvP regulado, 2026-10-31).
/// El cliente solo pide "pegar"; el servidor busca a quién le llega, aplica las reglas (zona segura, nivel, zona en
/// disputa, evento), descuenta vida y, si llega a 0, deja al personaje herido 10 s y tira su mercadería al suelo.
/// La mercadería tirada se levanta pasando por encima.
/// </summary>
public partial class CombatNetwork : Node
{
    private const double StateRefreshSeconds = 10;
    private const double LevelRefreshSeconds = 15;
    private const float PickupRadius = 1.3f;
    private const double DropLifetimeSeconds = 300;

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private readonly Dictionary<long, double> _lastPunch = new();
    private readonly Dictionary<long, double> _lastRefusal = new();
    private readonly Dictionary<Guid, int> _levels = new();
    private readonly Dictionary<Guid, (Vector3 Position, string Label, double Created)> _drops = new();
    private readonly Dictionary<Guid, Node3D> _dropVisuals = new();
    private readonly HashSet<Guid> _pickingUp = new();
    private CombatState _state = CombatState.None;
    private List<WarPair> _wars = [];
    private Dictionary<Guid, Guid> _warMembers = new();
    private double _clock, _nextState, _nextLevels, _nextPickupCheck;

    /// <summary>Cliente: mensajes para el HUD.</summary>
    public event Action<string>? MessageReceived;
    /// <summary>Pruebas: piñas que el servidor confirmó a este cliente.</summary>
    public int ConfirmedHits { get; private set; }

    public void Initialize(GameNetwork network, EconomyNetwork economy)
    {
        _network = network;
        _economy = economy;
        LocalPlayerController.Punch += RequestPunch;
        _network.PlayerAdmitted += (peer, _) => SendDropsTo(peer);
    }

    public override void _ExitTree() => LocalPlayerController.Punch -= RequestPunch;

    // ================= Cliente → servidor =================

    public void RequestPunch()
    {
        if (_network.LocalPlayer is null)
            return;
        if (Multiplayer.IsServer())
            HandlePunch(Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.ServerPunch);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerPunch()
    {
        if (Multiplayer.IsServer())
            HandlePunch(Multiplayer.GetRemoteSenderId());
    }

    // ================= Servidor =================

    public override void _Process(double delta)
    {
        _clock += delta;
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer || !Multiplayer.IsServer())
            return;
        if (_network.Backend is { } backend)
        {
            if (_clock >= _nextState)
            {
                _nextState = _clock + StateRefreshSeconds;
                _ = RefreshStateAsync(backend);
            }
            if (_clock >= _nextLevels)
            {
                _nextLevels = _clock + LevelRefreshSeconds;
                _ = RefreshLevelsAsync(backend);
            }
            if (_clock >= _nextPickupCheck)
            {
                _nextPickupCheck = _clock + 0.3;
                CheckPickups(backend);
            }
        }
        foreach (var (id, drop) in _drops.ToList())
            if (_clock - drop.Created > DropLifetimeSeconds)
                RemoveDropEverywhere(id);
    }

    private async Task RefreshStateAsync(Distrito.Shared.Api.DistritoApiClient backend)
    {
        var result = await backend.GetCombatStateAsync(_network.ServerKey);
        if (result.IsSuccess)
        {
            _state = new CombatState(result.Value!.DisputedZones.ToHashSet(), result.Value.EventZones.ToHashSet());
            _wars = result.Value.Wars ?? [];
            _warMembers = result.Value.WarMembers ?? new();
        }
    }

    private async Task RefreshLevelsAsync(Distrito.Shared.Api.DistritoApiClient backend)
    {
        foreach (var (_, session) in _network.AuthenticatedPlayers.ToList())
        {
            var snapshot = await backend.GetEconomyAsync(_network.ServerKey, session.CharacterId);
            if (snapshot.IsSuccess)
                _levels[session.CharacterId] = snapshot.Value!.ProgressOrEmpty.Level;
        }
    }

    private int? LevelOf(long peer)
    {
        if (_network.Backend is null)
            return null; // partida local sin cuentas
        return _network.TryGetSession(peer, out var s) && _levels.TryGetValue(s.CharacterId, out var level) ? level : 1;
    }

    private static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>¿Las cuadrillas de los dos están en guerra ahora?</summary>
    private bool AtWar(long a, long b)
    {
        if (!_network.TryGetSession(a, out var sa) || !_network.TryGetSession(b, out var sb))
            return false;
        if (!_warMembers.TryGetValue(sa.CharacterId, out var crewA) || !_warMembers.TryGetValue(sb.CharacterId, out var crewB))
            return false;
        return _wars.Any(w => (w.CrewA == crewA && w.CrewB == crewB) || (w.CrewA == crewB && w.CrewB == crewA));
    }

    private void HandlePunch(long peer)
    {
        if (_network.GetPlayer(peer) is not { Downed: false } attacker)
            return;
        if (_lastPunch.TryGetValue(peer, out var last) && _clock - last < CombatRules.PunchCooldownSeconds)
            return;
        _lastPunch[peer] = _clock;
        PlayPunchEverywhere(peer);

        var target = _network.AllPlayers
            .Where(p => p != attacker && !p.Downed && CombatRules.InReach(N(attacker.GlobalPosition), attacker.GlobalRotation.Y, N(p.GlobalPosition)))
            .OrderBy(p => p.GlobalPosition.DistanceTo(attacker.GlobalPosition))
            .FirstOrDefault();
        if (target is null)
            return;

        if (CombatRules.WhyNot(N(attacker.GlobalPosition), N(target.GlobalPosition), LevelOf(peer), LevelOf(target.PeerId), _state,
                AtWar(peer, target.PeerId)) is { } reason)
        {
            if (!_lastRefusal.TryGetValue(peer, out var said) || _clock - said > 3)
            {
                _lastRefusal[peer] = _clock;
                SendMessage(peer, reason);
            }
            return;
        }

        target.Health = Math.Max(0, target.Health - CombatRules.PunchDamage);
        SendHit(peer);
        if (target.Health > 0)
            return;

        target.Downed = true;
        GD.Print($"[DISTRITO] COMBAT_KO {attacker.DisplayName} dejó herido a {target.DisplayName}");
        SendMessage(peer, $"Dejaste herido a {target.DisplayName}.");
        SendMessage(target.PeerId, $"{attacker.DisplayName} te dejó herido. Volvés en {CombatRules.RespawnSeconds:0} segundos.");
        _ = DropGoodsAsync(target);
        var victimPeer = target.PeerId;
        GetTree().CreateTimer(CombatRules.RespawnSeconds).Timeout += () => Respawn(victimPeer);
    }

    private void Respawn(long peer)
    {
        if (_network.GetPlayer(peer) is not { } player)
            return;
        player.Velocity = Vector3.Zero;
        player.GlobalPosition = player.SpawnPoint;
        player.Health = CombatRules.MaxHealth;
        player.Downed = false;
        SendMessage(peer, "Volviste. Cuidate.");
    }

    private async Task DropGoodsAsync(PlayerCharacter victim)
    {
        if (_network.Backend is not { } backend || !_network.TryGetSession(victim.PeerId, out var session))
            return;
        var id = Guid.NewGuid();
        var position = victim.GlobalPosition;
        var result = await backend.KnockoutAsync(_network.ServerKey, new KnockoutRequest(session.CharacterId, id, Guid.NewGuid().ToString()));
        if (!result.IsSuccess || result.Value!.Items.Count == 0)
            return;
        var label = string.Join(", ", result.Value.Items.Select(i => $"{i.Name} ×{i.Quantity}"));
        _drops[id] = (position, label, _clock);
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ShowDrop, id.ToString(), position, label);
        if (_network.GetPlayer(Multiplayer.GetUniqueId()) is not null)
            ShowDrop(id.ToString(), position, label);
        _economy.RefreshPeer(victim.PeerId);
    }

    private void CheckPickups(Distrito.Shared.Api.DistritoApiClient backend)
    {
        foreach (var (id, drop) in _drops.ToList())
        {
            if (_pickingUp.Contains(id))
                continue;
            var picker = _network.AllPlayers.FirstOrDefault(p => !p.Downed && p.GlobalPosition.DistanceTo(drop.Position) <= PickupRadius);
            if (picker is null || !_network.TryGetSession(picker.PeerId, out var session))
                continue;
            _pickingUp.Add(id);
            _ = PickupAsync(backend, id, picker.PeerId, session.CharacterId);
        }
    }

    private async Task PickupAsync(Distrito.Shared.Api.DistritoApiClient backend, Guid id, long peer, Guid characterId)
    {
        try
        {
            var result = await backend.PickupAsync(_network.ServerKey, new PickupRequest(characterId, id, Guid.NewGuid().ToString()));
            if (result.IsSuccess)
            {
                RemoveDropEverywhere(id);
                SendMessage(peer, result.Value!.Message);
                _economy.RefreshPeer(peer);
            }
        }
        finally
        {
            _pickingUp.Remove(id);
        }
    }

    private void RemoveDropEverywhere(Guid id)
    {
        _drops.Remove(id);
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.HideDrop, id.ToString());
        HideDrop(id.ToString());
    }

    private void SendDropsTo(long peer)
    {
        foreach (var (id, drop) in _drops)
            RpcId(peer, MethodName.ShowDrop, id.ToString(), drop.Position, drop.Label);
    }

    private void PlayPunchEverywhere(long attacker)
    {
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.PlayPunch, attacker);
        PlayPunch(attacker);
    }

    private void SendMessage(long peer, string text)
    {
        if (peer == Multiplayer.GetUniqueId())
            ShowMessage(text);
        else
            RpcId(peer, MethodName.ShowMessage, text);
    }

    private void SendHit(long peer)
    {
        if (peer == Multiplayer.GetUniqueId())
            ConfirmHit();
        else
            RpcId(peer, MethodName.ConfirmHit);
    }

    // ================= Servidor → clientes =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ShowMessage(string text) => MessageReceived?.Invoke(text);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ConfirmHit() => ConfirmedHits++;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void PlayPunch(long attacker) => _network.GetPlayer(attacker)?.PlayPunch();

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ShowDrop(string id, Vector3 position, string label)
    {
        if (!Guid.TryParse(id, out var guid) || _dropVisuals.ContainsKey(guid))
            return;
        var node = new Node3D { Name = $"Bulto_{guid:N}" };
        node.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.3f, 0.35f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#7a5a36") },
            Position = new Vector3(0, 0.15f, 0),
        });
        node.AddChild(new Label3D { Text = $"{label}\n(pasá por encima)", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, OutlineSize = 6, Position = new Vector3(0, 0.8f, 0) });
        AddChild(node);
        node.GlobalPosition = position;
        _dropVisuals[guid] = node;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void HideDrop(string id)
    {
        if (Guid.TryParse(id, out var guid) && _dropVisuals.Remove(guid, out var node))
            node.QueueFree();
    }
}
