using Distrito.Game.Network;
using Godot;

namespace Distrito.Game.Player;

/// <summary>
/// Solo existe en el equipo del dueño del personaje. Lee teclado y mouse (o un bot de prueba),
/// maneja la cámara en tercera persona y envía la intención de movimiento.
/// </summary>
public partial class LocalPlayerController : Node
{
    private static float MouseSensitivity => Distrito.Game.UI.GameSettings.MouseSensitivity;

    /// <summary>El jugador quiere pegar (clic izquierdo o F). Lo atiende CombatNetwork.</summary>
    public static event Action? Punch;

    /// <summary>Tecla G: subir o bajar del vehículo más cercano.</summary>
    public static event Action? VehicleKey;

    /// <summary>Manejando: acelerador (−1…1), dirección (−1 izq…1 der) y freno. Lo atiende VehicleNetwork.</summary>
    public static event Action<float, float, bool>? DriveInput;

    /// <summary>Teclas 1–4: elegir arma (1 = piñas).</summary>
    public static event Action<int>? WeaponKey;
    private const float MinPitch = -1.2f;
    private const float MaxPitch = 0.3f;
    private const float CameraDistance = 4.5f;
    private const float CameraHeight = 1.6f;

    private PlayerCharacter _player = null!;
    /// <summary>Gira la cámara para mirar hacia un ángulo (radianes; 0 = hacia −Z). Lo usan las capturas de prueba.</summary>
    public void LookToward(float yaw)
    {
        if (_yaw is not null)
            _yaw.GlobalRotation = new Vector3(0, yaw, 0);
    }

    private Node3D _yaw = null!;
    private Node3D _pitch = null!;
    private double _botClock;
    private LocalPrediction? _prediction;
    private Node3D? _excludedVehicle;

    /// <summary>Bot de pruebas: camina en círculos solo y se detiene al final para que la posición final sea estable.</summary>
    public BotSettings Bot { get; init; } = BotSettings.Off;

    private bool UseBot => Bot.Enabled;

    /// <summary>Dirección impuesta por un bot con guion (por ejemplo, el bot de contrato). Tiene prioridad.</summary>
    public Vector2? ScriptedDirection { get; set; }

    private static bool IsHeadless => DisplayServer.GetName() == "headless";

