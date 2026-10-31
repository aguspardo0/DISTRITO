using Distrito.Game.Network;
using Godot;

namespace Distrito.Game.Player;

/// <summary>
/// Solo existe en el equipo del dueño del personaje. Lee teclado y mouse (o un bot de prueba),
/// maneja la cámara en tercera persona y envía la intención de movimiento.
/// </summary>
public partial class LocalPlayerController : Node
{
    private const float MouseSensitivity = 0.003f;
    private const float MinPitch = -1.2f;
    private const float MaxPitch = 0.3f;
    private const float CameraDistance = 4.5f;
    private const float CameraHeight = 1.6f;

    private PlayerCharacter _player = null!;
    private Node3D _yaw = null!;
    private Node3D _pitch = null!;
    private double _botClock;

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
        var arm = new SpringArm3D { Name = "SpringArm", SpringLength = CameraDistance };
        arm.AddExcludedObject(_player.GetRid());
        var camera = new Camera3D { Name = "Camera" };

        AddChild(_yaw);
        _yaw.AddChild(_pitch);
        _pitch.AddChild(arm);
        arm.AddChild(camera);
        camera.Current = true;
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
        else if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event is InputEventMouseButton { Pressed: true } && !IsHeadless)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        _yaw.GlobalPosition = _player.VisualPosition + Vector3.Up * CameraHeight;
    }

    public override void _PhysicsProcess(double delta)
    {
        var (direction, jump) = UseBot ? BotInput(delta) : KeyboardInput();
        _player.SubmitLocalInput(direction.X, direction.Y, jump);
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
