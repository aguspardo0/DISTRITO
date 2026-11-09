using Godot;

namespace Distrito.Game.Audio;

/// <summary>
/// Reproduce los efectos (ver <see cref="Sfx"/>) en el mundo, con un grupo de reproductores que se reusan, y el sonido
/// de fondo de la ciudad: murmullo y tránsito de día (más bajo de noche) y alguna bocina a lo lejos. Todo va por el bus
/// «Efectos», cuyo volumen se elige en Esc (Opciones). Solo en los clientes.
/// </summary>
public partial class SoundPlayer : Node
{
    public const string Bus = "Efectos";

    private static SoundPlayer? _instance;
    private readonly List<AudioStreamPlayer3D> _pool = new();
    private int _next;
    private AudioStreamPlayer _street = null!;
    private readonly List<AudioStreamPlayer> _ui = new();
    private int _nextUi;
    private bool _hooked;
    private double _hornIn = 25, _tick;
    private readonly Random _rng = new();

    public static bool Enabled => _instance is not null;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            SetProcess(false);
            return;
        }
        _instance = this;
        if (AudioServer.GetBusIndex(Bus) < 0)
        {
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, Bus);
            AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
        }
        for (var i = 0; i < 24; i++)
        {
            var p = new AudioStreamPlayer3D { Bus = Bus, MaxDistance = 60, UnitSize = 6, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
            AddChild(p);
            _pool.Add(p);
        }
        for (var i = 0; i < 4; i++)
        {
            var p = new AudioStreamPlayer { Bus = Bus };
            AddChild(p);
            _ui.Add(p);
        }
        // Cada botón que aparece en pantalla suena al apretarlo (también los de ventanas que se arman después).
        GetTree().NodeAdded += OnNodeAdded;
        _hooked = true;
        foreach (var existing in GetTree().Root.FindChildren("*", "BaseButton", recursive: true, owned: false))
            OnNodeAdded(existing);
        _street = new AudioStreamPlayer { Bus = Bus, Stream = Sfx.Street, VolumeDb = -20 };
        AddChild(_street);
        _street.Play();
        Distrito.Game.UI.GameSettings.EffectsVolumeChanged += ApplyVolume;
        ApplyVolume();
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
        Distrito.Game.UI.GameSettings.EffectsVolumeChanged -= ApplyVolume;
        if (IsInsideTree() && _instance is null && _hooked)
            GetTree().NodeAdded -= OnNodeAdded;
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is BaseButton button)
            button.Pressed += () => Ui(Sfx.Click, -10, Vary(0.06f));
    }

    /// <summary>Un sonido de la interfaz (sin lugar en el mundo).</summary>
    public static void Ui(AudioStream stream, float volumeDb = 0, float pitch = 1)
    {
        if (_instance is null)
            return;
        var p = _instance._ui[_instance._nextUi++ % _instance._ui.Count];
        p.Stream = stream;
        p.VolumeDb = volumeDb;
        p.PitchScale = pitch;
        p.Play();
    }

    private static void ApplyVolume()
    {
        var v = Distrito.Game.UI.GameSettings.EffectsVolume;
        var bus = AudioServer.GetBusIndex(Bus);
        if (bus >= 0)
            AudioServer.SetBusVolumeDb(bus, v <= 0.001f ? -80 : Mathf.LinearToDb(v));
    }

    /// <summary>Un sonido en un punto del mundo.</summary>
    public static void At(AudioStream stream, Vector3 position, float volumeDb = 0, float pitch = 1, float maxDistance = 60)
    {
        if (_instance is not { } me)
            return;
        var p = me._pool[me._next++ % me._pool.Count];
        p.Stream = stream;
        p.GlobalPosition = position;
        p.VolumeDb = volumeDb;
        p.PitchScale = pitch;
        p.MaxDistance = maxDistance;
        p.Play();
    }

    /// <summary>Un poco de variación en el tono para que no suene siempre igual.</summary>
    public static float Vary(float amount = 0.08f) => 1 + ((float)Random.Shared.NextDouble() * 2 - 1) * amount;

    public override void _Process(double delta)
    {
        _tick += delta;
        if (_tick < 0.5)
            return;
        var dt = _tick;
        _tick = 0;
        var hour = Distrito.Shared.Life.BellmontClock.TimeOfDay(DateTimeOffset.UtcNow).TotalHours;
        var night = Distrito.Game.World.DayNightCycle.NightFactor(hour);
        // De día la calle suena más; de noche queda un rumor bajito.
        _street.VolumeDb = Mathf.Lerp(-16, -28, night);
        _hornIn -= dt;
        if (_hornIn <= 0)
        {
            _hornIn = 20 + _rng.NextDouble() * 50 * (1 + 2 * night);
            if (GetViewport().GetCamera3D() is { } cam)
            {
                var angle = (float)(_rng.NextDouble() * Math.Tau);
                var where = cam.GlobalPosition + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * (25 + (float)_rng.NextDouble() * 25);
                At(Sfx.Horn, where, -6, Vary(0.05f), 90);
            }
        }
    }
}
