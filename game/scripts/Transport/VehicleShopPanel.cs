using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Transport;

/// <summary>Compra de vehículos en el Taller Caruso: propios o, el jefe, para la cuadrilla con la caja.</summary>
public partial class VehicleShopPanel : PanelContainer
{
    private static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    private static readonly Color Muted = new(0.72f, 0.68f, 0.6f);

    private VehicleNetwork _vehicles = null!;
    private Func<EconomySnapshot?> _snapshot = () => null;
    private VBoxContainer _content = null!;
    private Label _message = null!;

    public bool IsOpen => Visible;

    public void Initialize(VehicleNetwork vehicles, Func<EconomySnapshot?> snapshot)
    {
        _vehicles = vehicles;
        _snapshot = snapshot;
        _vehicles.MessageReceived += text => { if (Visible) _message.Text = text; };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(520, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.07f, 0.94f), BorderColor = new Color(0.55f, 0.43f, 0.22f),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);
        var title = new Label { Text = "Vehículos — Taller Caruso" };
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", Gold);
        box.AddChild(title);
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 8);
        box.AddChild(_content);
        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0) };
        _message.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 0.75f));
        box.AddChild(_message);
        var close = new Button { Text = "Cerrar (Esc)" };
        close.Pressed += Close;
        box.AddChild(close);
    }

    public void Open()
    {
        Visible = true;
        _message.Text = "";
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Rebuild();
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren()) child.QueueFree();
        var level = _snapshot()?.ProgressOrEmpty.Level ?? 1;
        var friend = _snapshot()?.StoryDone(EconomyCatalog.Caruso) ?? false; // premio de la historia de los Caruso
        var info = new Label
        {
            Text = "Los maneja el dueño o cualquiera de su cuadrilla (G para subir y bajar). La carga queda adentro al bajarte; "
                   + "donde se permiten robos te la pueden forzar. El vehículo no se roba.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0),
        };
        info.AddThemeColorOverride("font_color", Muted);
        _content.AddChild(info);
        foreach (var type in Vehicles.All)
        {
            _content.AddChild(new HSeparator());
            var price = friend ? type.PriceCents * (100 - FamilyStories.CarusoVehicleDiscountPercent) / 100 : type.PriceCents;
            var name = new Label { Text = friend
                ? $"{type.Name} — {EconomyCatalog.FormatMoney(price)} (precio de amigo; antes {EconomyCatalog.FormatMoney(type.PriceCents)})"
                : $"{type.Name} — {EconomyCatalog.FormatMoney(price)}" };
            name.AddThemeFontSizeOverride("font_size", 16);
            name.AddThemeColorOverride("font_color", Gold);
            _content.AddChild(name);
            var facts = new Label { Text = $"Nivel {type.MinLevel} · carga {type.CapacityKg:0} kg · hasta {type.MaxSpeed * 3.6f:0} km/h" };
            facts.AddThemeColorOverride("font_color", Muted);
            _content.AddChild(facts);
            var row = new HBoxContainer();
            var mine = new Button { Text = "Comprar (propia)", Disabled = level < type.MinLevel };
            var code = type.Code;
            mine.Pressed += () => _vehicles.Buy(code, forCrew: false);
            row.AddChild(mine);
            var crew = new Button { Text = "Para la cuadrilla (caja)", Disabled = level < type.MinLevel, TooltipText = "Solo el jefe" };
            crew.Pressed += () => _vehicles.Buy(code, forCrew: true);
            row.AddChild(crew);
            if (level < type.MinLevel)
                row.AddChild(new Label { Text = $"  (te falta nivel {type.MinLevel})" });
            _content.AddChild(row);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
