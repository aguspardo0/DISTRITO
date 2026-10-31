using Distrito.Shared.Api;
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

    /// <summary>Nombre de la cuadrilla (vacío si no tiene). Lo fija el servidor y se replica cuando cambia.</summary>
    [Export]
    public string CrewName { get; set; } = "";

    /// <summary>Aspecto (código de CharacterLooks). Lo fija el servidor y se replica al aparecer.</summary>
    [Export]
    public string Look { get; set; } = "";

    private CharacterModel? _model;
    private Vector3 _lastVisualPosition;

    private Label3D _nameTag = null!;
    private string _shownTag = "";

    /// <summary>Punto de aparición asignado por el servidor (solo se usa en el servidor).</summary>
    public Vector3 SpawnPoint { get; set; }

    /// <summary>Si se asigna (personaje propio con predicción), el modelo visual sigue a este nodo en vez de al cuerpo replicado.</summary>
    public Node3D? VisualOverride { get; set; }

    /// <summary>Posición suavizada que ve el jugador; la cámara la sigue.</summary>
    public Vector3 VisualPosition => _visual.GlobalPosition;

    public override void _Ready()
    {
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        _visual = GetNode<Node3D>("Visual");
        _visual.TopLevel = true;
        _visual.GlobalTransform = GlobalTransform;

        GetNode<MeshInstance3D>("Visual/Body").Visible = false;
        GetNode<MeshInstance3D>("Visual/Facing").Visible = false;
        RefreshModel();
        _lastVisualPosition = _visual.GlobalPosition;
        _nameTag = GetNode<Label3D>("Visual/NameTag");
        RefreshNameTag();
    }

    private void RefreshModel()
    {
        var look = Look.Length > 0 ? CharacterLooks.Find(Look) : CharacterLooks.ForGuest(PeerId);
        if (_model is not null && _model.LookCode == look.Code)
            return;
        _model?.QueueFree();
        _model = CharacterModel.Build(look);
        _visual.AddChild(_model);
    }

    private void RefreshNameTag()
    {
        var name = DisplayName.Length > 0 ? DisplayName : $"Jugador {PeerId}";
        var tag = CrewName.Length > 0 ? $"{name}\n«{CrewName}»" : name;
        if (tag == _shownTag)
            return;
        _shownTag = tag;
        _nameTag.Text = tag;
    }

    public override void _Process(double delta)
    {
        RefreshNameTag();
        RefreshModel();
        AnimateModel((float)delta);
        if (VisualOverride is { } predicted && IsInstanceValid(predicted))
        {
            // Personaje propio: se muestra la predicción local, sin demora.
            _visual.GlobalPosition = predicted.GlobalPosition;
            _visual.GlobalRotation = new Vector3(0, Mathf.LerpAngle(_visual.GlobalRotation.Y, predicted.GlobalRotation.Y, 1f - Mathf.Exp(-20f * (float)delta)), 0);
            return;
        }
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

    /// <summary>La caminata sale de cuánto se movió el modelo visual (sirve igual para el propio y para los demás).</summary>
    private void AnimateModel(float delta)
    {
        if (_model is null || delta <= 0)
            return;
        var now = _visual.GlobalPosition;
        var moved = new Vector2(now.X - _lastVisualPosition.X, now.Z - _lastVisualPosition.Z).Length();
        _lastVisualPosition = now;
        _model.Animate(moved / delta, delta);
    }
}
