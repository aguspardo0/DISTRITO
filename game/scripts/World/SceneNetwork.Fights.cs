using Distrito.Game.Combat;
using Distrito.Game.Pve;
using Distrito.Server.Core.World;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Escenas en las que uno se puede meter (aprobado 2026-11-09, ver <see cref="SceneFights"/>). Solo en el servidor:
/// cuando hay un jugador cerca, los matones de la coreografía pasan a ser NPC de verdad (PveNetwork: se les puede
/// pegar y tirar, y pegan y tiran). Al terminar, el backend da la reputación (y el regalito de la extorsión). Nunca plata.
/// </summary>
public partial class SceneNetwork
{
    private sealed class Fight
    {
        public required ActiveScene Scene;
        public required string Group;
        public string Family = "";             // extorsión: la familia que cuida la zona
        public (string Left, string Right) Sides; // tiroteo
        public readonly Dictionary<long, (int Left, int Right)> Hits = new();
        public bool Done;
    }

    private PveNetwork? _pve;
    private CombatNetwork? _combat;
    private readonly Dictionary<long, Fight> _fights = new();
    private readonly HashSet<long> _caught = new();
    private long? _contrabandScene;
    private double _fightTick;

    public void UseFights(PveNetwork pve, CombatNetwork combat)
    {
        _pve = pve;
        _combat = combat;
        pve.GoonHit += OnGoonHit;
        pve.ContrabandStarted += at =>
        {
            if (_contrabandScene is { } old)
                EndScene(old);
            _contrabandScene = Trigger("contrabando")?.Id;
        };
        pve.ContrabandEnded += () =>
        {
            if (_contrabandScene is { } id)
                EndScene(id);
            _contrabandScene = null;
        };
    }

    /// <summary>Panel de administración: empieza una escena en su lugar más cercano a <paramref name="near"/>.</summary>
    public string TriggerNear(string code, Vector3 near)
    {
        if (_config?.Escenas.FirstOrDefault(d => d.Codigo == code) is not { } def)
            return $"No hay una escena «{code}».";
        var place = def.Lugares is { Length: > 0 } list
            ? Enumerable.Range(0, list.Length).OrderBy(i => Flat(near, new Vector3(list[i][0], 0, list[i][1]))).First()
            : 0;
        return Trigger(code, place) is { } s
            ? $"Empieza «{def.Nombre}» en ({s.X:0}, {s.Z:0}), dura {s.DurationMs / 60000.0:0.#} min."
            : "No se pudo empezar la escena.";
    }

    /// <summary>
    /// Llegó un personaje nuevo al andén: si no hay un tren parado (o llegando), se pone uno que ya frenó, así baja del
    /// tren junto con los demás pasajeros.
    /// </summary>
    public void TrainAtPlatform()
    {
        if (!Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer())
            return;
        var now = NowMs();
        var current = _active.Values.Select(a => a.Scene).FirstOrDefault(s => s.Code == "tren");
        if (current is not null && current.Seconds(now) < SceneScripts.TrainDeparts - 15)
            return; // está llegando o parado: alcanza
        if (current is not null)
            EndScene(current.Id);
        Trigger("tren", 0, SceneScripts.TrainStops + 1);
    }

    /// <summary>Termina una escena ya, en todos lados.</summary>
    private void EndScene(long id)
    {
        _director?.End(id);
        _active.Remove(id);
        EndFights(id);
        _taken.Remove(id);
        foreach (var peer in Multiplayer.GetPeers())
            RpcId(peer, MethodName.ReceiveEnd, id);
    }

    private void Take(long id, int track, double at)
    {
        if (!_taken.TryGetValue(id, out var t))
            _taken[id] = t = new Dictionary<int, double>();
        t[track] = at;
    }

    private void SendTaken(long id)
    {
        if (_active.TryGetValue(id, out var a))
            foreach (var peer in Multiplayer.GetPeers())
                RpcId(peer, MethodName.ReceiveStart, Pack([a.Scene]), NowMs());
    }

