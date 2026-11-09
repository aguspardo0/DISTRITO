using Distrito.Game.Economy;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// La pantalla de juego, minimalista (pedido del equipo, 2026-11-08): lo justo y siempre en el mismo lugar.
/// <list type="bullet">
/// <item>Arriba al medio, una <b>brújula</b> fina con el objetivo actual (el paso del tutorial o el encargo) marcado
/// con un rombo dorado, y abajo su nombre y la distancia.</item>
/// <item>Abajo a la izquierda, <b>vida</b> (barra fina), <b>plata</b> y la <b>zona</b> donde estás (en rojo si es zona
/// libre).</item>
/// <item>Abajo a la derecha, la <b>hora de Bellmont</b> y, solo con arma en la mano, las balas.</item>
/// <item>Arriba a la derecha, los <b>avisos</b>: aparecen, se leen y se van solos (como mucho tres).</item>
/// </list>
/// Los controles se ven con F1; los datos de rendimiento, con F3.
/// </summary>
public partial class GameHud : Control
{
    public static readonly Color Ink = new(0.95f, 0.91f, 0.82f);
    public static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    public static readonly Color Danger = new(0.92f, 0.38f, 0.3f);
    private static readonly Color Shadow = new(0, 0, 0, 0.55f);

    public Func<Distrito.Game.Player.PlayerCharacter?> Me { get; set; } = () => null;
    public Func<EconomySnapshot?> Snapshot { get; set; } = () => null;
    public Func<string> Ammo { get; set; } = () => "";
    /// <summary>Códigos de NPC a los que hay que ir ahora (el primero es el objetivo de la brújula).</summary>
    public Func<IEnumerable<string>> Objectives { get; set; } = () => [];

    private Label _money = null!, _zone = null!, _clock = null!, _ammo = null!, _objective = null!, _health = null!;
    private VBoxContainer _toasts = null!;
    private PanelContainer _controls = null!;
    private Control? _barSlot;
    private float _heading;
    private float? _bearing;
    private float _life = 1;
    private string _lastZone = "";
    private double _tick;

    public const string ControlsText =
        "W A S D  moverse   ·   Shift  correr   ·   Espacio  saltar\n" +
        "Mouse  mirar   ·   Clic / F  piña o disparo   ·   1–4  arma\n" +
        "E  hablar, usar   ·   G  subir o bajar del auto\n" +
        "M  mapa   ·   L  encargos   ·   I  inventario   ·   P  diario y negocio\n" +
        "C  cuadrilla   ·   J  tablero   ·   K  habilidades\n" +
        "F8  reportar un problema   ·   F3  rendimiento   ·   F11  pantalla completa   ·   Esc  opciones";

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Abajo a la izquierda: vida, plata y zona.
        var left = Pin(new VBoxContainer(), 0, 1, new Vector2(24, -24), Control.GrowDirection.End, Control.GrowDirection.Begin);
        _health = Text(13, Ink);
        left.AddChild(_health);
        _barSlot = new Control { CustomMinimumSize = new Vector2(150, 8), MouseFilter = MouseFilterEnum.Ignore };
        left.AddChild(_barSlot); // la barra se dibuja acá (en _Draw)
        _money = Text(18, Ink);
        left.AddChild(_money);
        _zone = Text(12, new Color(0.78f, 0.74f, 0.66f));
        left.AddChild(_zone);

