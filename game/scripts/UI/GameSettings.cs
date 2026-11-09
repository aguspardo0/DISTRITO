using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// Opciones del jugador guardadas en user://settings.cfg: modo de pantalla, sincronización vertical y sensibilidad del mouse.
/// Se aplican al arrancar el cliente (nunca en servidor o bots sin pantalla).
/// </summary>
public static class GameSettings
{
    private const string Path = "user://settings.cfg";
    public static readonly Vector2I WindowedSize = new(1280, 720);

    public static bool Fullscreen { get; private set; }
    public static bool VSync { get; private set; } = true;
    public static float MouseSensitivity { get; private set; } = 0.003f;

    /// <summary>Volumen de la música (0 a 1).</summary>
    public static float MusicVolume { get; private set; } = 0.5f;

    public static event Action? MusicVolumeChanged;

    /// <summary>Volumen de los efectos y el sonido de la calle (0 a 1).</summary>
    public static float EffectsVolume { get; private set; } = 0.8f;

    public static event Action? EffectsVolumeChanged;

    /// <summary>Calidad gráfica: 0 baja (notebooks), 1 media, 2 alta. Ver <see cref="Distrito.Game.World.GraphicsQuality"/>.</summary>
    public static int Quality { get; private set; } = 1;
    public static event Action? QualityChanged;

    public static void SetQuality(int value)
    {
        Quality = Math.Clamp(value, 0, 2);
        Save();
        QualityChanged?.Invoke();
    }

    /// <summary>Solo para la medición automática: cambia la calidad sin guardarla.</summary>
    public static void PreviewQuality(int value)
    {
        Quality = Math.Clamp(value, 0, 2);
        QualityChanged?.Invoke();
    }

    public static bool IsHeadless => DisplayServer.GetName() == "headless";

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            Fullscreen = (bool)cfg.GetValue("pantalla", "completa", false);
            VSync = (bool)cfg.GetValue("pantalla", "vsync", true);
            MouseSensitivity = Mathf.Clamp((float)cfg.GetValue("controles", "sensibilidad", 0.003f), 0.0005f, 0.01f);
            MusicVolume = Mathf.Clamp((float)cfg.GetValue("sonido", "musica", 0.5f), 0f, 1f);
            EffectsVolume = Mathf.Clamp((float)cfg.GetValue("sonido", "efectos", 0.8f), 0f, 1f);
            Quality = Math.Clamp((int)cfg.GetValue("pantalla", "calidad", 1), 0, 2);
        }
        ApplyDisplay();
    }

    public static void SetFullscreen(bool on) { Fullscreen = on; ApplyDisplay(); Save(); }
    public static void ToggleFullscreen() => SetFullscreen(!Fullscreen);
    public static void SetVSync(bool on) { VSync = on; ApplyDisplay(); Save(); }
    public static void SetMouseSensitivity(float value) { MouseSensitivity = Mathf.Clamp(value, 0.0005f, 0.01f); Save(); }
    public static void SetMusicVolume(float value) { MusicVolume = Mathf.Clamp(value, 0f, 1f); Save(); MusicVolumeChanged?.Invoke(); }
    public static void SetEffectsVolume(float value) { EffectsVolume = Mathf.Clamp(value, 0f, 1f); Save(); EffectsVolumeChanged?.Invoke(); }

    private static void ApplyDisplay()
    {
        if (IsHeadless)
            return;
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        if (Fullscreen)
        {
            // Pantalla completa sin bordes: no cambia la resolución del monitor y Alt+Tab funciona bien.
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            return;
        }

        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
        // Al volver de pantalla completa, ventana de tamaño razonable y centrada en el monitor actual
        // (si no, a veces queda del tamaño del monitor con la barra de título fuera de la pantalla).
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var size = new Vector2I(Math.Min(WindowedSize.X, usable.Size.X - 40), Math.Min(WindowedSize.Y, usable.Size.Y - 60));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("pantalla", "completa", Fullscreen);
        cfg.SetValue("pantalla", "vsync", VSync);
        cfg.SetValue("controles", "sensibilidad", MouseSensitivity);
        cfg.SetValue("sonido", "musica", MusicVolume);
        cfg.SetValue("sonido", "efectos", EffectsVolume);
        cfg.SetValue("pantalla", "calidad", Quality);
        cfg.Save(Path);
    }
}
