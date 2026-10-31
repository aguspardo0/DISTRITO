using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// Menú de opciones en partida (Esc): seguir jugando, pantalla, sensibilidad, salir al menú o salir del juego.
/// No pausa el mundo: es un juego en línea, el resto sigue jugando.
/// </summary>
public partial class PauseMenu : Control
{
    private CheckButton _fullscreen = null!;
    private CheckButton _vsync = null!;
    private HSlider _sensitivity = null!;

    public event Action? LeaveRequested;
    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // tapa el juego: los clics no capturan el mouse

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0) };
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        var title = new Label { Text = "Opciones", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(title);

        box.AddChild(Button("Seguir jugando", Close));

        _fullscreen = new CheckButton { Text = "Pantalla completa (F11 / Alt+Enter)" };
        _fullscreen.Toggled += GameSettings.SetFullscreen;
        box.AddChild(_fullscreen);

        _vsync = new CheckButton { Text = "Sincronización vertical" };
        _vsync.Toggled += on => { if (on != GameSettings.VSync) GameSettings.SetVSync(on); };
        box.AddChild(_vsync);

        box.AddChild(new Label { Text = "Sensibilidad del mouse" });
        _sensitivity = new HSlider { MinValue = 0.0005, MaxValue = 0.01, Step = 0.0005, CustomMinimumSize = new Vector2(300, 0) };
        _sensitivity.ValueChanged += v => GameSettings.SetMouseSensitivity((float)v);
        box.AddChild(_sensitivity);

        box.AddChild(new HSeparator());
        box.AddChild(Button("Salir de la partida (volver al menú)", () => { Close(); LeaveRequested?.Invoke(); }));
        box.AddChild(Button("Salir del juego", () => GetTree().Quit()));
    }

    public void Open()
    {
        _fullscreen.SetPressedNoSignal(DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen);
        _vsync.SetPressedNoSignal(GameSettings.VSync);
        _sensitivity.SetValueNoSignal(GameSettings.MouseSensitivity);
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Notification(int what)
    {
        // Al refrescar la ventana después de cambiar el modo de pantalla, el estado del botón sigue al real.
        if (what == NotificationVisibilityChanged && Visible && _fullscreen is not null)
            _fullscreen.SetPressedNoSignal(GameSettings.Fullscreen);
    }

    private static Button Button(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }
}
