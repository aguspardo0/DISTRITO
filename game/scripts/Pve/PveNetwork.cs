using Distrito.Game.Combat;
using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Game.Player;
using Distrito.Server.Core.Npcs;
using Distrito.Server.Core.Pve;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Pve;

/// <summary>
/// Asaltos a NPC (eventos PvE, aprobado por el equipo 2026-11-01). El servidor decide todo: cuándo sale el camión de
/// reparto y el cobrador, cuándo llega el contrabando a Los Muelles, quién amenaza a quién apuntándole con el arma, los
/// tiros de los guardias y el botín (que sale del backend, con datos reales). Los clientes solo ven los NPC moverse.
/// Reglas en config/eventos-pve.json.
/// </summary>
public partial class PveNetwork : Node
{
    private enum Kind { Truck, Collector, Guard }

    private sealed class Actor
    {
        public required string Id;
        public required Kind Kind;
        public required Node3D Body;
        public required string Label;
        public IReadOnlyList<(float X, float Z)> Route = [];
        public int Next = 1;
        public float Speed;
        public int Health = 1;
        public bool Robbed;
        public double LeaveAt = double.MaxValue;
        public readonly Dictionary<long, float> Threat = new();
        public double NextShot;
    }

    private const double StateInterval = 0.1;

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private CombatNetwork _combat = null!;
    private PveRules? _rules;
    private string _rulesJson = "";
    private readonly Dictionary<string, Actor> _actors = new();
    private readonly Dictionary<string, Node3D> _clientActors = new();
    private readonly Dictionary<string, (Vector3 Pos, float Yaw)> _targets = new();
    private readonly Dictionary<string, double> _shopCooldown = new();
    private readonly Dictionary<(long Peer, string Shop), float> _shopThreat = new();
    private long _truckSlot = -1, _collectorSlot = -1;
    private string? _contrabandSlot;
    private string? _contrabandEvent;
    private double _contrabandEnds;
    private Node3D? _contrabandCrates;
    private double _clock, _nextState, _nextNotice;

    /// <summary>Algo que cuenta para misiones de cuadrilla (jugador, tipo de misión). Lo atiende BaseNetwork.</summary>
    public event Action<long, string, string?>? MissionEvent;

    public void Initialize(GameNetwork network, EconomyNetwork economy, CombatNetwork combat)
    {
        _network = network;
        _economy = economy;
        _combat = combat;
        _combat.PveRulesChanged += json =>
        {
            if (json == _rulesJson)
                return;
            var (rules, errors) = PveRules.Parse(json);
            if (rules is null)
            {
                GD.PushWarning($"[DISTRITO] eventos-pve.json con errores, se mantienen las reglas anteriores: {string.Join(" | ", errors)}");
                return;
            }
            _rules = rules;
            _rulesJson = json;
        };
        _combat.NpcShot = HitTest;
        _combat.NpcHit = OnShot;
        _combat.ShotTransparent = () => _actors.Values.Select(a => a.Body).OfType<CollisionObject3D>().Select(b => b.GetRid()).ToList();
        _network.PlayerAdmitted += (peer, _) =>
        {
            foreach (var a in _actors.Values)
                RpcId(peer, MethodName.ShowActor, a.Id, (int)a.Kind, a.Label, a.Body.GlobalPosition, a.Body.Rotation.Y);
            if (_contrabandCrates is not null)
                RpcId(peer, MethodName.ShowCrates, _contrabandCrates.GlobalPosition);
        };
    }

