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

    public static bool IsHeadless => DisplayServer.GetName() == "headless";

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            Fullscreen = (bool)cfg.GetValue("pantalla", "completa", false);
            VSync = (bool)cfg.GetValue("pantalla", "vsync", true);
            MouseSensitivity = Mathf.Clamp((float)cfg.GetValue("controles", "sensibilidad", 0.003f), 0.0005f, 0.01f);
        }
        ApplyDisplay();
    }

    public static void SetFullscreen(bool on) { Fullscreen = on; ApplyDisplay(); Save(); }
    public static void ToggleFullscreen() => SetFullscreen(!Fullscreen);
    public static void SetVSync(bool on) { VSync = on; ApplyDisplay(); Save(); }
    public static void SetMouseSensitivity(float value) { MouseSensitivity = Mathf.Clamp(value, 0.0005f, 0.01f); Save(); }

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
        cfg.Save(Path);
    }
}
