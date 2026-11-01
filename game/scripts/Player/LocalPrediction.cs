using Distrito.Server.Core.Movement;
using Godot;

namespace Distrito.Game.Player;

/// <summary>
/// Predicción del lado del cliente para el personaje propio.
///
/// Un "fantasma" local (CharacterBody3D solo en este cliente) aplica la misma entrada con las mismas reglas
/// que el servidor, así el personaje responde al instante aunque haya ping. El modelo visual y la cámara
/// siguen al fantasma. La posición real sigue siendo la del servidor: el fantasma se corrige suavemente
/// hacia ella si se aleja más de lo explicable por la latencia, y salta de golpe si la diferencia es grande
/// (por ejemplo, si el servidor rechazó un movimiento o lo devolvió al punto de aparición).
/// </summary>
public partial class LocalPrediction : CharacterBody3D
{
    private const float SnapDistance = 3f;
    private const float BaseTolerance = 0.35f;
    private const float CorrectionRate = 3f;     // por segundo, mientras se mueve
    private const float IdleCorrectionRate = 8f; // quieto: converge rápido a la posición real

    private PlayerCharacter _player = null!;
    private float _gravity;
    private MovementInput _input;

    public void Initialize(PlayerCharacter player)
    {
        _player = player;
        Name = "Prediction";
        TopLevel = true;
        CollisionLayer = 0; // no empuja a nadie: solo choca con el mundo y con los demás
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
    }

    public override void _Ready()
    {
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        AddCollisionExceptionWith(_player);
        GlobalPosition = _player.GlobalPosition;
        _player.VisualOverride = this;
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_player) && _player.VisualOverride == this)
            _player.VisualOverride = null;
    }

    /// <summary>La entrada que también se mandó al servidor.</summary>
    public void SetInput(float x, float z, bool jump, bool sprint = false) => _input = MovementRules.Sanitize(new MovementInput(x, z, jump, sprint));

    private Vector3 _lastServerPosition;

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;

        // 1. Simular igual que el servidor.
        var velocity = Velocity;
        if (!IsOnFloor())
            velocity.Y -= _gravity * dt;
        else if (_input.Jump)
            velocity.Y = MovementRules.JumpVelocity;
        var horizontal = MovementRules.HorizontalVelocity(_input);
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Y;
        Velocity = velocity;
        MoveAndSlide();
        if (horizontal.LengthSquared() > 0.01f)
            Rotation = new Vector3(0, Mathf.Atan2(-horizontal.X, -horizontal.Y), 0);

        // 2. Reconciliar con la posición autoritativa del servidor.
        // Si el servidor movió al personaje de golpe (reaparición, rescate), se salta directo.
        var serverJump = _player.GlobalPosition.DistanceTo(_lastServerPosition);
        _lastServerPosition = _player.GlobalPosition;
        var error = _player.GlobalPosition - GlobalPosition;
        var flatError = new Vector2(error.X, error.Z).Length();
        if (flatError > SnapDistance || Mathf.Abs(error.Y) > 2f || serverJump > 1.2f || _player.Downed)
        {
            GlobalPosition = _player.GlobalPosition;
            Velocity = Vector3.Zero;
            return;
        }

        var moving = horizontal.LengthSquared() > 0.01f;
        var tolerance = BaseTolerance + (moving ? MovementRules.RunSpeed * LatencySeconds() : 0f);
        if (flatError > tolerance || !moving)
        {
            var rate = moving ? CorrectionRate : IdleCorrectionRate;
            var weight = 1f - Mathf.Exp(-rate * dt);
            GlobalPosition += new Vector3(error.X, IsOnFloor() ? 0 : error.Y * 0.2f, error.Z) * weight;
        }
    }

    /// <summary>Latencia de ida y vuelta más el intervalo de replicación: cuánto "atrás" llega la posición del servidor.</summary>
    private float LatencySeconds()
    {
        const float replicationInterval = 0.05f;
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet && enet.GetPeer(1) is { } server)
        {
            var rtt = server.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime) / 1000.0;
            return (float)Math.Clamp(rtt, 0.0, 0.5) + replicationInterval;
        }
        return 0.1f + replicationInterval;
    }
}
