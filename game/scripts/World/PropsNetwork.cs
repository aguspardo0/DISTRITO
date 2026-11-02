using Distrito.Game.Network;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Física compartida (pedido del equipo, 2026-11-02): cajones, barriles, bolsas, sillas, botellas, cubiertas y tachos que
/// se empujan caminando, se atropellan con el auto y se caen. Los simula el servidor (Jolt) y los ve todo el mundo igual:
/// el servidor manda la posición de los que se están moviendo (unas 12 veces por segundo) a quien esté cerca, y los
/// clientes suavizan el movimiento. Los que quedan quietos no gastan red. Si alguien los deja tirados, a los 10 minutos
/// vuelven a su lugar.
/// </summary>
public partial class PropsNetwork : Node3D
{
    /// <summary>Capa de colisión de los objetos (la predicción local no choca con ellos: el empujón lo decide el servidor).</summary>
    public const uint PropLayer = 4;

    private const double SendInterval = 1.0 / 12;
    private const float SendRadius = 80f;
    private const double ReturnHomeSeconds = 600;

    private sealed record Spec(Vector3 Size, float Mass, bool Round, Color Color);

    private static readonly Dictionary<string, Spec> Specs = new()
    {
        ["cajon"] = new(new Vector3(0.6f, 0.6f, 0.6f), 12f, false, new Color(0.55f, 0.4f, 0.22f)),
        ["caja"] = new(new Vector3(0.5f, 0.4f, 0.4f), 3f, false, new Color(0.68f, 0.55f, 0.36f)),
        ["bolsa"] = new(new Vector3(0.6f, 0.3f, 0.4f), 15f, false, new Color(0.78f, 0.7f, 0.52f)),
        ["silla"] = new(new Vector3(0.45f, 0.9f, 0.45f), 5f, false, new Color(0.35f, 0.22f, 0.12f)),
        ["barril"] = new(new Vector3(0.6f, 0.9f, 0.6f), 25f, true, new Color(0.4f, 0.27f, 0.15f)),
        ["botella"] = new(new Vector3(0.08f, 0.28f, 0.08f), 0.4f, true, new Color(0.25f, 0.5f, 0.3f)),
        ["neumatico"] = new(new Vector3(0.7f, 0.22f, 0.7f), 9f, true, new Color(0.08f, 0.08f, 0.08f)),
        ["tacho"] = new(new Vector3(0.55f, 0.8f, 0.55f), 6f, true, new Color(0.45f, 0.47f, 0.48f)),
    };

    private sealed class Prop
    {
        public required string Type;
        public required Transform3D Home;
        public RigidBody3D? Body;          // servidor
        public Node3D? Visual;             // cliente
        public Transform3D Target;         // cliente: lo último que mandó el servidor
        public Transform3D LastSent;
        public double LastMoved;
    }

    private readonly List<Prop> _props = new();
    private GameNetwork _network = null!;
    private bool _server;
    private double _clock, _nextSend;

    public void Initialize(GameNetwork network)
    {
        _network = network;
        _network.PlayerAdmitted += (peer, _) => { if (_server) RpcId(peer, MethodName.ReceiveProps, Pack(_props.Select((p, i) => i), all: true)); };
    }