    /// <summary>Cinco veces por segundo: ¿alguien se metió en una escena?</summary>
    private void CheckFights(double delta)
    {
        if (_pve is null || _combat is null || (_fightTick += delta) < 0.2)
            return;
        _fightTick = 0;
        var now = NowMs();
        var players = _network.AllPlayers.Where(p => !p.Dead).ToList();
        foreach (var (id, (scene, timeline)) in _active.ToList())
        {
            var t = scene.Seconds(now);
            var (from, to) = SceneFights.Window(scene.Code, scene.DurationMs / 1000.0);
            if (_fights.TryGetValue(id, out var fight))
            {
                UpdateFight(fight, t, to);
                continue;
            }
            if (t < from || t > to || players.Count == 0)
                continue;
            var at = new Vector3(scene.X, 0, scene.Z);
            switch (scene.Code)
            {
                case "extorsion" when players.Any(p => Flat(p.GlobalPosition, at) < SceneFights.ExtortionNear):
                case "tiroteo" when players.Any(p => Flat(p.GlobalPosition, at) < SceneFights.ShootoutNear):
                    StartFight(scene, timeline, t);
                    break;
                case "persecucion" when !_caught.Contains(id):
                    var car = SceneScripts.Pose(timeline.Tracks[0], t);
                    var catcher = players.FirstOrDefault(p => Flat(p.GlobalPosition, new Vector3(car.X, 0, car.Z)) < SceneFights.ChaseCatch);
                    if (catcher is not null)
                        Caught(scene, timeline, t, catcher);
                    break;
            }
        }
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private void StartFight(ActiveScene scene, SceneTimeline timeline, double t)
    {
        var fight = new Fight { Scene = scene, Group = $"escena-{scene.Id}" };
        if (scene.Code == "tiroteo")
            fight.Sides = SceneFights.ShootoutFamilies(scene.Seed);
        else
            fight.Family = SceneFights.ZoneFamily(scene.X, scene.Z);
        foreach (var (track, side) in SceneFights.Fighters(scene.Code, timeline))
        {
            var tr = timeline.Tracks[track];
            var pose = SceneScripts.Pose(tr, t);
            var family = side switch { < 0 => fight.Sides.Left, > 0 => fight.Sides.Right, _ => fight.Family };
            var label = scene.Code == "tiroteo" ? $"Gente de {EconomyCatalog.FamilyName(family)}" : "Matón";
            var yaw = float.IsNaN(pose.Yaw) ? (side < 0 ? Mathf.Pi / 2 : -Mathf.Pi / 2) : pose.Yaw;
            _pve!.SpawnGoon($"{fight.Group}-{track}", fight.Group, side, family, armed: scene.Code == "tiroteo",
                new Vector3(pose.X, 0.05f, pose.Z), yaw, tr.Variant, label);
            Take(scene.Id, track, -1);
        }
        _fights[scene.Id] = fight;
        SendTaken(scene.Id);
        GD.Print($"[DISTRITO] ESCENA_PELEA {scene.Code} {scene.Id}: los matones son de verdad");
    }

    private void UpdateFight(Fight f, double t, double windowEnd)
    {
        if (f.Done || _pve is null)
            return;
        var left = _pve.GoonsAlive(f.Group, -1);
        var right = _pve.GoonsAlive(f.Group, 1);
        var middle = _pve.GoonsAlive(f.Group, 0);
        if (f.Scene.Code == "extorsion")
        {
            if (middle == 0)
            {
                // El comerciante se queda en la puerta, aliviado (ya no paga).
                Take(f.Scene.Id, 0, t);
                SendTaken(f.Scene.Id);
                Finish(f, won: true);
            }
            else if (t > windowEnd + 2 && !_pve.GroupProvoked(f.Group))
                Finish(f, won: false); // nadie se metió: cobran y se van
            else if (t > windowEnd + 40)
                Finish(f, won: false);
            return;
        }
        // Tiroteo: termina cuando cae un lado entero o se acaba el tiempo de la pelea.
        if (left == 0 || right == 0 || t > windowEnd + 2)
            Finish(f, won: true);
    }

    private void Finish(Fight f, bool won)
    {
        f.Done = true;
        _pve?.ReleaseGroup(f.Group, flee: true);
        if (!won || f.Hits.Count == 0 || _network.Backend is not { } api)
            return;
        foreach (var (peer, hits) in f.Hits)
        {
            if (!_network.TryGetSession(peer, out var me))
                continue;
            if (f.Scene.Code == "extorsion")
            {
                _combat?.Notify(peer, "El comerciante: «¡Gracias, muchacho! Llévese algo, de la casa.»");
                _ = RewardAsync(api, peer, new SceneRewardRequest(me.CharacterId, f.Group, "extorsion",
                    [new(f.Family, SceneFights.ExtortionReputation)], f.Family, $"{me.CharacterName} echó a unos extorsionadores de la Calle Principal"));
            }
            else if (SceneFights.HelpedSide(hits.Left, hits.Right) is var helped and not 0)
            {
                var (friend, foe) = helped < 0 ? (f.Sides.Left, f.Sides.Right) : (f.Sides.Right, f.Sides.Left);
                _ = RewardAsync(api, peer, new SceneRewardRequest(me.CharacterId, f.Group, "tiroteo",
                    [new(friend, SceneFights.ShootoutReputation), new(foe, -SceneFights.ShootoutReputation)], null,
                    $"{me.CharacterName} se metió en un tiroteo del lado de los {EconomyCatalog.FamilyName(friend)}"));
            }
        }
    }

    private void Caught(ActiveScene scene, SceneTimeline timeline, double t, Player.PlayerCharacter catcher)
    {
        _caught.Add(scene.Id);
        Take(scene.Id, 0, t);
        Take(scene.Id, 1, Math.Min(t + 2.5, scene.DurationMs / 1000.0)); // el patrullero llega atrás
        SendTaken(scene.Id);
        _combat?.NotifyAll($"¡{catcher.DisplayName} alcanzó al auto que se escapaba de la policía!");
        GD.Print($"[DISTRITO] ESCENA_PERSECUCION {catcher.DisplayName} alcanzó el auto");
        if (_network.Backend is { } api && _network.TryGetSession(catcher.PeerId, out var me))
            _ = RewardAsync(api, catcher.PeerId, new SceneRewardRequest(me.CharacterId, $"escena-{scene.Id}", "persecucion",
                SceneFights.Families.Select(f => new ReputationDelta(f, SceneFights.ChaseReputation)).ToList(), null,
                $"{me.CharacterName} alcanzó al auto que se escapaba de la policía por la Calle Principal"));
    }

    private async Task RewardAsync(Distrito.Shared.Api.DistritoApiClient api, long peer, SceneRewardRequest request)
    {
        var result = await api.SceneRewardAsync(_network.ServerKey, request);
        if (!result.IsSuccess)
        {
            GD.PushWarning($"[DISTRITO] premio de escena: {result.Error!.Message}");
            return;
        }
        GD.Print($"[DISTRITO] ESCENA_PREMIO {request.Kind}: {result.Value!.Message}");
        _combat?.Notify(peer, result.Value.Message);
        EconomyRefresh?.Invoke(peer);
    }

    /// <summary>Lo pone Main: refrescar la reputación y el inventario del jugador después del premio.</summary>
    public Action<long>? EconomyRefresh { get; set; }

    private void OnGoonHit(string group, int side, long peer)
    {
        if (_fights.Values.FirstOrDefault(f => f.Group == group) is not { } f)
            return;
        var h = f.Hits.GetValueOrDefault(peer);
        f.Hits[peer] = side < 0 ? (h.Left + 1, h.Right) : (h.Left, h.Right + 1);
    }

    private void EndFights(long id)
    {
        _caught.Remove(id);
        if (_fights.Remove(id, out var f) && !f.Done)
            _pve?.ReleaseGroup(f.Group, flee: true);
    }
}