    /// <summary>Panel de administración: sacar un evento ahora (camion, cobrador o contrabando). Devuelve qué pasó.</summary>
    public string ForceEvent(string kind)
    {
        if (_rules is null)
            return "Todavía no llegaron las reglas de eventos (config/eventos-pve.json) desde el backend.";
        var tag = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        switch (kind)
        {
            case "camion" when _rules.Truck is { } t:
                if (_actors.Values.Any(a => a.Kind == Kind.Truck)) return "Ya hay un camión en la calle.";
                _ = StartTruckAsync(t, -tag);
                return "Sale el camión de reparto.";
            case "cobrador" when _rules.Collector is { } c:
                if (_actors.Values.Any(a => a.Kind == Kind.Collector)) return "El cobrador ya está haciendo la ronda.";
                StartCollector(c, -tag);
                return "Sale el cobrador de Valenti.";
            case "contrabando" when _rules.Contraband is { } k:
                if (_contrabandEvent is not null) return "Ya hay contrabando en Los Muelles.";
                StartContraband(k, $"admin:{tag}");
                return "Llegó el contrabando a Los Muelles.";
            default:
                return "Ese evento no está configurado.";
        }
    }

    // ================= Servidor =================

    public override void _PhysicsProcess(double delta)
    {
        if (Multiplayer.MultiplayerPeer is null || !Multiplayer.IsServer() || _network.Backend is null || _rules is null)
            return;
        _clock += delta;
        var dt = (float)delta;
        var now = DateTimeOffset.UtcNow;

        if (_rules.Truck is { } truck && PveRules.SlotOf(now, truck.EveryMinutes) is var ts && ts != _truckSlot && _actors.Values.All(a => a.Kind != Kind.Truck))
        {
            _truckSlot = ts;
            _ = StartTruckAsync(truck, ts);
        }
        if (_rules.Collector is { } col && PveRules.SlotOf(now, col.EveryMinutes) is var cs && cs != _collectorSlot && _actors.Values.All(a => a.Kind != Kind.Collector))
        {
            _collectorSlot = cs;
            StartCollector(col, cs);
        }
        if (_rules.Contraband is { } con && PveRules.ContrabandSlot(now, con.Hours) is { } slot && slot != _contrabandSlot && _contrabandEvent is null)
        {
            _contrabandSlot = slot;
            StartContraband(con, slot);
        }
        if (_contrabandEvent is not null && _clock > _contrabandEnds)
            EndContraband(looted: false);

        foreach (var a in _actors.Values.ToList())
        {
            switch (a.Kind)
            {
                case Kind.Truck when _rules.Truck is { } t: UpdateRobbable(a, dt, t.ThreatSeconds, blockable: true); break;
                case Kind.Collector when _rules.Collector is { } c: UpdateRobbable(a, dt, c.ThreatSeconds, blockable: false); break;
                case Kind.Guard when _rules.Contraband is { } k: UpdateGuard(a, k); break;
            }
            if (_clock > a.LeaveAt)
                Remove(a);
        }
        UpdateShops(dt);

        if (_clock >= _nextState)
        {
            _nextState = _clock + StateInterval;
            if (_actors.Count > 0)
            {
                var ids = _actors.Keys.ToArray();
                var data = _actors.Values.SelectMany(a => new[] { a.Body.GlobalPosition.X, a.Body.GlobalPosition.Y, a.Body.GlobalPosition.Z, a.Body.Rotation.Y }).ToArray();
                foreach (var p in Multiplayer.GetPeers())
                    RpcId(p, MethodName.ReceiveState, ids, data);
            }
        }
    }