    public override void _Ready()
    {
        Name = "Props";
        var list = new List<(PhysicsProp Prop, Vector3 At)>();
        foreach (var p in InteriorLayout.MarketProps)
            list.Add((p, new Vector3(p.X, p.Y, p.Z)));
        foreach (var plan in InteriorLayout.All)
            foreach (var p in plan.Props)
            {
                var w = plan.ToWorld(p.X, p.Z);
                list.Add((p, new Vector3(w.X, p.Y, w.Y)));
            }
        foreach (var (p, at) in list)
        {
            var spec = Specs.GetValueOrDefault(p.Type, Specs["caja"]);
            var lift = spec.Size.Y / 2 + 0.02f;
            var home = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(p.Rotation)), at + new Vector3(0, lift, 0));
            _props.Add(new Prop { Type = p.Type, Home = home, Target = home, LastSent = home });
        }
    }

    private void Spawn(bool server)
    {
        _server = server;
        for (var i = 0; i < _props.Count; i++)
        {
            var prop = _props[i];
            var spec = Specs.GetValueOrDefault(prop.Type, Specs["caja"]);
            var mesh = Mesh(prop.Type, spec);
            if (server)
            {
                var body = new RigidBody3D
                {
                    Name = $"Prop{i}", Mass = spec.Mass, CollisionLayer = PropLayer, CollisionMask = 1u | PropLayer, CanSleep = true,
                    ContinuousCd = spec.Mass < 1f, LinearDamp = 0.3f, AngularDamp = 0.6f,
                    PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f, Bounce = prop.Type == "neumatico" ? 0.35f : 0.1f },
                };
                body.AddChild(new CollisionShape3D { Shape = Shape(spec), Rotation = prop.Type == "neumatico" ? Vector3.Zero : Vector3.Zero });
                body.AddChild(mesh);
                AddChild(body);
                body.GlobalTransform = prop.Home;
                body.Sleeping = true;
                prop.Body = body;
            }
            else
            {
                var visual = new Node3D { Name = $"Prop{i}" };
                visual.AddChild(mesh);
                AddChild(visual);
                visual.GlobalTransform = prop.Home;
                prop.Visual = visual;
            }
        }
    }

    private static Shape3D Shape(Spec spec) => spec.Round
        ? new CylinderShape3D { Radius = spec.Size.X / 2, Height = spec.Size.Y }
        : new BoxShape3D { Size = spec.Size };

    private static Node3D Mesh(string type, Spec spec)
    {
        var root = new Node3D();
        var mat = InteriorBuilder.Mat(spec.Color, type == "botella" ? 0.1f : 0.8f, type is "tacho" ? 0.5f : 0f);
        var s = spec.Size;
        switch (type)
        {
            case "silla":
                InteriorBuilder.Box(root, new Vector3(s.X, 0.05f, s.Z), new Vector3(0, -0.02f, 0), mat);
                InteriorBuilder.Box(root, new Vector3(s.X, 0.45f, 0.05f), new Vector3(0, 0.22f, -s.Z / 2 + 0.03f), mat);
                foreach (var x in new[] { -s.X / 2 + 0.04f, s.X / 2 - 0.04f })
                    foreach (var z in new[] { -s.Z / 2 + 0.04f, s.Z / 2 - 0.04f })
                        InteriorBuilder.Box(root, new Vector3(0.04f, 0.43f, 0.04f), new Vector3(x, -0.25f, z), mat);
                break;
            case "cajon":
                InteriorBuilder.Box(root, s * 0.98f, Vector3.Zero, mat);
                foreach (var y in new[] { -0.15f, 0.15f })
                    InteriorBuilder.Box(root, new Vector3(s.X + 0.01f, 0.06f, s.Z + 0.01f), new Vector3(0, y, 0), InteriorBuilder.Mat(new Color(0.42f, 0.3f, 0.16f)));
                break;
            case "barril":
                InteriorBuilder.Cylinder(root, s.X / 2, s.Y, Vector3.Zero, mat);
                foreach (var y in new[] { -0.3f, 0.3f })
                    InteriorBuilder.Cylinder(root, s.X / 2 + 0.01f, 0.05f, new Vector3(0, y, 0), InteriorBuilder.Mat(new Color(0.3f, 0.3f, 0.32f), 0.5f, 0.6f));
                break;
            case "neumatico":
                InteriorBuilder.Cylinder(root, s.X / 2, s.Y, Vector3.Zero, mat);
                InteriorBuilder.Cylinder(root, s.X / 4, s.Y + 0.01f, Vector3.Zero, InteriorBuilder.Mat(new Color(0.55f, 0.55f, 0.55f), 0.4f, 0.7f));
                break;
            default:
                if (spec.Round)
                    InteriorBuilder.Cylinder(root, s.X / 2, s.Y, Vector3.Zero, mat);
                else
                    InteriorBuilder.Box(root, s, Vector3.Zero, mat);
                break;
        }
        return root;
    }

    /// <summary>
    /// Servidor: quien camina (o maneja) empuja los objetos con los que choca, según su velocidad. Sin empujar hacia abajo
    /// y con tope, para que una botella no salga volando como una bala.
    /// </summary>
    public static void PushFromSlides(CharacterBody3D body, float speed, float strength)
    {
        if (speed < 0.2f)
            return;
        for (var i = 0; i < body.GetSlideCollisionCount(); i++)
        {
            var hit = body.GetSlideCollision(i);
            if (hit.GetCollider() is not RigidBody3D rb)
                continue;
            var push = -hit.GetNormal();
            push.Y = 0;
            if (push.LengthSquared() < 0.01f)
                continue;
            var impulse = push.Normalized() * Mathf.Min(strength * (0.25f + speed * 0.12f), rb.Mass * speed * 1.2f);
            rb.ApplyImpulse(impulse, hit.GetPosition() - rb.GlobalPosition + new Vector3(0, 0.1f, 0));
        }
    }

    private bool _spawned;

    public override void _Process(double delta)
    {
        // Se arman cuando ya hay partida (servidor: cuerpos con física; cliente: solo lo que se ve). Al salir se desarman.
        var peer = Multiplayer.MultiplayerPeer;
        var online = peer is not null && peer is not OfflineMultiplayerPeer && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
        if (online && !_spawned)
        {
            Spawn(Multiplayer.IsServer());
            _spawned = true;
        }
        else if (!online && _spawned)
        {
            foreach (var child in GetChildren()) child.QueueFree();
            foreach (var p in _props) { p.Body = null; p.Visual = null; p.Target = p.Home; p.LastSent = p.Home; }
            _spawned = false;
            _server = false;
        }
        _clock += delta;
        if (!_spawned)
            return;
        if (!_server)
        {
            // Cliente: se acerca suave a lo último que mandó el servidor.
            var weight = 1f - Mathf.Exp(-14f * (float)delta);
            foreach (var p in _props)
                if (p.Visual is { } v && !v.GlobalTransform.IsEqualApprox(p.Target))
                    v.GlobalTransform = v.GlobalTransform.InterpolateWith(p.Target, weight);
            return;
        }
        if (Multiplayer.MultiplayerPeer is null || _clock < _nextSend)
            return;
        _nextSend = _clock + SendInterval;

        var moved = new List<int>();
        for (var i = 0; i < _props.Count; i++)
        {
            var p = _props[i];
            if (p.Body is not { } b)
                continue;
            var t = b.GlobalTransform;
            // Se cayó del mundo o lo dejaron tirado mucho tiempo: vuelve a su lugar.
            if (t.Origin.Y < -10 || (!b.Sleeping ? false : _clock - p.LastMoved > ReturnHomeSeconds && t.Origin.DistanceTo(p.Home.Origin) > 0.5f))
            {
                b.GlobalTransform = p.Home;
                b.LinearVelocity = Vector3.Zero;
                b.AngularVelocity = Vector3.Zero;
                b.Sleeping = true;
                t = p.Home;
            }
            if (t.Origin.DistanceSquaredTo(p.LastSent.Origin) > 0.0001f || !t.Basis.IsEqualApprox(p.LastSent.Basis))
            {
                p.LastSent = t;
                p.LastMoved = _clock;
                moved.Add(i);
            }
        }
        if (moved.Count == 0)
            return;
        // A cada jugador, solo los que tiene cerca.
        foreach (var (peer, _) in _network.AuthenticatedPlayers)
        {
            if (_network.GetPlayer(peer) is not { } player || !Multiplayer.GetPeers().Contains((int)peer))
                continue;
            var near = moved.Where(i => _props[i].LastSent.Origin.DistanceTo(player.GlobalPosition) <= SendRadius).ToList();
            if (near.Count > 0)
                RpcId(peer, MethodName.ReceiveMoves, Pack(near, all: false));
        }
    }

    private float[] Pack(IEnumerable<int> ids, bool all)
    {
        var data = new List<float>();
        foreach (var i in ids)
        {
            var t = _props[i].Body?.GlobalTransform ?? _props[i].Target;
            var q = t.Basis.GetRotationQuaternion();
            data.AddRange([i, t.Origin.X, t.Origin.Y, t.Origin.Z, q.X, q.Y, q.Z, q.W]);
        }
        return data.ToArray();
    }

    private void Unpack(float[] data, bool snap)
    {
        for (var k = 0; k + 7 < data.Length; k += 8)
        {
            var i = (int)data[k];
            if (i < 0 || i >= _props.Count)
                continue;
            var target = new Transform3D(new Basis(new Quaternion(data[k + 4], data[k + 5], data[k + 6], data[k + 7]).Normalized()),
                new Vector3(data[k + 1], data[k + 2], data[k + 3]));
            _props[i].Target = target;
            if (snap && _props[i].Visual is { } v)
                v.GlobalTransform = target;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReceiveMoves(float[] data) => Unpack(data, snap: false);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveProps(float[] data) => Unpack(data, snap: true);
}