    public override void _Ready()
    {
        _player = GetParent<PlayerCharacter>();

        _yaw = new Node3D { Name = "CameraYaw" };
        _pitch = new Node3D { Name = "CameraPitch", Rotation = new Vector3(-0.35f, 0, 0) };
        // La cámara choca con una esfera y deja margen: en los interiores y contra edificios no se mete en la pared.
        var arm = new SpringArm3D { Name = "SpringArm", SpringLength = CameraDistance, Margin = 0.25f, Shape = new SphereShape3D { Radius = 0.25f } };
        arm.AddExcludedObject(_player.GetRid());
        var camera = new Camera3D { Name = "Camera" };

        AddChild(_yaw);
        _yaw.AddChild(_pitch);
        _pitch.AddChild(arm);
        arm.AddChild(camera);
        camera.Current = true;

        // En un cliente remoto se predice el movimiento propio. En el anfitrión no hace falta: el servidor es este proceso.
        if (!Multiplayer.IsServer())
        {
            _prediction = new LocalPrediction();
            _prediction.Initialize(_player);
            AddChild(_prediction);
        }
        _yaw.GlobalPosition = _player.GlobalPosition + Vector3.Up * CameraHeight;

        if (!IsHeadless && !UseBot)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (UseBot)
            return;

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw.RotateY(-motion.Relative.X * MouseSensitivity);
            var pitch = Mathf.Clamp(_pitch.Rotation.X - motion.Relative.Y * MouseSensitivity, MinPitch, MaxPitch);
            _pitch.Rotation = new Vector3(pitch, 0, 0);
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Punch?.Invoke(); // clic con el mouse capturado = piña
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } && !IsHeadless && !AnyWindowOpen())
        {
            // Un clic en el juego (no en una ventana) vuelve a tomar el mouse. La rueda nunca: antes, girar la rueda
            // dentro de una ventana escondía el cursor.
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F } && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Punch?.Invoke();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.G } && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            VehicleKey?.Invoke();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode is >= Key.Key1 and <= Key.Key4)
        {
            WeaponKey?.Invoke((int)(key.PhysicalKeycode - Key.Key1) + 1);
        }
    }

    /// <summary>¿Hay alguna ventana (panel, diálogo, menú) abierta? Mientras haya, el cursor queda libre.</summary>
    private bool AnyWindowOpen()
    {
        if (GetTree().CurrentScene?.GetNodeOrNull("Hud") is not { } hud)
            return false;
        return hud.FindChildren("*", "PanelContainer", recursive: true, owned: false).OfType<Control>().Any(c => c.IsVisibleInTree());
    }

    public override void _Process(double delta)
    {
        var vehicle = _player.Vehicle.Length > 0 ? Distrito.Game.Transport.VehicleNetwork.Find(_player.Vehicle) : null;
        _yaw.GlobalPosition = (vehicle?.GlobalPosition ?? _player.VisualPosition) + Vector3.Up * (vehicle is null ? CameraHeight : CameraHeight + 1f);
        var arm = _pitch.GetNode<SpringArm3D>("SpringArm");
        if (vehicle is not null && vehicle != _excludedVehicle)
        {
            arm.AddExcludedObject(vehicle.GetRid()); // la cámara no choca con el propio vehículo
            _excludedVehicle = vehicle;
        }
        arm.SpringLength = Mathf.Lerp(arm.SpringLength, vehicle is null ? CameraDistance : CameraDistance + 4f, 1f - Mathf.Exp(-4f * (float)delta));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player.Vehicle.Length > 0)
        {
            // Manejando: W/S, A/D y Espacio van al vehículo; el personaje no camina.
            var captured = !UseBot && Input.MouseMode == Input.MouseModeEnum.Captured;
            DriveInput?.Invoke(captured ? Axis(Key.W, Key.S) : 0, captured ? Axis(Key.D, Key.A) : 0, captured && Input.IsPhysicalKeyPressed(Key.Space));
            _player.SubmitLocalInput(0, 0, false);
            _prediction?.SetInput(0, 0, false);
            return;
        }
        var (direction, jump) = UseBot ? BotInput(delta) : KeyboardInput();
        var sprint = !UseBot && Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsPhysicalKeyPressed(Key.Shift);
        if (_player.Downed)
            (direction, jump, sprint) = (Vector2.Zero, false, false);
        _player.SubmitLocalInput(direction.X, direction.Y, jump, sprint);
        _prediction?.SetInput(direction.X, direction.Y, jump, sprint);
        if (_prediction is not null)
            _prediction.AimYaw = _player.Weapon != Distrito.Shared.Economy.Weapons.Fists && !UseBot ? _yaw.GlobalRotation.Y : null;
    }

    private (Vector2 Direction, bool Jump) KeyboardInput()
    {
        // Sin foco o con el mouse liberado, el personaje no se mueve.
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            return (Vector2.Zero, false);

        var forwardAxis = Axis(Key.W, Key.S);
        var rightAxis = Axis(Key.D, Key.A);

        var basis = _yaw.GlobalBasis;
        var forward = new Vector2(-basis.Z.X, -basis.Z.Z).Normalized();
        var right = new Vector2(basis.X.X, basis.X.Z).Normalized();
        var direction = forward * forwardAxis + right * rightAxis;
        if (direction.LengthSquared() > 1f)
            direction = direction.Normalized();

        return (direction, Input.IsPhysicalKeyPressed(Key.Space));
    }

    private (Vector2 Direction, bool Jump) BotInput(double delta)
    {
        if (ScriptedDirection is { } scripted)
            return (scripted, false);

        _botClock += delta;
        if (_botClock >= Bot.StopAfterSeconds)
            return (Vector2.Zero, false);
        return (new Vector2(Mathf.Cos((float)_botClock), Mathf.Sin((float)_botClock)), false);
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);
}
