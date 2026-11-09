using Distrito.Game.Player;
using Distrito.Server.Core.Combat;
using Godot;

namespace Distrito.Game.Pve;

/// <summary>
/// Matones de las escenas de la ciudad (extorsión y tiroteo, ver SceneNetwork.Fights). Con arma (tiroteo) le tiran a
/// los del otro lado y a quien les haya tirado; sin arma (extorsión) se le van encima a las piñas al que se metió.
/// Al caer quedan tirados hasta que la escena los suelta.
/// </summary>
public partial class PveNetwork
{
    private const int GoonHealth = 60, BruteHealth = 40;
    private const float GoonRange = 24f, BruteChase = 30f;

    /// <summary>Servidor: un jugador le pegó a un matón (escena, lado, jugador).</summary>
    public event Action<string, int, long>? GoonHit;

    private readonly Dictionary<string, CharacterModel?> _models = new();
    private readonly Dictionary<string, Vector3> _lastSeen = new();

    public void SpawnGoon(string id, string group, int side, string family, bool armed, Vector3 at, float yaw, int variant, string label)
    {
        if (_actors.ContainsKey(id))
            return;
        var kind = armed ? Kind.Goon : Kind.Brute;
        var text = $"{variant}|{label}";
        var body = GoonBody(text, armed);
        body.Position = at;
        body.Rotation = new Vector3(0, yaw, 0);
        Spawn(new Actor
        {
            Id = id, Kind = kind, Body = body, Label = text, Group = group, Side = side, Family = family,
            Health = armed ? GoonHealth : BruteHealth, NextShot = _clock + 0.5 + Random.Shared.NextDouble(),
        });
    }

    public int GoonsAlive(string group, int side) => _actors.Values.Count(a => a.Group == group && a.Side == side && !a.Down && a.LeaveAt == double.MaxValue);

    public bool GroupProvoked(string group) => _actors.Values.Any(a => a.Group == group && a.Provoked.Count > 0);

    /// <summary>Termina la pelea: los que quedan en pie se van (corriendo si hubo lío) y los caídos desaparecen al rato.</summary>
    public void ReleaseGroup(string group, bool flee)
    {
        var members = _actors.Values.Where(a => a.Group == group).ToList();
        if (members.Count == 0)
            return;
        var trouble = members.Any(a => a.Provoked.Count > 0 || a.Down);
        var center = members.Aggregate(Vector3.Zero, (s, a) => s + a.Body.GlobalPosition) / members.Count;
        foreach (var a in members)
        {
            if (a.Down)
            {
                a.LeaveAt = _clock + 20;
                continue;
            }
            var away = a.Body.GlobalPosition - center with { Y = a.Body.GlobalPosition.Y };
            if (away.LengthSquared() < 0.01f)
                away = new Vector3(a.Side == 0 ? 1 : a.Side, 0, 0);
            away.Y = 0;
            a.Flee = away.Normalized() * (trouble && flee ? 5.5f : 1.6f);
            a.Provoked.Clear();
            a.LeaveAt = _clock + (trouble ? 8 : 14);
        }
    }

    private void UpdateGoon(Actor a, float dt)
    {
        if (a.Down)
            return;
        var body = a.Body;
        if (a.LeaveAt != double.MaxValue)
        {
            body.GlobalPosition += a.Flee * dt;
            body.Rotation = new Vector3(0, Mathf.Atan2(-a.Flee.X, -a.Flee.Z), 0);
            return;
        }
        var target = _network.AllPlayers
            .Where(p => a.Provoked.Contains(p.PeerId) && !p.Dead && !p.Downed && p.Vehicle.Length == 0
                        && p.GlobalPosition.DistanceTo(body.GlobalPosition) <= (a.Kind == Kind.Goon ? GoonRange : BruteChase))
            .OrderBy(p => p.GlobalPosition.DistanceTo(body.GlobalPosition)).FirstOrDefault();

        if (a.Kind == Kind.Brute)
        {
            if (target is null)
                return;
            var to = target.GlobalPosition - body.GlobalPosition with { Y = target.GlobalPosition.Y };
            to.Y = 0;
            Face(body, to);
            if (to.Length() > 1.3f)
                body.GlobalPosition += to.Normalized() * Math.Min(3.6f * dt, to.Length() - 1.2f);
            if (to.Length() <= 1.7f && _clock >= a.NextShot)
            {
                a.NextShot = _clock + 1.1;
                Anim(a.Id, "pina");
                _combat.NpcMelee(target, 12, "Un matón");
            }
            return;
        }

        // Con arma: al jugador que les tiró, si no a los del otro lado.
        if (_clock < a.NextShot)
            return;
        a.NextShot = _clock + 1.2 + Random.Shared.NextDouble() * 0.7;
        var eye = body.GlobalPosition + Vector3.Up * 1.5f;
        if (target is not null && _combat.ClearShot(eye, target.GlobalPosition + Vector3.Up * 1.2f))
        {
            var to = target.GlobalPosition - body.GlobalPosition;
            Face(body, to);
            Anim(a.Id, "tiro");
            var chance = Mathf.Lerp(0.75f, 0.3f, Mathf.Clamp(to.Length() / GoonRange, 0, 1));
            if (Random.Shared.NextDouble() < chance)
                _combat.NpcAttack(target, 18, $"La gente de {Distrito.Shared.Economy.EconomyCatalog.FamilyName(a.Family)}", eye);
            else
                _combat.NpcShotFx(eye, target.GlobalPosition + new Vector3((float)Random.Shared.NextDouble() * 2 - 1, 1.2f, (float)Random.Shared.NextDouble() * 2 - 1));
            return;
        }
        var enemy = _actors.Values
            .Where(e => e.Group == a.Group && e.Side == -a.Side && !e.Down && e.LeaveAt == double.MaxValue)
            .OrderBy(e => e.Body.GlobalPosition.DistanceTo(body.GlobalPosition)).FirstOrDefault();
        if (enemy is null)
            return;
        Face(body, enemy.Body.GlobalPosition - body.GlobalPosition);
        Anim(a.Id, "tiro");
        // Entre ellos se tiran mucho y se pegan poco: la pelea dura lo que dura la escena, si nadie se mete.
        _combat.NpcShotFx(eye, enemy.Body.GlobalPosition + Vector3.Up * 1.1f + new Vector3(0, (float)Random.Shared.NextDouble() - 0.5f, 0));
        if (Random.Shared.NextDouble() < 0.12)
        {
            enemy.Health -= 20;
            if (enemy.Health <= 0)
                DownGoon(enemy);
        }
    }

