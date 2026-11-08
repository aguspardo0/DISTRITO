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
    private readonly Dictionary<Guid, (long Peer, double Until)> _noPickup = new();
    private readonly Dictionary<Guid, Node3D> _dropVisuals = new();
    private readonly HashSet<Guid> _pickingUp = new();
    private CombatState _state = CombatState.None;
    private ZoneRules? _rules;
    private string? _rulesJson;
    private List<WarPair> _wars = [];
    private Dictionary<Guid, Guid> _warMembers = new();
    private double _clock, _nextState, _nextLevels, _nextPickupCheck;

    /// <summary>Cliente: mensajes para el HUD.</summary>
    public event Action<string>? MessageReceived;

    /// <summary>
    /// Servidor: pasó algo en la calle que los habitantes pueden ver u oír (un tiro, alguien herido, un asalto). Lo usa
    /// la vida de los NPC para que reaccionen (etapa 7).
    /// </summary>
    public event Action<string, Vector3>? Incident;

    public void ReportIncident(string kind, Vector3 at) => Incident?.Invoke(kind, at);
    /// <summary>Pruebas: piñas que el servidor confirmó a este cliente.</summary>
    public int ConfirmedHits { get; private set; }

    public void Initialize(GameNetwork network, EconomyNetwork economy)
    {
        _network = network;
        _economy = economy;
        LocalPlayerController.Punch += OnPunchKey;
        _network.PlayerAdmitted += (peer, _) => SendDropsTo(peer);
        InitializeWeapons();
    }

    public override void _ExitTree()
    {
        LocalPlayerController.Punch -= OnPunchKey;
        LocalPlayerController.WeaponKey -= OnWeaponKey;
    }

    /// <summary>Clic o F: piña con las manos vacías (con un arma, el disparo lo maneja ProcessLocalShooting).</summary>
    private void OnPunchKey()
    {
        if (_myWeapon == Weapons.Fists)
            RequestPunch();
    }

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
        ProcessLocalShooting(delta);
        ProcessLocalRevive();
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer || !Multiplayer.IsServer())
            return;
        ProcessBleeding();
        if (_network.Backend is { } backend)
        {
            if (!_dropsLoaded)
            {
                _dropsLoaded = true;
                _ = LoadDropsAsync(backend);
            }
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
            ProcessAmmoFlush();
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

    private bool _dropsLoaded;

    /// <summary>Al arrancar el servidor de juego, vuelve a poner en la calle los bultos que quedaron (persistencia).</summary>
    private async Task LoadDropsAsync(Distrito.Shared.Api.DistritoApiClient backend)
    {
        var result = await backend.GetDropsAsync(_network.ServerKey);
        if (!result.IsSuccess)
        {
            _dropsLoaded = false;
            return;
        }
        foreach (var drop in result.Value!)
            PlaceDrop(drop, ((drop.ExpiresAt ?? DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow).TotalSeconds);
        if (result.Value.Count > 0)
            GD.Print($"[DISTRITO] {result.Value.Count} bultos recuperados de la calle");
    }

    /// <summary>Dónde reaparece un jugador (la sede de su cuadrilla); null = su punto de aparición.</summary>
    public Func<long, Vector3?>? RespawnPoint { get; set; }

    private IReadOnlyList<int> _baseSheds = [];

    /// <summary>Galpones que son sede (adentro es zona segura). Lo actualiza BaseNetwork.</summary>
    public IReadOnlyList<int> BaseSheds
    {
        get => _baseSheds;
        set
        {
            _baseSheds = value;
            _state = _state with { BaseSheds = value };
        }
    }

    /// <summary>El backend mandó config/eventos-pve.json (lo toma PveNetwork).</summary>
    public event Action<string>? PveRulesChanged;

    private async Task RefreshStateAsync(Distrito.Shared.Api.DistritoApiClient backend)
    {
        var result = await backend.GetCombatStateAsync(_network.ServerKey);
        if (result.IsSuccess)
        {
            if (result.Value!.ZoneRulesJson is { } json && json != _rulesJson)
            {
                var (rules, errors) = ZoneRules.Parse(json);
                if (rules is not null) { _rules = rules; _rulesJson = json; }
                else GD.PushWarning($"[DISTRITO] zonas.json con errores, se mantienen las reglas anteriores: {string.Join(" | ", errors)}");
            }
            _state = new CombatState(result.Value.DisputedZones.ToHashSet(), result.Value.EventZones.ToHashSet(), _rules, _baseSheds);
            if (result.Value.PveRulesJson is { } pve)
                PveRulesChanged?.Invoke(pve);
            _wars = result.Value.Wars ?? [];
            _warMembers = result.Value.WarMembers ?? new();
        }
    }

    private async Task RefreshLevelsAsync(Distrito.Shared.Api.DistritoApiClient backend)
    {
        foreach (var (peer, session) in _network.AuthenticatedPlayers.ToList())
        {
            var snapshot = await backend.GetEconomyAsync(_network.ServerKey, session.CharacterId);
            if (snapshot.IsSuccess)
                OnSnapshotSent(peer, snapshot.Value!);
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
        if (_network.GetPlayer(peer) is not { Downed: false, Vehicle.Length: 0 } attacker)
            return;
        if (_lastPunch.TryGetValue(peer, out var last) && _clock - last < CombatRules.PunchCooldownSeconds)
            return;
        _lastPunch[peer] = _clock;
        PlayPunchEverywhere(peer);

        var target = _network.AllPlayers
            .Where(p => p != attacker && !p.Dead && p.Vehicle.Length == 0 && CombatRules.InReach(N(attacker.GlobalPosition), attacker.GlobalRotation.Y, N(p.GlobalPosition)))
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

        ApplyDamage(attacker, target, CombatRules.PunchDamage + (int)Skill(attacker.PeerId, Distrito.Shared.Progression.SkillTree.IronFists)); // Puños de hierro
    }

    // ---------- Daño, herido y muerte (Player Death System) ----------

    private readonly Dictionary<long, double> _bleedUntil = new();
    private readonly Dictionary<(long Reviver, long Target), (double Progress, double LastTick)> _revives = new();

    private void ApplyDamage(PlayerCharacter attacker, PlayerCharacter target, int damage)
    {
        if (damage <= 0 || target.Dead)
            return;
        SendHit(attacker.PeerId);
        if (target.Downed)
        {
            Kill(target, attacker); // rematar a un herido
            return;
        }
        target.Health = Math.Max(0, target.Health - damage);
        if (target.Health > 0)
            return;

        target.Downed = true;
        ReportIncident(Distrito.Shared.Life.IncidentKinds.Wounded, target.GlobalPosition);
        _bleedUntil[target.PeerId] = _clock + CombatRules.BleedSeconds + Skill(target.PeerId, Distrito.Shared.Progression.SkillTree.ColdBlood); // Sangre fría
        GD.Print($"[DISTRITO] COMBAT_KO {attacker.DisplayName} dejó herido a {target.DisplayName}");
        SendMessage(attacker.PeerId, $"Dejaste herido a {target.DisplayName}.");
        SendMessage(target.PeerId, $"{attacker.DisplayName} te dejó herido. Si en {CombatRules.BleedSeconds:0} s no te levanta un compañero, morís.");
    }

    private void Kill(PlayerCharacter victim, PlayerCharacter? killer)
    {
        if (victim.Dead)
            return;
        _bleedUntil.Remove(victim.PeerId);
        victim.Downed = true;
        victim.Dead = true;
        victim.Health = 0;
        GD.Print($"[DISTRITO] COMBAT_KILL {victim.DisplayName}{(killer is null ? " se desangró" : $" rematado por {killer.DisplayName}")}");
        if (killer is not null)
            SendMessage(killer.PeerId, $"Remataste a {victim.DisplayName}.");
        SendMessage(victim.PeerId, $"Moriste. Tus armas quedaron tiradas. Volvés en {CombatRules.RespawnSeconds:0} segundos.");
        _ = DropOnDeathAsync(victim);
        var victimPeer = victim.PeerId;
        GetTree().CreateTimer(CombatRules.RespawnSeconds).Timeout += () => Respawn(victimPeer);
    }

    private void Revive(PlayerCharacter target, PlayerCharacter by)
    {
        _bleedUntil.Remove(target.PeerId);
        target.Downed = false;
        target.Health = CombatRules.ReviveHealth;
        GD.Print($"[DISTRITO] COMBAT_REVIVE {by.DisplayName} levantó a {target.DisplayName}");
        SendMessage(by.PeerId, $"Levantaste a {target.DisplayName}.");
        SendMessage(target.PeerId, $"{by.DisplayName} te levantó.");
    }

    /// <summary>Mismo bando: misma cuadrilla o coalición (en partidas locales, cualquiera).</summary>
    private bool SameSide(long a, long b)
    {
        if (_network.Backend is null)
            return true;
        if (!_network.TryGetSession(a, out var sa) || !_network.TryGetSession(b, out var sb))
            return false;
        return _snapshots.TryGetValue(sa.CharacterId, out var x) && _snapshots.TryGetValue(sb.CharacterId, out var y)
               && x.FactionId is { } fa && fa == y.FactionId;
    }

    /// <summary>Cliente: mantener E cerca de un compañero herido manda "levantando" cada 0,25 s.</summary>
    private double _nextReviveTick;
    private void ProcessLocalRevive()
    {
        if (_network.LocalPlayer is not { Downed: false } me || !Input.IsPhysicalKeyPressed(Key.E) || _clock < _nextReviveTick)
            return;
        var target = _network.AllPlayers.FirstOrDefault(p => p != me && p.Downed && !p.Dead && p.GlobalPosition.DistanceTo(me.GlobalPosition) <= CombatRules.ReviveRange);
        if (target is null)
            return;
        _nextReviveTick = _clock + 0.25;
        if (Multiplayer.IsServer())
            HandleReviveTick(Multiplayer.GetUniqueId(), target.PeerId);
        else
            RpcId(1, MethodName.ServerReviveTick, target.PeerId);
    }

    /// <summary>Pruebas: empezar a levantar al herido más cercano (como si se mantuviera E).</summary>
    public void RequestReviveTick(long target)
    {
        if (Multiplayer.IsServer())
            HandleReviveTick(Multiplayer.GetUniqueId(), target);
        else
            RpcId(1, MethodName.ServerReviveTick, target);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ServerReviveTick(long target)
    {
        if (Multiplayer.IsServer())
            HandleReviveTick(Multiplayer.GetRemoteSenderId(), target);
    }

    private void HandleReviveTick(long reviver, long targetPeer)
    {
        if (_network.GetPlayer(reviver) is not { Downed: false } by || _network.GetPlayer(targetPeer) is not { Downed: true, Dead: false } target)
            return;
        if (by.GlobalPosition.DistanceTo(target.GlobalPosition) > CombatRules.ReviveRange || !SameSide(reviver, targetPeer))
            return;
        var key = (reviver, targetPeer);
        var (progress, lastTick) = _revives.GetValueOrDefault(key);
        if (_clock - lastTick > 0.6)
            progress = 0; // soltó la tecla: empieza de nuevo
        progress += Math.Min(0.35, _clock - lastTick is > 0 and < 0.6 ? _clock - lastTick : 0.25);
        _revives[key] = (progress, _clock);
        if (progress >= CombatRules.ReviveSeconds)
        {
            _revives.Remove(key);
            Revive(target, by);
        }
    }

    private void ProcessBleeding()
    {
        foreach (var (peer, until) in _bleedUntil.ToList())
            if (_clock >= until && _network.GetPlayer(peer) is { } victim)
                Kill(victim, null);
    }

    /// <summary>Panel de administración: cura del todo en el lugar (si estaba herido o muerto, se levanta).</summary>
    public void AdminHeal(long peer)
    {
        if (_network.GetPlayer(peer) is not { } player)
            return;
        _bleedUntil.Remove(peer);
        player.Health = MaxHealthOf(peer);
        player.Downed = false;
        player.Dead = false;
    }

    private void Respawn(long peer)
    {
        if (_network.GetPlayer(peer) is not { } player)
            return;
        player.Velocity = Vector3.Zero;
        player.GlobalPosition = RespawnPoint?.Invoke(peer) ?? player.SpawnPoint; // con sede, se reaparece adentro
        player.Health = MaxHealthOf(peer);
        player.Downed = false;
        player.Dead = false;
        SendMessage(peer, "Volviste. Cuidate.");
    }

    private async Task DropOnDeathAsync(PlayerCharacter victim)
    {
        await BeforeKnockoutAsync(victim);
        if (_network.Backend is not { } backend || !_network.TryGetSession(victim.PeerId, out var session))
            return;
        var id = Guid.NewGuid();
        var position = victim.GlobalPosition;
        var rules = CombatRules.PermissionsAt(N(position), _rules ?? ZoneRules.ServerDefault, _state, false);
        var result = await backend.KnockoutAsync(_network.ServerKey,
            new KnockoutRequest(session.CharacterId, id, Guid.NewGuid().ToString(), rules.LoseGoodsOnDeath,
                position.X, position.Y, position.Z, rules.DropSeconds));
        if (!result.IsSuccess || result.Value!.Items.Count == 0)
            return;
        PlaceDrop(result.Value, rules.DropSeconds);
        _economy.RefreshPeer(victim.PeerId);
    }

    /// <summary>Permisos de la zona en ese punto (reglas de config/zonas.json, sin guerra).</summary>
    public ZonePermissions PermissionsAt(Vector3 position) =>
        CombatRules.PermissionsAt(N(position), _rules ?? ZoneRules.ServerDefault, _state, false);

    /// <summary>Segundos que dura un bulto tirado en ese lugar (regla de zona).</summary>
    public int DropSecondsAt(Vector3 position) =>
        CombatRules.PermissionsAt(N(position), _rules ?? ZoneRules.ServerDefault, _state, false).DropSeconds;

    /// <summary>Muestra un bulto a todos y lo recuerda hasta que vence (en segundos desde ahora).
    /// El que lo soltó a propósito no lo vuelve a levantar sin querer durante unos segundos.</summary>
    public void PlaceDrop(DropView drop, double secondsLeft, long droppedBy = 0)
    {
        if (droppedBy != 0)
            _noPickup[drop.DropId] = (droppedBy, _clock + 4);
        var position = new Vector3((float)drop.X, (float)drop.Y, (float)drop.Z);
        var label = string.Join(", ", drop.Items.Select(i => $"{i.Name} ×{i.Quantity}"));
        _drops[drop.DropId] = (position, label, _clock - DropLifetimeSeconds + secondsLeft);
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ShowDrop, drop.DropId.ToString(), position, label);
        if (_network.GetPlayer(Multiplayer.GetUniqueId()) is not null)
            ShowDrop(drop.DropId.ToString(), position, label);
    }

    private void CheckPickups(Distrito.Shared.Api.DistritoApiClient backend)
    {
        foreach (var (id, drop) in _drops.ToList())
        {
            if (_pickingUp.Contains(id))
                continue;
            // Reglas de zona: hace falta poder recoger y saquear donde quedó el bulto.
            var here = CombatRules.PermissionsAt(N(drop.Position), _rules ?? ZoneRules.ServerDefault, _state, false);
            if (!here.Pickup || !here.Looting)
                continue;
            var blocked = _noPickup.TryGetValue(id, out var nb) && _clock < nb.Until ? nb.Peer : 0;
            var picker = _network.AllPlayers.FirstOrDefault(p => !p.Downed && p.PeerId != blocked && p.GlobalPosition.DistanceTo(drop.Position) <= PickupRadius);
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
        _noPickup.Remove(id);
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
    public void PlayPunch(long attacker)
    {
        if (_network.GetPlayer(attacker) is not { } p)
            return;
        p.PlayPunch();
        Distrito.Game.Audio.SoundPlayer.At(Distrito.Game.Audio.Sfx.Punch, p.VisualPosition + Vector3.Up * 1.3f, -4, Distrito.Game.Audio.SoundPlayer.Vary(), 30);
    }

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
