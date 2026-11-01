using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Transport;

/// <summary>
/// Un vehículo en el mundo. En el servidor simula el manejo (arcade) con su conductor; en los clientes se suaviza hacia
/// el último estado recibido. El cuerpo choca con personajes y edificios en los dos lados.
/// </summary>
public partial class VehicleBody : CharacterBody3D
{
    private const float Gravity = 9.8f;

    public Guid Id { get; private set; }
    public VehicleType Type { get; private set; } = Vehicles.Pickup;
    public string OwnerName { get; private set; } = "";
    public long DriverPeer { get; set; }

    // Servidor: estado del manejo.
    public float Speed { get; private set; }
    private float _throttle, _steer;
    private bool _brake;
    private double _lastInput;
    private double _clock;

    // Cliente: hacia dónde suavizar.
    private Vector3 _targetPosition;
    private float _targetYaw;
    private Label3D _label = null!;

    public static VehicleBody Create(VehicleView view)
    {
        var type = Vehicles.Find(view.Kind) ?? Vehicles.Pickup;
        var body = new VehicleBody { Name = $"Vehicle_{view.Id:N}", Id = view.Id, Type = type, OwnerName = view.OwnerName };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(type.Width, type.Height * 0.8f, type.Length) },
            Position = new Vector3(0, type.Height * 0.45f, 0),
        });
        body.AddChild(BuildModel(type));
        body._label = new Label3D
        {
            Text = $"{type.Name}\n{view.OwnerName}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 36, OutlineSize = 8, Position = new Vector3(0, type.Height + 0.7f, 0),
            Modulate = new Color(0.95f, 0.85f, 0.6f),
        };
        body.AddChild(body._label);
        body.Position = new Vector3((float)view.X, (float)view.Y, (float)view.Z);
        body.Rotation = new Vector3(0, (float)view.Yaw, 0);
        body._targetPosition = body.Position;
        body._targetYaw = (float)view.Yaw;
        return body;
    }

    /// <summary>Modelo procedural de época: chasis, cabina, caja de carga, guardabarros y ruedas.</summary>
    private static Node3D BuildModel(VehicleType type)
    {
        var model = new Node3D { Name = "Model" };
        var truck = type.Code == Vehicles.Truck.Code;
        var paint = new StandardMaterial3D { AlbedoColor = truck ? new Color(0.22f, 0.27f, 0.22f) : new Color(0.32f, 0.12f, 0.1f), Roughness = 0.6f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.08f) };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.3f, 0.18f) };
        var chrome = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.72f, 0.65f), Metallic = 0.8f, Roughness = 0.3f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.3f, 0.33f) };
        float l = type.Length, w = type.Width, h = type.Height;
        void Box(Vector3 size, Vector3 at, Material mat) =>
            model.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = at });

        Box(new Vector3(w * 0.9f, 0.2f, l * 0.95f), new Vector3(0, 0.55f, 0), dark);                        // chasis
        Box(new Vector3(w * 0.7f, h * 0.28f, l * 0.28f), new Vector3(0, 0.85f, -l * 0.33f), paint);        // capó
        Box(new Vector3(w * 0.25f, h * 0.22f, 0.08f), new Vector3(0, 0.85f, -l * 0.48f), chrome);          // parrilla
        Box(new Vector3(w * 0.92f, h * 0.45f, l * 0.22f), new Vector3(0, 1.2f, -l * 0.1f), paint);         // cabina
        Box(new Vector3(w * 0.8f, h * 0.18f, 0.05f), new Vector3(0, 1.35f, -l * 0.215f), glass);           // parabrisas
        if (truck)
            Box(new Vector3(w, h * 0.55f, l * 0.55f), new Vector3(0, 1.35f, l * 0.22f), wood);              // caja cerrada
        else
        {
            Box(new Vector3(w * 0.95f, 0.08f, l * 0.45f), new Vector3(0, 0.72f, l * 0.25f), wood);          // caja abierta
            Box(new Vector3(0.08f, 0.35f, l * 0.45f), new Vector3(w * 0.45f, 0.9f, l * 0.25f), wood);
            Box(new Vector3(0.08f, 0.35f, l * 0.45f), new Vector3(-w * 0.45f, 0.9f, l * 0.25f), wood);
            Box(new Vector3(w * 0.95f, 0.35f, 0.08f), new Vector3(0, 0.9f, l * 0.47f), wood);
        }
        foreach (var side in new[] { -1f, 1f })
            foreach (var z in new[] { -l * 0.32f, l * 0.3f })
            {
                model.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.25f },
                    MaterialOverride = dark, Position = new Vector3(side * w * 0.45f, 0.36f, z),
                    Rotation = new Vector3(0, 0, Mathf.Pi / 2),
                });
                Box(new Vector3(0.3f, 0.12f, 0.9f), new Vector3(side * w * 0.45f, 0.78f, z), paint);     // guardabarros
            }
        Box(new Vector3(0.18f, 0.18f, 0.06f), new Vector3(w * 0.3f, 0.95f, -l * 0.49f), chrome);          // faros
        Box(new Vector3(0.18f, 0.18f, 0.06f), new Vector3(-w * 0.3f, 0.95f, -l * 0.49f), chrome);
        return model;
    }

    public void SetInput(float throttle, float steer, bool brake, double clock)
    {
        _throttle = Mathf.Clamp(float.IsFinite(throttle) ? throttle : 0, -1, 1);
        _steer = Mathf.Clamp(float.IsFinite(steer) ? steer : 0, -1, 1);
        _brake = brake;
        _lastInput = clock;
    }

    public void ClearInput() => (_throttle, _steer, _brake) = (0, 0, false);

    /// <summary>Servidor: un paso de manejo arcade. Sin entrada reciente (se cortó el conductor) frena solo.</summary>
    public void Simulate(float dt)
    {
        _clock += dt;
        if (DriverPeer == 0 || _clock - _lastInput > 0.5)
            ClearInput();
        var max = Type.MaxSpeed;
        if (_brake)
            Speed = Mathf.MoveToward(Speed, 0, Type.Acceleration * 3f * dt);
        else if (_throttle != 0)
            Speed = Mathf.Clamp(Speed + _throttle * Type.Acceleration * dt, -max * 0.35f, max);
        else
            Speed = Mathf.MoveToward(Speed, 0, Type.Acceleration * 0.6f * dt); // rozamiento
        var grip = Mathf.Clamp(Speed / 4f, -1f, 1f);
        Rotation = new Vector3(0, Rotation.Y - _steer * Type.TurnRate * grip * dt, 0);
        var forward = -GlobalTransform.Basis.Z;
        var velocity = new Vector3(forward.X * Speed, Velocity.Y - Gravity * dt, forward.Z * Speed);
        if (IsOnFloor() && velocity.Y < 0)
            velocity.Y = -0.5f;
        Velocity = velocity;
        MoveAndSlide();
        if (GetSlideCollisionCount() > 0 && GetRealVelocity().Length() < Mathf.Abs(Speed) * 0.5f)
            Speed *= 0.4f; // chocó
    }

    /// <summary>Cliente: nuevo estado del servidor.</summary>
    public void SetTarget(Vector3 position, float yaw)
    {
        _targetPosition = position;
        _targetYaw = yaw;
    }

    public override void _Process(double delta)
    {
        if (Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer())
            return;
        var weight = 1f - Mathf.Exp(-12f * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(_targetPosition, weight);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _targetYaw, weight), 0);
    }
}
