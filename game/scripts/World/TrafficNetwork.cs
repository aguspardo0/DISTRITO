using Distrito.Game.Network;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Autos que circulan (ver <see cref="TrafficSim"/>). El servidor los mueve y frena por la gente; a cada jugador le
/// manda, diez veces por segundo, los que tiene cerca. Todos tienen colisión (en el servidor y en los equipos), así no
/// se los atraviesa. Durante la feria la Calle Principal se corta y esos autos quedan estacionados contra el cordón.
/// </summary>
public partial class TrafficNetwork : Node
{
    private const float SendRadius = 160f;

    private GameNetwork _network = null!;
    private readonly TrafficSim _sim = new();
    private readonly List<AnimatableBody3D> _bodies = new();
    private readonly Dictionary<int, (Vector3 Pos, float Yaw)> _target = new();
    private double _send;

    public void Initialize(GameNetwork network) => _network = network;

    public override void _Ready()
    {
        Name = "Traffic";
        Color[] paints =
        [
            new(0.55f, 0.12f, 0.1f), new(0.15f, 0.2f, 0.35f), new(0.75f, 0.7f, 0.55f), new(0.2f, 0.3f, 0.22f), new(0.12f, 0.12f, 0.12f),
            new(0.6f, 0.45f, 0.2f), new(0.35f, 0.5f, 0.55f), new(0.45f, 0.18f, 0.3f),
        ];
        var type = Distrito.Shared.Economy.Vehicles.Van;
        foreach (var car in _sim.Cars)
        {
            // Escondidos y lejos hasta que el servidor los ubica (así no hay colisiones donde no se ven).
            var body = new AnimatableBody3D { Name = $"Auto{car.Paint}", SyncToPhysics = false, Visible = false, Position = new Vector3(0, -50, 0) };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(type.Width, 1.5f, type.Length) }, Position = new Vector3(0, 0.85f, 0) });
            if (DisplayServer.GetName() != "headless")
                body.AddChild(Distrito.Game.Transport.VehicleBody.Visual(type, paints[car.Paint % paints.Length], car.Paint % 5 == 3 ? "funebre" : "sedan"));
            AddChild(body);
            _bodies.Add(body);
        }
    }

    private static void Put(Node3D body, System.Numerics.Vector2 at, System.Numerics.Vector2 dir) =>
        body.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(-dir.X, -dir.Y)), new Vector3(at.X, 0, at.Y));

    public override void _PhysicsProcess(double delta)
    {
        if (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer())
        {
            var people = _network.AllPlayers.Select(p => new System.Numerics.Vector2(p.GlobalPosition.X, p.GlobalPosition.Z)).ToList();
            var closed = SceneNetwork.Instance?.Active.Any(s => s.Code == "feria") == true ? new HashSet<int> { 0 } : null;
            _sim.Step((float)delta, people, closed);
            for (var i = 0; i < _sim.Cars.Count; i++)
            {
                var car = _sim.Cars[i];
                var dir = car.Heading;
                var at = car.Parked ? car.Position + new System.Numerics.Vector2(-dir.Y, dir.X) * 1.4f : car.Position; // estacionado: contra el cordón
                Put(_bodies[i], at, dir);
                _bodies[i].Visible = true;
            }
            if ((_send += delta) >= 0.1)
            {
                _send = 0;
                foreach (var peer in Multiplayer.GetPeers())
                {
                    if (_network.GetPlayer(peer) is not { } player)
                        continue;
                    var here = player.GlobalPosition;
                    var data = new List<float>();
                    for (var i = 0; i < _bodies.Count; i++)
                    {
                        var t = _bodies[i].GlobalTransform;
                        if (t.Origin.DistanceTo(here) > SendRadius)
                            continue;
                        data.AddRange([i, t.Origin.X, t.Origin.Z, _bodies[i].Rotation.Y]);
                    }
                    RpcId(peer, MethodName.ReceiveCars, data.ToArray());
                }
            }
            return;
        }
        // Equipo: se acerca a lo último que mandó el servidor (suave) y esconde los que quedaron lejos.
        foreach (var (i, (pos, yaw)) in _target)
        {
            var body = _bodies[i];
            var k = 1 - Mathf.Exp(-12 * (float)delta);
            body.Position = body.Visible ? body.Position.Lerp(pos, k) : pos;
            body.Rotation = new Vector3(0, body.Visible ? Mathf.LerpAngle(body.Rotation.Y, yaw, k) : yaw, 0);
            body.Visible = true;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReceiveCars(float[] data)
    {
        var seen = new HashSet<int>();
        for (var j = 0; j + 3 < data.Length; j += 4)
        {
            var i = (int)data[j];
            if (i < 0 || i >= _bodies.Count)
                continue;
            seen.Add(i);
            _target[i] = (new Vector3(data[j + 1], 0, data[j + 2]), data[j + 3]);
        }
        foreach (var i in _target.Keys.Except(seen).ToList())
        {
            _target.Remove(i);
            _bodies[i].Visible = false;
            _bodies[i].Position = new Vector3(0, -50, 0); // lejos: sin colisión donde no se ve
        }
    }
}