        // Abajo a la derecha: balas (con arma) y la hora.
        var right = Pin(new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End }, 1, 1, new Vector2(-24, -24), Control.GrowDirection.Begin, Control.GrowDirection.Begin);
        right.CustomMinimumSize = new Vector2(200, 0);
        _ammo = Text(18, Gold, HorizontalAlignment.Right);
        right.AddChild(_ammo);
        _clock = Text(13, Ink, HorizontalAlignment.Right);
        right.AddChild(_clock);

        // Arriba al medio: el objetivo debajo de la brújula.
        _objective = Text(13, Gold, HorizontalAlignment.Center);
        _objective.CustomMinimumSize = new Vector2(400, 0);
        Pin(_objective, 0.5f, 0, new Vector2(0, 50), Control.GrowDirection.Both, Control.GrowDirection.End);

        // Arriba a la derecha: los avisos.
        _toasts = Pin(new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin }, 1, 0, new Vector2(-24, 20), Control.GrowDirection.Begin, Control.GrowDirection.End);
        _toasts.CustomMinimumSize = new Vector2(360, 0);
        _toasts.AddThemeConstantOverride("separation", 6);

        // F1: los controles.
        _controls = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _controls.AddThemeStyleboxOverride("panel", EncargoLook.Panel(0.88f, Gold));
        _controls.AddChild(Text(14, Ink, HorizontalAlignment.Center, ControlsText));
        Pin(_controls, 0.5f, 0.5f, Vector2.Zero, Control.GrowDirection.Both, Control.GrowDirection.Both);

        Notify("F1: ver los controles");
    }

    /// <summary>Clava un control a un punto de la pantalla (0..1) con un corrimiento en píxeles; crece hacia donde se le diga.</summary>
    private T Pin<T>(T box, float ax, float ay, Vector2 offset, Control.GrowDirection growX, Control.GrowDirection growY) where T : Control
    {
        box.MouseFilter = MouseFilterEnum.Ignore;
        box.AnchorLeft = box.AnchorRight = ax;
        box.AnchorTop = box.AnchorBottom = ay;
        box.OffsetLeft = box.OffsetRight = offset.X;
        box.OffsetTop = box.OffsetBottom = offset.Y;
        box.GrowHorizontal = growX;
        box.GrowVertical = growY;
        AddChild(box);
        return box;
    }

    private static Label Text(int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left, string text = "")
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_shadow_color", Shadow);
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
        return l;
    }

    /// <summary>Un aviso arriba a la derecha que se va solo a los pocos segundos.</summary>
    public void Notify(string text, bool important = false)
    {
        if (string.IsNullOrWhiteSpace(text) || _toasts is null)
            return;
        foreach (var child in _toasts.GetChildren())
            if (child is PanelContainer p && p.GetMeta("texto", "").AsString() == text)
                p.QueueFree(); // el mismo aviso repetido no se apila
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.SetMeta("texto", text);
        var style = EncargoLook.Panel(0.72f, important ? Danger : new Color(1, 1, 1, 0.12f));
        style.ContentMarginLeft = style.ContentMarginRight = 12;
        style.ContentMarginTop = style.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", style);
        var label = Text(13, important ? Danger.Lightened(0.3f) : Ink, text: text);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(336, 0);
        panel.AddChild(label);
        _toasts.AddChild(panel);
        while (_toasts.GetChildCount() > 3)
            _toasts.GetChild(0).Free();
        var tween = panel.CreateTween();
        tween.TweenInterval(important ? 8 : 5);
        tween.TweenProperty(panel, "modulate:a", 0f, 0.6f);
        tween.TweenCallback(Callable.From(panel.QueueFree));
    }

    public void ToggleControls() => _controls.Visible = !_controls.Visible;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F1 })
        {
            ToggleControls();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        var me = Me();
        Visible = me is not null;
        if (me is null)
            return;

        // Brújula: hacia dónde mira la cámara y dónde queda el objetivo (el norte es −Z, como en el mapa).
        if (GetViewport().GetCamera3D() is { } cam)
        {
            var f = -cam.GlobalTransform.Basis.Z;
            _heading = Mathf.RadToDeg(Mathf.Atan2(f.X, -f.Z));
        }
        _tick += delta;
        if (_tick >= 0.2)
        {
            _tick = 0;
            Refresh(me);
        }
        QueueRedraw();
    }

    private void Refresh(Distrito.Game.Player.PlayerCharacter me)
    {
        _life = Mathf.Clamp(me.Health / (float)Distrito.Server.Core.Combat.CombatRules.MaxHealth, 0, 1);
        _health.Text = me.Dead ? "Muerto — volvés en unos segundos"
            : me.Downed ? "Herido — esperá que te levante un compañero"
            : $"♥ {me.Health}";
        _health.AddThemeColorOverride("font_color", me.Downed || me.Dead || _life < 0.4f ? Danger : Ink);
        _money.Text = Snapshot() is { } s ? EconomyCatalog.FormatMoney(s.BalanceCents) : "";

        var p = me.VisualPosition;
        var zoneCode = Distrito.Server.Core.Combat.CombatRules.ZoneAt(new System.Numerics.Vector3(p.X, p.Y, p.Z));
        var free = Distrito.Server.Core.World.MapPresence.InDocks(p.X, p.Z);
        var zoneName = Distrito.Server.Core.World.CityLayout.BarrioAt(p.X, p.Z)?.Area.Name ?? TerritoryRules.NameOf(zoneCode);
        _zone.Text = free ? $"{zoneName} · zona libre: te pueden atacar y robar" : zoneName;
        _zone.AddThemeColorOverride("font_color", free ? Danger : new Color(0.78f, 0.74f, 0.66f));
        if (zoneName != _lastZone)
        {
            if (_lastZone.Length > 0 && free)
                Notify("Entraste a Los Muelles: zona libre, acá te pueden atacar y robar.", important: true);
            _lastZone = zoneName;
        }

        var now = DateTimeOffset.UtcNow;
        _clock.Text = $"{Distrito.Shared.Life.BellmontClock.TimeOfDay(now):hh\\:mm} · {(Distrito.Shared.Life.BellmontClock.IsNight(now) ? "noche" : "día")} en Bellmont";
        _ammo.Text = Ammo();

        _bearing = null;
        _objective.Text = "";
        foreach (var code in Objectives())
        {
            if (EncargoLook.PositionOf(code) is not { } at)
                continue;
            var d = new Vector2(at.X - p.X, at.Z - p.Z);
            _bearing = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Y));
            _objective.Text = $"{EncargoLook.Name(code)} · {d.Length():0} m";
            break;
        }
    }

    public override void _Draw()
    {
        if (Me() is not { } me)
            return;
        // Barra de vida (abajo a la izquierda, debajo del número).
        var slot = _barSlot!.GetGlobalRect();
        var bar = new Rect2(slot.Position - GlobalPosition + new Vector2(0, 2), new Vector2(150, 4));
        DrawRect(bar, new Color(1, 1, 1, 0.15f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * _life, bar.Size.Y)), me.Downed || _life < 0.4f ? Danger : Ink);

        // Brújula.
        const float width = 360, span = 90; // se ven 90° a cada lado
        var cx = Size.X / 2;
        const float y = 30;
        DrawLine(new Vector2(cx - width / 2, y), new Vector2(cx + width / 2, y), new Color(1, 1, 1, 0.25f), 1);
        var font = ThemeDB.FallbackFont;
        string[] names = ["N", "NE", "E", "SE", "S", "SO", "O", "NO"];
        for (var a = 0; a < 360; a += 15)
        {
            var rel = Wrap(a - _heading);
            if (Mathf.Abs(rel) > span)
                continue;
            var x = cx + rel / span * (width / 2);
            var major = a % 45 == 0;
            DrawLine(new Vector2(x, y - (major ? 6 : 3)), new Vector2(x, y + (major ? 6 : 3)), new Color(1, 1, 1, major ? 0.7f : 0.3f), 1);
            if (major)
            {
                var label = names[a / 45];
                var size = font.GetStringSize(label, HorizontalAlignment.Left, -1, 12);
                DrawString(font, new Vector2(x - size.X / 2, y - 9), label, HorizontalAlignment.Left, -1, 12, a == 0 ? Gold : Ink);
            }
        }
        // El objetivo: un rombo dorado; si queda atrás, una flecha en el borde hacia donde hay que girar.
        if (_bearing is { } b)
        {
            var rel = Wrap(b - _heading);
            var x = cx + Mathf.Clamp(rel, -span, span) / span * (width / 2);
            if (Mathf.Abs(rel) <= span)
                DrawColoredPolygon([new(x, y - 6), new(x + 5, y), new(x, y + 6), new(x - 5, y)], Gold);
            else
            {
                var dir = Mathf.Sign(rel);
                DrawColoredPolygon([new(x + dir * 8, y), new(x - dir * 2, y - 6), new(x - dir * 2, y + 6)], Gold);
            }
        }
    }

    private static float Wrap(float degrees)
    {
        degrees %= 360;
        if (degrees > 180) degrees -= 360;
        if (degrees < -180) degrees += 360;
        return degrees;
    }
}
