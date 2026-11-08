using Distrito.Shared.Api;
using Distrito.Server.Core.Movement;
using Godot;

namespace Distrito.Game.Player;

/// <summary>
/// Personaje de un jugador. El nombre del nodo es el id de red (peer) de su dueño.
///
/// Autoridad: el servidor simula el movimiento; la posición y el giro viajan en paquetes compactos (PlayerSnapshots) y el resto por MultiplayerSynchronizer.
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

    private long _peerId;

    /// <summary>Se lee muchas veces por segundo (paquetes de movimiento): se calcula una sola vez.</summary>
    public long PeerId => _peerId != 0 ? _peerId : (_peerId = long.Parse(Name.ToString()));

    /// <summary>Nombre visible. Lo fija el servidor y se replica una vez al aparecer.</summary>
    [Export]
    public string DisplayName { get; set; } = "";

    /// <summary>Nombre de la cuadrilla (vacío si no tiene). Lo fija el servidor y se replica cuando cambia.</summary>
    [Export]
    public string CrewName { get; set; } = "";

    /// <summary>Aspecto (código de CharacterLooks). Lo fija el servidor y se replica al aparecer.</summary>
    [Export]
    public string Look { get; set; } = "";

    /// <summary>Vida (0–100). La decide el servidor.</summary>
    [Export]
    public int Health { get; set; } = Distrito.Server.Core.Combat.CombatRules.MaxHealth;

    /// <summary>Arma en la mano (código de Weapons, "punos" = sin arma). La fija el servidor.</summary>
    [Export]
    public string Weapon { get; set; } = Distrito.Shared.Economy.Weapons.Fists;

    /// <summary>Fuera de combate (herido o muerto): no se mueve.</summary>
    [Export]
    public bool Downed { get; set; }

    /// <summary>Ángulo vertical de la puntería (radianes, + arriba). Con arma, el brazo apunta así.</summary>
    [Export]
    public float AimPitch { get; set; }

    /// <summary>Servidor: hacia dónde mira con el arma (yaw). Con arma el cuerpo gira hacia la mira, no hacia donde camina.</summary>
    public float AimYaw { get; set; }

    /// <summary>Id del vehículo que maneja ("" = a pie). Lo fija el servidor; adentro no se ve ni choca.</summary>
    [Export]
    public string Vehicle
    {
        get => _vehicle;
        set
        {
            _vehicle = value ?? "";
            if (_visual is not null)
                ApplyVehicleState();
        }
    }
    private string _vehicle = "";

    private void ApplyVehicleState()
    {
        var driving = _vehicle.Length > 0;
        _visual.Visible = !driving;
        CollisionLayer = driving ? 0u : 1u;
        // Los objetos con física (capa 3) no frenan al jugador: el servidor los empuja al pasar (así la predicción coincide).
        CollisionMask = driving ? 0u : 1u;
    }

    /// <summary>Muerto (además de Downed): reaparece a los 10 s. Herido = Downed sin Dead.</summary>
    [Export]
    public bool Dead { get; set; }

    private CharacterModel? _model;
    private Vector3 _lastVisualPosition;
    private float _stepDistance;
    private int _steps;

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
        ApplyVehicleState();
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
        if (Dead)
            tag += "\n(muerto)";
        else if (Downed)
            tag += "\n(herido — un compañero lo levanta con E)";
        else if (Health < Distrito.Server.Core.Combat.CombatRules.MaxHealth)
            tag += $"\n♥ {Health}";
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
    public void SubmitLocalInput(float x, float z, bool jump, bool sprint = false)
    {
        if (Multiplayer.IsServer())
            ApplyInput(x, z, jump, sprint); // modo anfitrión: el servidor es este mismo proceso
        else
            RpcId(1, MethodName.ReceiveInput, x, z, jump, sprint);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    public void ReceiveInput(float x, float z, bool jump, bool sprint)
    {
        if (!Multiplayer.IsServer())
            return;

        // Solo el dueño puede mover su personaje.
        if (Multiplayer.GetRemoteSenderId() != PeerId)
        {
            GD.PushWarning($"[DISTRITO] Peer {Multiplayer.GetRemoteSenderId()} intentó mover al jugador {PeerId}. Ignorado.");
            return;
        }

        ApplyInput(x, z, jump, sprint);
    }

    private void ApplyInput(float x, float z, bool jump, bool sprint) =>
        _input.Update(Downed ? MovementInput.None : new MovementInput(x, z, jump, sprint), _clock);

    /// <summary>Animación de piña (la manda el servidor a todos).</summary>
    public void PlayPunch() => _model?.Punch();
    public void PlayRecoil() => _model?.Recoil();
    public void PlayReload(float seconds) => _model?.Reload(seconds);

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer())
            return;
        if (_vehicle.Length > 0)
            return; // lo mueve el vehículo (VehicleNetwork)
        if (Downed)
        {
            Velocity = new Vector3(0, Velocity.Y - _gravity * (float)delta, 0);
            MoveAndSlide();
            return;
        }

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
        Distrito.Game.World.PropsNetwork.PushNearby(GlobalPosition, new Vector3(horizontal.X, 0, horizontal.Y), 0.75f, 0.9f);

        if (Weapon != Distrito.Shared.Economy.Weapons.Fists)
            Rotation = new Vector3(0, AimYaw, 0);
        else if (horizontal.LengthSquared() > 0.01f)
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
        _model.SetDowned(Downed);
        _model.SetWeapon(Weapon);
        _model.SetAimPitch(AimPitch);
        var now = _visual.GlobalPosition;
        var moved = new Vector2(now.X - _lastVisualPosition.X, now.Z - _lastVisualPosition.Z).Length();
        var grounded = MathF.Abs(now.Y - _lastVisualPosition.Y) < 0.05f && Vehicle.Length == 0 && !Downed;
        _lastVisualPosition = now;
        _model.Animate(moved / delta, delta);
        // Pasos: uno cada 1,7 m caminados en el piso (más seguido corriendo, porque se camina más rápido).
        if (grounded && moved < 2 && Distrito.Game.Audio.SoundPlayer.Enabled)
        {
            _stepDistance += moved;
            if (_stepDistance >= 1.7f)
            {
                _stepDistance = 0;
                Distrito.Game.Audio.SoundPlayer.At(Distrito.Game.Audio.Sfx.Step(_steps++), now + Vector3.Up * 0.1f, -10, Distrito.Game.Audio.SoundPlayer.Vary(0.1f), 25);
            }
        }
    }
}