    private static Vector3 V((float X, float Z) p) => new(p.X, 0.05f, p.Z);
    private static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    private void Spawn(Actor a)
    {
        _actors[a.Id] = a;
        AddChild(a.Body);
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ShowActor, a.Id, (int)a.Kind, a.Label, a.Body.GlobalPosition, a.Body.Rotation.Y);
    }

    private void Remove(Actor a)
    {
        if (!_actors.Remove(a.Id))
            return;
        a.Body.QueueFree();
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.HideActor, a.Id);
        if (a.Kind == Kind.Truck && _network.Backend is { } api && _rules?.Truck is { } t)
            _ = api.PveUnloadAsync(_network.ServerKey, new PveUnloadRequest(a.Id, t.Supplier)); // lo que no se robó vuelve al stock
    }

    private async Task StartTruckAsync(TruckRules t, long slot)
    {
        if (_network.Backend is not { } api)
            return;
        var id = $"camion-{slot}";
        var wanted = Random.Shared.Next(t.Min, t.Max + 1);
        var loaded = await api.PveLoadAsync(_network.ServerKey, new PveLoadRequest(id, t.Supplier, t.Item, wanted));
        if (!loaded.IsSuccess || loaded.Value <= 0)
            return;
        var body = Distrito.Game.Transport.VehicleBody.Create(new VehicleView(Guid.NewGuid(), Vehicles.Truck.Code, "Reparto Moretti", null, null,
            t.Route[0].X, 0.05, t.Route[0].Z, 0));
        body.SetCargo([new StockPile(t.Item, ItemKinds.Goods, loaded.Value, 0)]);
        Spawn(new Actor { Id = id, Kind = Kind.Truck, Body = body, Label = "Reparto Moretti", Route = t.Route, Speed = t.Speed });
        GD.Print($"[DISTRITO] PVE_TRUCK sale con {loaded.Value} × {t.Item}");
        _combat.NotifyAll($"Sale el camión de reparto de Moretti con {loaded.Value} cajas. Apuntale al chofer {t.ThreatSeconds:0} segundos para que se rinda.");
    }

    private void StartCollector(CollectorRules c, long slot)
    {
        var id = $"cobrador-{slot}";
        var body = Humanoid("cobrador", "Cobrador de Valenti\n(maletín)", holdsGun: false);
        body.Position = V(c.Route[0]);
        Spawn(new Actor { Id = id, Kind = Kind.Collector, Body = body, Label = "Cobrador de Valenti", Route = c.Route, Speed = c.Speed });
        GD.Print("[DISTRITO] PVE_COLLECTOR sale a cobrar");
        _combat.NotifyAll("El cobrador de Valenti hace la ronda por el barrio con la recaudación.");
    }

    private void StartContraband(ContrabandRules k, string slot)
    {
        _contrabandEvent = $"contrabando-{slot.Replace(':', '-')}";
        _contrabandEnds = _clock + k.DurationMinutes * 60;
        var center = V(k.Place);
        _contrabandCrates = new Node3D { Position = center };
        AddChild(_contrabandCrates);
        Distrito.Game.World.StockProps.Layout(_contrabandCrates, [new StockPile("caja", ItemKinds.Goods, 8, 0)], 1.8f, 1.2f, 2);
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ShowCrates, center);
        for (var i = 0; i < k.Guards; i++)
        {
            var angle = Mathf.Tau * i / Math.Max(1, k.Guards);
            var body = Humanoid("guardia", "Guardia del contrabando", holdsGun: true);
            body.Position = center + new Vector3(Mathf.Cos(angle) * 2.5f, 0, Mathf.Sin(angle) * 2.5f);
            Spawn(new Actor { Id = $"{_contrabandEvent}-g{i}", Kind = Kind.Guard, Body = body, Label = "Guardia", Health = k.GuardHealth });
        }
        GD.Print($"[DISTRITO] PVE_CONTRABAND llega a Los Muelles ({k.Guards} guardias)");
        _combat.NotifyAll($"Llegó un cargamento de contrabando a Los Muelles, con {k.Guards} guardias armados. Dura {k.DurationMinutes} minutos.");
    }

    private void EndContraband(bool looted)
    {
        foreach (var g in _actors.Values.Where(a => a.Kind == Kind.Guard).ToList())
            Remove(g);
        _contrabandCrates?.QueueFree();
        _contrabandCrates = null;
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.HideCrates);
        if (!looted)
            _combat.NotifyAll("El contrabando se fue de Los Muelles.");
        _contrabandEvent = null;
    }

    /// <summary>Camión y cobrador: avanzan por la ruta; si alguien les apunta, frenan y se van rindiendo.</summary>
    private void UpdateRobbable(Actor a, float dt, float threatSeconds, bool blockable)
    {
        var body = a.Body;
        var aimedBy = new HashSet<long>();
        if (!a.Robbed)
            foreach (var p in _network.AllPlayers)
                if (p is { Downed: false } && p.Weapon != Weapons.Fists
                    && PveRules.Aims(N(p.GlobalPosition + Vector3.Up * 1.5f), p.AimYaw, p.AimPitch, N(body.GlobalPosition + Vector3.Up * 1.4f)))
                    aimedBy.Add(p.PeerId);

        foreach (var peer in a.Threat.Keys.ToList())
            if (!aimedBy.Contains(peer))
                a.Threat[peer] = Math.Max(0, a.Threat[peer] - dt * 2); // si deja de apuntar, se le pasa el susto
        foreach (var peer in aimedBy)
        {
            var before = a.Threat.GetValueOrDefault(peer);
            var progress = a.Threat[peer] = before + dt * (1 + (float)_combat.Skill(peer, Distrito.Shared.Progression.SkillTree.Intimidation) / 100); // Intimidación
            if ((int)progress != (int)before)
                _combat.Notify(peer, $"Amenazando… {(int)progress}/{threatSeconds:0} s");
            if (progress >= threatSeconds)
            {
                Rob(a, peer);
                break;
            }
        }

        var blocked = blockable && _network.AllPlayers.Any(p =>
            p.Vehicle.Length == 0 && p.GlobalPosition.DistanceTo(body.GlobalPosition - body.GlobalTransform.Basis.Z * (Vehicles.Truck.Length / 2 + 1.5f)) < 2.2f);
        if (aimedBy.Count > 0 || blocked || (a.Robbed && _clock < a.LeaveAt - 4))
            return; // frenado
        if (a.Next >= a.Route.Count)
        {
            a.LeaveAt = Math.Min(a.LeaveAt, _clock); // terminó la ruta
            return;
        }
        var target = V(a.Route[a.Next]);
        var to = target - body.GlobalPosition with { Y = 0.05f };
        var step = a.Speed * (a.Robbed ? 1.6f : 1f) * dt;
        if (to.Length() <= step)
        {
            body.GlobalPosition = target;
            a.Next++;
        }
        else
        {
            body.GlobalPosition += to.Normalized() * step;
            body.Rotation = new Vector3(0, Mathf.LerpAngle(body.Rotation.Y, Mathf.Atan2(-to.X, -to.Z), Math.Min(1, dt * 4)), 0);
        }
    }

    private void Rob(Actor a, long peer)
    {
        if (a.Robbed || _network.Backend is not { } api || !_network.TryGetSession(peer, out var me) || _rules is null)
            return;
        a.Robbed = true;
        a.Threat.Clear();
        a.LeaveAt = _clock + 12; // queda frenado unos segundos y se va
        if (a.Kind == Kind.Truck && _rules.Truck is { } t)
        {
            var behind = a.Body.GlobalPosition + a.Body.GlobalTransform.Basis.Z * (Vehicles.Truck.Length / 2 + 1f);
            behind.Y = 0.05f;
            var seconds = _combat.DropSecondsAt(behind);
            _ = RobTruckAsync(api, a, t, me.CharacterId, me.CharacterName, peer, behind, seconds);
        }
        else if (a.Kind == Kind.Collector && _rules.Collector is { } c)
        {
            _ = RobCashAsync(api, peer, me.CharacterId, me.CharacterName, c.Account, c.MinCents, c.MaxCents, c.Family, c.Reputation,
                "Asalto al cobrador de Valenti", $"{a.Id}:{me.CharacterId}");
        }
    }

    private async Task RobTruckAsync(DistritoApiClient api, Actor a, TruckRules t, Guid character, string name, long peer, Vector3 at, int seconds)
    {
        var result = await api.PveRobGoodsAsync(_network.ServerKey,
            new PveRobGoodsRequest(a.Id, t.Supplier, character, t.Family, t.Reputation, Guid.NewGuid(), at.X, at.Y, at.Z, seconds));
        if (!result.IsSuccess)
        {
            _combat.Notify(peer, $"Hubo un problema: {result.Error!.Message}");
            return;
        }
        _combat.Notify(peer, result.Value!.Result.Message);
        if (result.Value.Drop is { } drop)
        {
            GD.Print($"[DISTRITO] PVE_ROBBED {name} asaltó el camión de reparto");
            _combat.PlaceDrop(drop, seconds);
            if (a.Body is Distrito.Game.Transport.VehicleBody truck)
                truck.SetCargo([]);
            _combat.NotifyAll($"¡Asaltaron el camión de reparto de Moretti! La carga quedó tirada en la calle.");
            MissionEvent?.Invoke(peer, Distrito.Shared.Crews.MissionKinds.TruckJob, null);
            _economy.RefreshPeer(peer);
        }
    }

    private async Task RobCashAsync(DistritoApiClient api, long peer, Guid character, string name, string account, long min, long max,
        string family, int reputation, string reason, string requestId, string? shopCode = null)
    {
        var result = await api.PveRobCashAsync(_network.ServerKey, new PveRobCashRequest(character, account, min, max, family, reputation, reason, requestId));
        if (!result.IsSuccess)
        {
            _combat.Notify(peer, $"Hubo un problema: {result.Error!.Message}");
            return;
        }
        _combat.Notify(peer, result.Value!.Message);
        if (result.Value.Ok)
        {
            GD.Print($"[DISTRITO] PVE_ROBBED {name}: {reason} — {result.Value.Message}");
            if (shopCode is not null)
                MissionEvent?.Invoke(peer, Distrito.Shared.Crews.MissionKinds.Shop, shopCode);
            _economy.RefreshPeer(peer);
        }
    }

    /// <summary>Guardias: le tiran al jugador más cercano que vean, con su cadencia y puntería.</summary>
    private void UpdateGuard(Actor g, ContrabandRules k)
    {
        if (g.Health <= 0 || _clock < g.NextShot)
            return;
        var eye = g.Body.GlobalPosition + Vector3.Up * 1.5f;
        var target = _network.AllPlayers
            .Where(p => !p.Dead && p.Vehicle.Length == 0 && p.GlobalPosition.DistanceTo(g.Body.GlobalPosition) <= k.GuardRange
                        && _combat.ClearShot(eye, p.GlobalPosition + Vector3.Up * 1.2f))
            .OrderBy(p => p.GlobalPosition.DistanceTo(g.Body.GlobalPosition)).FirstOrDefault();
        g.NextShot = _clock + k.GuardCooldown;
        if (target is null)
            return;
        var to = target.GlobalPosition - g.Body.GlobalPosition;
        g.Body.Rotation = new Vector3(0, Mathf.Atan2(-to.X, -to.Z), 0);
        // Puntería: mejor de cerca (90 % a 3 m, 45 % al límite del alcance).
        var chance = Mathf.Lerp(0.9f, 0.45f, Mathf.Clamp(to.Length() / k.GuardRange, 0, 1));
        if (Random.Shared.NextDouble() < chance)
            _combat.NpcAttack(target, k.GuardDamage, "Un guardia del contrabando", eye);
    }

    /// <summary>Caja de los locales: apuntarle al encargado un rato (con espera entre asaltos por local).</summary>
    private void UpdateShops(float dt)
    {
        if (_rules?.ShopRobbery is not { } s)
            return;
        foreach (var code in s.Shops)
        {
            if (NpcInteractionRules.Find(code) is not { } npc)
                continue;
            var at = new Vector3(npc.Position.X, 1.4f, npc.Position.Z);
            var ready = !_shopCooldown.TryGetValue(code, out var until) || _clock >= until;
            foreach (var p in _network.AllPlayers)
            {
                var key = (p.PeerId, code);
                var aiming = p is { Downed: false } && p.Weapon != Weapons.Fists && p.Vehicle.Length == 0
                             && PveRules.Aims(N(p.GlobalPosition + Vector3.Up * 1.5f), p.AimYaw, p.AimPitch, N(at), 8f);
                if (!aiming)
                {
                    _shopThreat.Remove(key);
                    continue;
                }
                if (!ready)
                {
                    if (_clock >= _nextNotice)
                    {
                        _nextNotice = _clock + 4;
                        _combat.Notify(p.PeerId, $"{npc.Name} ya fue asaltado hace poco: la caja está vacía. Volvé en {(int)((until - _clock) / 60) + 1} minutos.");
                    }
                    continue;
                }
                var before = _shopThreat.GetValueOrDefault(key);
                var progress = _shopThreat[key] = before + dt * (1 + (float)_combat.Skill(p.PeerId, Distrito.Shared.Progression.SkillTree.Intimidation) / 100);
                if ((int)progress != (int)before)
                    _combat.Notify(p.PeerId, $"Asaltando la caja de {npc.Name}… {(int)progress}/{s.ThreatSeconds:0} s");
                if (progress < s.ThreatSeconds || _network.Backend is not { } api || !_network.TryGetSession(p.PeerId, out var me))
                    continue;
                _shopThreat.Remove(key);
                _shopCooldown[code] = _clock + s.CooldownMinutes * 60;
                var family = FamilyRules.FamilyOfNpc(code) ?? "";
                _ = RobCashAsync(api, p.PeerId, me.CharacterId, me.CharacterName, code, s.MinCents, s.MaxCents, family, s.Reputation,
                    $"Asalto a la caja de {npc.Name}", $"caja-{code}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60}:{me.CharacterId}", code);
                break;
            }
        }
    }

    // ---------- Tiros a los NPC ----------

    private (string Id, float Distance)? HitTest(Vector3 origin, Vector3 dir, float range)
    {
        (string, float)? best = null;
        foreach (var a in _actors.Values)
        {
            if (a.Kind == Kind.Guard && a.Health <= 0)
                continue;
            var (radius, bottom, top) = a.Kind == Kind.Truck ? (1.4f, 0.5f, 2.6f) : (0.45f, 0.2f, 1.7f);
            var (sc, dist) = CombatNetwork.RaySegmentDistance(origin, dir, a.Body.GlobalPosition + Vector3.Up * bottom, a.Body.GlobalPosition + Vector3.Up * top);
            if (sc >= 0 && sc <= range && dist <= radius && (best is null || sc < best.Value.Item2))
                best = (a.Id, sc);
        }
        return best;
    }

    private void OnShot(string id, PlayerCharacter shooter, int damage)
    {
        if (!_actors.TryGetValue(id, out var a))
            return;
        switch (a.Kind)
        {
            case Kind.Truck or Kind.Collector:
                // Lo balearon: se rinde enseguida con el que tiró.
                Rob(a, shooter.PeerId);
                break;
            case Kind.Guard when a.Health > 0:
                a.Health -= damage;
                if (a.Health > 0)
                    return;
                GD.Print($"[DISTRITO] PVE_GUARD_DOWN {shooter.DisplayName} venció a un guardia");
                MissionEvent?.Invoke(shooter.PeerId, Distrito.Shared.Crews.MissionKinds.Contraband, null);
                a.LeaveAt = _clock + 1;
                _combat.Notify(shooter.PeerId, "Bajaste a un guardia.");
                if (_actors.Values.Where(x => x.Kind == Kind.Guard && x != a).All(x => x.Health <= 0))
                    _ = LootContrabandAsync();
                break;
        }
    }

    private async Task LootContrabandAsync()
    {
        if (_network.Backend is not { } api || _rules?.Contraband is not { } k || _contrabandEvent is not { } eventId)
            return;
        var at = V(k.Place);
        var loot = k.Loots[Random.Shared.Next(k.Loots.Count)];
        var seconds = _combat.DropSecondsAt(at);
        var drop = await api.PveContrabandAsync(_network.ServerKey,
            new PveContrabandRequest(eventId, loot.Select(l => new PveLootItem(l.Item, l.Quantity)).ToList(), Guid.NewGuid(), at.X, at.Y, at.Z, seconds));
        EndContraband(looted: true);
        if (drop.IsSuccess && drop.Value is { } d)
        {
            GD.Print("[DISTRITO] PVE_CONTRABAND_LOOT el contrabando quedó libre");
            _combat.PlaceDrop(d, seconds);
            _combat.NotifyAll("¡Cayeron los guardias del contrabando! El cargamento quedó libre en Los Muelles: el primero que llegue se lo lleva.");
        }
    }

    // ================= Clientes =================

    private static Node3D Humanoid(string look, string label, bool holdsGun)
    {
        var root = new Node3D();
        var all = CharacterLooks.All;
        var model = CharacterModel.Build(all[(look.Length + (holdsGun ? 1 : 0)) % all.Count]);
        if (holdsGun)
            model.SetWeapon(Weapons.Pistol.Code);
        root.AddChild(model);
        root.AddChild(new Label3D
        {
            Text = label, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 30, OutlineSize = 6,
            Position = new Vector3(0, 2.25f, 0), Modulate = holdsGun ? new Color(0.95f, 0.5f, 0.4f) : new Color(0.95f, 0.85f, 0.6f),
        });
        return root;
    }

    private static Node3D BuildActor(int kind, string label) => (Kind)kind switch
    {
        Kind.Truck => Distrito.Game.Transport.VehicleBody.Create(new VehicleView(Guid.NewGuid(), Vehicles.Truck.Code, label, null, null, 0, 0, 0, 0)),
        Kind.Guard => Humanoid("guardia", "Guardia del contrabando", holdsGun: true),
        _ => Humanoid("cobrador", "Cobrador de Valenti\n(maletín)", holdsGun: false),
    };

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ShowActor(string id, int kind, string label, Vector3 position, float yaw)
    {
        if (_clientActors.ContainsKey(id))
            return;
        var node = BuildActor(kind, label);
        AddChild(node);
        node.GlobalPosition = position;
        node.Rotation = new Vector3(0, yaw, 0);
        if (node is Distrito.Game.Transport.VehicleBody truck && kind == (int)Kind.Truck)
            truck.SetCargo([new StockPile(EconomyCatalog.SparePartsItem, ItemKinds.Goods, 12, 0)]);
        _clientActors[id] = node;
        _targets[id] = (position, yaw);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void HideActor(string id)
    {
        if (_clientActors.Remove(id, out var node))
            node.QueueFree();
        _targets.Remove(id);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReceiveState(string[] ids, float[] data)
    {
        for (var i = 0; i < ids.Length && i * 4 + 3 < data.Length; i++)
            if (_clientActors.ContainsKey(ids[i]))
                _targets[ids[i]] = (new Vector3(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]), data[i * 4 + 3]);
    }

    private Node3D? _clientCrates;

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ShowCrates(Vector3 at)
    {
        _clientCrates?.QueueFree();
        _clientCrates = new Node3D { Position = at };
        AddChild(_clientCrates);
        Distrito.Game.World.StockProps.Layout(_clientCrates, [new StockPile("caja", ItemKinds.Goods, 8, 0)], 1.8f, 1.2f, 2);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void HideCrates()
    {
        _clientCrates?.QueueFree();
        _clientCrates = null;
    }

    public override void _Process(double delta)
    {
        if (Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer())
            return;
        var weight = 1f - Mathf.Exp(-10f * (float)delta);
        foreach (var (id, node) in _clientActors)
            if (_targets.TryGetValue(id, out var target))
            {
                node.GlobalPosition = node.GlobalPosition.Lerp(target.Pos, weight);
                node.Rotation = new Vector3(0, Mathf.LerpAngle(node.Rotation.Y, target.Yaw, weight), 0);
            }
    }

    /// <summary>Solo para la prueba visual: un camión, un cobrador y el contrabando con guardias cerca del jugador.</summary>
    public void Preview(Vector3 near)
    {
        ShowActor("p-camion", (int)Kind.Truck, "Reparto Moretti", near + new Vector3(-4, 0, -8), 0.5f);
        ShowActor("p-cobrador", (int)Kind.Collector, "", near + new Vector3(1.5f, 0, -5), 0.2f);
        ShowActor("p-guardia", (int)Kind.Guard, "", near + new Vector3(4, 0, -6), -0.6f);
        ShowCrates(near + new Vector3(5.5f, 0, -8));
    }
}
