using Distrito.Server.Core.Movement;
using Godot;

namespace Distrito.Game.Player;

/// <summary>
/// Personaje de un jugador. El nombre del nodo es el id de red (peer) de su dueño.
///
/// Autoridad: el servidor simula el movimiento y replica posición/rotación (MultiplayerSynchronizer).
/// El dueño solo envía su intención de movimiento con <see cref="SubmitLocalInput"/>.
/// En los demás equipos el modelo visual se suaviza hacia la última posición recibida.
/// </summary>
public partial class PlayerCharacter : CharacterBody3D
{
    private const float VisualSmoothing = 15f;

    private readonly PlayerInputState _input = new();
    private float _gravity;
    private double _clock;
    private Node3D _visual = null!;

    public long PeerId => long.Parse(Name.ToString());

    /// <summary>Nombre visible. Lo fija el servidor y se replica una vez al aparecer.</summary>
    [Export]
    public string DisplayName { get; set; } = "";

    /// <summary>Punto de aparición asignado por el servidor (solo se usa en el servidor).</summary>
    public Vector3 SpawnPoint { get; set; }

    /// <summary>Posición suavizada que ve el jugador; la cámara la sigue.</summary>
    public Vector3 VisualPosition => _visual.GlobalPosition;

    public override void _Ready()
    {
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        _visual = GetNode<Node3D>("Visual");
        _visual.TopLevel = true;
        _visual.GlobalTransform = GlobalTransform;

        GetNode<MeshInstance3D>("Visual/Body").MaterialOverride = new StandardMaterial3D { AlbedoColor = ColorFor(PeerId) };
        GetNode<Label3D>("Visual/NameTag").Text = DisplayName.Length > 0 ? DisplayName : $"Jugador {PeerId}";
    }

    public override void _Process(double delta)
    {
        var weight = 1f - Mathf.Exp(-VisualSmoothing * (float)delta);
        _visual.GlobalPosition = _visual.GlobalPosition.Lerp(GlobalPosition, weight);
        _visual.GlobalRotation = new Vector3(0, Mathf.LerpAngle(_visual.GlobalRotation.Y, GlobalRotation.Y, weight), 0);
    }

    /// <summary>Lo llama el controlador local del dueño. Envía la intención al servidor.</summary>
    public void SubmitLocalInput(float x, float z, bool jump)
    {
        if (Multiplayer.IsServer())
            ApplyInput(x, z, jump); // modo anfitrión: el servidor es este mismo proceso
        else
            RpcId(1, MethodName.ReceiveInput, x, z, jump);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReceiveInput(float x, float z, bool jump)
    {
        if (!Multiplayer.IsServer())
            return;

        // Solo el dueño puede mover su personaje.
        if (Multiplayer.GetRemoteSenderId() != PeerId)
        {
            GD.PushWarning($"[DISTRITO] Peer {Multiplayer.GetRemoteSenderId()} intentó mover al jugador {PeerId}. Ignorado.");
            return;
        }

        ApplyInput(x, z, jump);
    }

    private void ApplyInput(float x, float z, bool jump) => _input.Update(new MovementInput(x, z, jump), _clock);

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer())
            return;

        _clock += delta;
        var input = _input.Current(_clock);

        var velocity = Velocity;
        if (!IsOnFloor())
            velocity.Y -= _gravity * (float)delta;
        else if (input.Jump)
            velocity.Y = MovementRules.JumpVelocity;

        var horizontal = MovementRules.HorizontalVelocity(input);
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Y;
        Velocity = velocity;
        MoveAndSlide();

        if (horizontal.LengthSquared() > 0.01f)
            Rotation = new Vector3(0, Mathf.Atan2(-horizontal.X, -horizontal.Y), 0);

        if (GlobalPosition.Y < MovementRules.KillPlaneY)
        {
            GlobalPosition = SpawnPoint;
            Velocity = Vector3.Zero;
        }
    }

    private static Color ColorFor(long peerId)
    {
        var hue = (float)(peerId * 0.618033988749895 % 1.0);
        return Color.FromHsv(hue, 0.65f, 0.9f);
    }
}
