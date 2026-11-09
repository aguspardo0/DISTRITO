using Distrito.Shared.Life;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Día y noche visibles (decisión del equipo 2026-11-08: 6 horas reales de día y 6 de noche, dos días de Bellmont por
/// día real). Sigue el mismo reloj que la vida de los NPC y las horas pico de los negocios (<see cref="BellmontClock"/>):
/// el sol sale a las 6 y se pone a las 18 de Bellmont, con amanecer y atardecer anaranjados, y de noche una luna fría.
/// Al oscurecer se prenden los faroles, brillan los carteles y se encienden algunas ventanas del Centro. Solo en los
/// clientes (el servidor no dibuja). Abajo a la derecha, la hora de Bellmont.
/// </summary>
public partial class DayNightCycle : Node
{
    /// <summary>Faroles con luz de verdad: se prenden de noche.</summary>
    public static readonly List<OmniLight3D> Lamps = new();

    /// <summary>Nublado (0 a 1): con lluvia, menos sol, cielo gris y más niebla (lo pone SceneNetwork).</summary>
    public static float Overcast { get; set; }

    /// <summary>Apagón: los faroles a menos de este radio de este punto quedan apagados (lo pone SceneNetwork).</summary>
    public static (Vector3 At, float Radius)? Blackout { get; set; }

    /// <summary>Materiales que brillan (cabezas de los faroles, carteles): de noche, más fuerte.</summary>
    public static readonly List<(StandardMaterial3D Material, float DayEnergy)> Glows = new();

    /// <summary>Las ventanas del Centro que se encienden de noche (comparten este material).</summary>
    public static readonly StandardMaterial3D LitWindows = new()
    {
        AlbedoColor = new Color(0.12f, 0.14f, 0.17f), EmissionEnabled = true, Emission = new Color(1f, 0.8f, 0.48f), EmissionEnergyMultiplier = 0,
    };

    private sealed record Look(float Hour, Color Sky, Color Ambient, float AmbientEnergy, Color Sun, float SunEnergy);

    private static readonly Look[] Looks =
    [
        new(0f, new(0.04f, 0.05f, 0.09f), new(0.25f, 0.3f, 0.45f), 0.32f, new(0.55f, 0.62f, 0.85f), 0.16f),
        new(5f, new(0.05f, 0.06f, 0.1f), new(0.25f, 0.3f, 0.45f), 0.32f, new(0.55f, 0.62f, 0.85f), 0.14f),
        new(6.5f, new(0.82f, 0.6f, 0.5f), new(0.75f, 0.6f, 0.55f), 0.5f, new(1f, 0.7f, 0.45f), 0.7f),
        new(9f, new(0.55f, 0.6f, 0.68f), new(0.6f, 0.6f, 0.65f), 0.6f, new(1f, 0.97f, 0.9f), 1f),
        new(16f, new(0.57f, 0.6f, 0.66f), new(0.62f, 0.6f, 0.62f), 0.6f, new(1f, 0.93f, 0.82f), 0.95f),
        new(18f, new(0.78f, 0.45f, 0.33f), new(0.7f, 0.5f, 0.45f), 0.45f, new(1f, 0.55f, 0.3f), 0.6f),
        new(19.5f, new(0.06f, 0.07f, 0.12f), new(0.27f, 0.3f, 0.45f), 0.34f, new(0.55f, 0.62f, 0.85f), 0.16f),
        new(24f, new(0.04f, 0.05f, 0.09f), new(0.25f, 0.3f, 0.45f), 0.32f, new(0.55f, 0.62f, 0.85f), 0.16f),
    ];

    private DirectionalLight3D? _sun;
    private Godot.Environment? _env;
    private Label? _clock;
    private double _tick = 1;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            SetProcess(false);
            return;
        }
        _sun = GetParent().GetNodeOrNull<DirectionalLight3D>("Sun");
        _env = GetParent().GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment;
        // La hora de Bellmont se muestra en la pantalla de juego (GameHud), abajo a la derecha.
    }

    /// <summary>Qué tan de noche es (0 de día, 1 de noche), con media hora de transición al amanecer y al atardecer.</summary>
    public static float NightFactor(double hour) => hour switch
    {
        < 5.5 => 1,
        < 6.5 => (float)(6.5 - hour),
        < 17.75 => 0,
        < 18.75 => (float)(hour - 17.75),
        _ => 1,
    };

    public override void _Process(double delta)
    {
        _tick += delta;
        if (_tick < 0.25)
            return;
        _tick = 0;
        var now = DateTimeOffset.UtcNow;
        var time = BellmontClock.TimeOfDay(now);
        var hour = time.TotalHours;
        Apply(hour);
        if (_clock is not null)
            _clock.Text = $"Bellmont {time:hh\\:mm} · {(BellmontClock.IsNight(now) ? "noche" : "día")}";
    }

    private void Apply(double hour)
    {
        var i = 0;
        while (i < Looks.Length - 2 && Looks[i + 1].Hour <= hour)
            i++;
        var a = Looks[i];
        var b = Looks[i + 1];
        var t = (float)Math.Clamp((hour - a.Hour) / (b.Hour - a.Hour), 0, 1);
        if (_env is not null)
        {
            var sky = a.Sky.Lerp(b.Sky, t).Lerp(new Color(0.32f, 0.34f, 0.37f) * (0.4f + 0.6f * (1 - NightFactor(hour))), Overcast * 0.75f);
            _env.BackgroundColor = sky;
            _env.FogDensity = Mathf.Lerp(0.008f, 0.02f, Overcast);
            _env.FogLightColor = sky.Lerp(new Color(0.5f, 0.5f, 0.55f), 0.2f);
            _env.AmbientLightColor = a.Ambient.Lerp(b.Ambient, t);
            _env.AmbientLightEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t);
        }
        if (_sun is not null)
        {
            _sun.LightColor = a.Sun.Lerp(b.Sun, t);
            _sun.LightEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, t) * (1 - 0.6f * Overcast);
            // De día el sol cruza de este a oeste; de noche, la luna hace el mismo camino.
            var arc = hour is >= 6 and < 18 ? (hour - 6) / 12 : ((hour + 6) % 24) / 12;
            var elevation = Mathf.DegToRad(8 + 57 * (float)Math.Sin(Math.PI * arc));
            var azimuth = Mathf.DegToRad(-90 + 180 * (float)arc);
            _sun.Rotation = new Vector3(-elevation, azimuth, 0);
        }
        var night = NightFactor(hour);
        Lamps.RemoveAll(l => !IsInstanceValid(l));
        foreach (var lamp in Lamps)
            lamp.Visible = night > 0.5f && !(Blackout is { } b0 && lamp.IsInsideTree() && lamp.GlobalPosition.DistanceTo(b0.At) < b0.Radius);
        Glows.RemoveAll(g => !IsInstanceValid(g.Material));
        foreach (var (material, dayEnergy) in Glows)
            material.EmissionEnergyMultiplier = Mathf.Lerp(dayEnergy, dayEnergy * 2.2f + 0.6f, night);
        LitWindows.EmissionEnergyMultiplier = 1.5f * night;
    }
}
