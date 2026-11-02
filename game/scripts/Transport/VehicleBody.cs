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

    /// <summary>Multiplicador de velocidad del conductor (habilidad «Buen conductor»).</summary>
    public float SpeedMultiplier { get; set; } = 1f;

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

    /// <summary>
    /// Modelo procedural de principios de los 50 (Bellmont, 1952): guardabarros redondeados, dos tonos (techo crema),
    /// parrilla ancha de barras cromadas, paragolpes cromados, faros redondos en los guardabarros y cubiertas de banda blanca.
    /// La caja de carga sigue abierta (la carga se ve) con piso de madera.
    /// </summary>
    private static Node3D BuildModel(VehicleType type)
    {
        var model = new Node3D { Name = "Model" };
        var truck = type.Code == Vehicles.Truck.Code;
        var paint = new StandardMaterial3D { AlbedoColor = truck ? new Color(0.2f, 0.32f, 0.26f) : new Color(0.28f, 0.56f, 0.55f), Roughness = 0.35f, Metallic = 0.15f };
        var cream = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.86f, 0.74f), Roughness = 0.35f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.08f) };
        var tire = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Roughness = 0.9f };
        var whitewall = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.91f, 0.88f) };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.3f, 0.18f) };
        var chrome = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.82f, 0.8f), Metallic = 0.9f, Roughness = 0.2f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.32f, 0.36f), Metallic = 0.3f, Roughness = 0.1f };
        var lens = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.95f, 0.8f), EmissionEnabled = true, Emission = new Color(1f, 0.92f, 0.7f), EmissionEnergyMultiplier = 0.6f };
        float l = type.Length, w = type.Width, h = type.Height;
        MeshInstance3D Add(Mesh mesh, Vector3 at, Material mat, Vector3? rotation = null, Vector3? scale = null)
        {
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at, Rotation = rotation ?? Vector3.Zero, Scale = scale ?? Vector3.One };
            model.AddChild(mi);
            return mi;
        }
        void Box(Vector3 size, Vector3 at, Material mat) => Add(new BoxMesh { Size = size }, at, mat);
        var alongZ = new Vector3(Mathf.Pi / 2, 0, 0);
        var alongX = new Vector3(0, 0, Mathf.Pi / 2);

        Box(new Vector3(w * 0.86f, 0.2f, l * 0.95f), new Vector3(0, 0.55f, 0), dark);                                  // chasis
        // Capó largo y redondeado adelante.
        Box(new Vector3(w * 0.62f, h * 0.24f, l * 0.26f), new Vector3(0, 0.88f, -l * 0.32f), paint);
        Add(new CapsuleMesh { Radius = h * 0.12f, Height = w * 0.62f }, new Vector3(0, 0.88f, -l * 0.45f), paint, alongX);
        // Parrilla ancha: tres barras cromadas horizontales.
        for (var i = 0; i < 3; i++)
            Box(new Vector3(w * 0.56f, 0.05f, 0.06f), new Vector3(0, 0.74f + i * 0.09f, -l * 0.48f - 0.04f), chrome);
        // Cabina con techo crema redondeado y parabrisas partido.
        Box(new Vector3(w * 0.9f, h * 0.36f, l * 0.22f), new Vector3(0, 1.12f, -l * 0.1f), paint);
        Add(new CapsuleMesh { Radius = l * 0.11f, Height = w * 0.9f }, new Vector3(0, 1.4f, -l * 0.1f), cream, alongX, new Vector3(0.6f, 1, 1));
        foreach (var x in new[] { -w * 0.21f, w * 0.21f })
            Box(new Vector3(w * 0.38f, h * 0.16f, 0.05f), new Vector3(x, 1.38f, -l * 0.215f), glass);
        foreach (var side in new[] { -1f, 1f })
            Box(new Vector3(0.05f, h * 0.15f, l * 0.13f), new Vector3(side * w * 0.452f, 1.37f, -l * 0.1f), glass);   // ventanillas

        // Caja de carga abierta: piso de madera, laterales pintados (camioneta) o estacas altas (camión).
        var (bedStart, bedEnd) = BedSpan(type);
        var bedLength = bedEnd - bedStart;
        var bedCenter = (bedStart + bedEnd) / 2;
        var rail = truck ? 0.9f : 0.42f;
        var sideMat = truck ? wood : paint;
        Box(new Vector3(w * 0.95f, 0.08f, bedLength), new Vector3(0, 0.72f, bedCenter), wood);
        Box(new Vector3(0.08f, rail, bedLength), new Vector3(w * 0.45f, 0.76f + rail / 2, bedCenter), sideMat);
        Box(new Vector3(0.08f, rail, bedLength), new Vector3(-w * 0.45f, 0.76f + rail / 2, bedCenter), sideMat);
        Box(new Vector3(w * 0.95f, rail, 0.08f), new Vector3(0, 0.76f + rail / 2, bedEnd), sideMat);
        if (truck)
            for (var i = 0; i <= 4; i++)
                foreach (var side in new[] { -1f, 1f })
                    Box(new Vector3(0.1f, rail + 0.25f, 0.1f), new Vector3(side * w * 0.46f, 0.76f + (rail + 0.25f) / 2, bedStart + bedLength * i / 4f), dark);

        // Ruedas con banda blanca y taza cromada, bajo guardabarros redondeados.
        foreach (var side in new[] { -1f, 1f })
            foreach (var z in new[] { -l * 0.32f, l * 0.3f })
            {
                var x = side * w * 0.45f;
                Add(new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.25f }, new Vector3(x, 0.36f, z), tire, alongX);
                Add(new CylinderMesh { TopRadius = 0.27f, BottomRadius = 0.27f, Height = 0.02f }, new Vector3(x + side * 0.125f, 0.36f, z), whitewall, alongX);
                Add(new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 0.03f }, new Vector3(x + side * 0.135f, 0.36f, z), chrome, alongX);
                Add(new CapsuleMesh { Radius = 0.26f, Height = 1.15f }, new Vector3(x, 0.74f, z), paint, alongZ, new Vector3(1.05f, 1f, 0.7f));
            }
        // Faros redondos en los guardabarros delanteros.
        foreach (var side in new[] { -1f, 1f })
        {
            var at = new Vector3(side * w * 0.45f, 0.86f, -l * 0.32f - 0.56f);
            Add(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.06f }, at, chrome, alongZ);
            Add(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.02f }, at + new Vector3(0, 0, -0.035f), lens, alongZ);
        }
        // Paragolpes cromados.
        Box(new Vector3(w * 1.0f, 0.12f, 0.12f), new Vector3(0, 0.58f, -l * 0.5f), chrome);
        Box(new Vector3(w * 0.98f, 0.1f, 0.1f), new Vector3(0, 0.58f, l * 0.49f), chrome);
        return model;
    }

    /// <summary>Dónde va la caja de carga (z local, de adelante hacia atrás).</summary>
    private static (float Start, float End) BedSpan(VehicleType type) =>
        type.Code == Vehicles.Truck.Code ? (type.Length * 0.02f, type.Length * 0.48f) : (type.Length * 0.04f, type.Length * 0.47f);

    private Node3D? _cargo;

    /// <summary>Muestra la carga real en la caja (bultos agrupados). Lo llama VisualStock cuando cambian las existencias.</summary>
    public void SetCargo(List<StockPile> items)
    {
        _cargo?.QueueFree();
        _cargo = new Node3D { Name = "Cargo" };
        var (start, end) = BedSpan(Type);
        _cargo.Position = new Vector3(0, 0.77f, (start + end) / 2);
        AddChild(_cargo);
        Distrito.Game.World.StockProps.Layout(_cargo, items, Type.Width * 0.82f, end - start - 0.1f, levels: Type.Code == Vehicles.Truck.Code ? 3 : 2);
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
        var max = Type.MaxSpeed * SpeedMultiplier;
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
        Distrito.Game.World.PropsNetwork.PushNearby(GlobalPosition, -GlobalTransform.Basis.Z * Speed, Type.Length / 2 + 0.3f, 3.5f); // atropella cajones y barriles
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