    private static void Face(Node3D body, Vector3 to)
    {
        if (to.X * to.X + to.Z * to.Z > 0.0001f)
            body.Rotation = new Vector3(0, Mathf.Atan2(-to.X, -to.Z), 0);
    }

    private void HurtGoon(Actor a, PlayerCharacter by, int damage)
    {
        if (a.Down || a.LeaveAt != double.MaxValue)
            return;
        a.Health -= damage;
        GoonHit?.Invoke(a.Group, a.Side, by.PeerId);
        // Le pegaste a uno: los de su lado te tienen bronca.
        foreach (var mate in _actors.Values.Where(m => m.Group == a.Group && m.Side == a.Side))
            mate.Provoked.Add(by.PeerId);
        if (a.Health > 0)
            return;
        DownGoon(a);
        _combat.Notify(by.PeerId, a.Kind == Kind.Brute ? "Dejaste tirado a un matón." : $"Bajaste a uno de {Distrito.Shared.Economy.EconomyCatalog.FamilyName(a.Family)}.");
    }

    private void DownGoon(Actor a)
    {
        a.Down = true;
        _combat.ReportIncident(Distrito.Shared.Life.IncidentKinds.Wounded, a.Body.GlobalPosition);
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ActorDown, a.Id);
        ApplyDown(a.Id, server: true);
    }

    /// <summary>Piña de un jugador: al matón que tenga adelante, a mano.</summary>
    private bool PunchGoon(PlayerCharacter attacker, int damage)
    {
        var target = _actors.Values
            .Where(a => a.Kind is Kind.Goon or Kind.Brute && !a.Down
                        && CombatRules.InReach(N(attacker.GlobalPosition), attacker.GlobalRotation.Y, N(a.Body.GlobalPosition)))
            .OrderBy(a => a.Body.GlobalPosition.DistanceTo(attacker.GlobalPosition)).FirstOrDefault();
        if (target is null)
            return false;
        HurtGoon(target, attacker, damage);
        return true;
    }

    private void Anim(string id, string what)
    {
        foreach (var p in Multiplayer.GetPeers())
            RpcId(p, MethodName.ActorAnim, id, what);
    }

    private static Node3D GoonBody(string text, bool armed)
    {
        var parts = text.Split('|', 2);
        var variant = int.TryParse(parts[0], out var v) ? v : 0;
        var label = parts.Length > 1 ? parts[1] : "";
        var root = Humanoid("maton", label, holdsGun: armed, exact: Distrito.Game.World.SceneNetwork.Look("maton", variant));
        return root;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ActorDown(string id) => ApplyDown(id, server: false);

    private void ApplyDown(string id, bool server)
    {
        var node = server ? _actors.GetValueOrDefault(id)?.Body : _clientActors.GetValueOrDefault(id);
        if (node is null)
            return;
        foreach (var model in node.FindChildren("*", "Node3D", false, false).OfType<CharacterModel>())
            model.SetDowned(true);
        foreach (var label in node.FindChildren("*", "Label3D", false, false).OfType<Label3D>())
            label.Visible = false;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ActorAnim(string id, string what)
    {
        if (!_clientActors.TryGetValue(id, out var node) || ModelOf(id, node) is not { } model)
            return;
        if (what == "tiro")
            model.Recoil(); // el fogonazo y el ruido los manda CombatNetwork
        else
        {
            model.Punch();
            Distrito.Game.Audio.SoundPlayer.At(Distrito.Game.Audio.Sfx.Punch, node.GlobalPosition + Vector3.Up * 1.3f, -4, Distrito.Game.Audio.SoundPlayer.Vary(), 30);
        }
    }

    private CharacterModel? ModelOf(string id, Node3D node)
    {
        if (!_models.TryGetValue(id, out var model) || model is not null && !IsInstanceValid(model))
            _models[id] = model = node.FindChildren("*", "Node3D", false, false).OfType<CharacterModel>().FirstOrDefault();
        return model;
    }

    /// <summary>Cliente: que caminen las piernas cuando el matón se mueve.</summary>
    private void AnimateClientActors(float delta)
    {
        if (delta <= 0)
            return;
        foreach (var (id, node) in _clientActors)
        {
            if (ModelOf(id, node) is not { } model)
                continue;
            var pos = node.GlobalPosition;
            var speed = _lastSeen.TryGetValue(id, out var last) ? new Vector2(pos.X - last.X, pos.Z - last.Z).Length() / delta : 0;
            _lastSeen[id] = pos;
            model.Animate(speed, delta);
        }
    }
}
