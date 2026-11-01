using Godot;

namespace Distrito.Game.Admin;

/// <summary>Panel de administración (F10): plata, objetos, nivel, teletransporte y curarse. Solo cuentas admin.</summary>
public partial class AdminPanel : PanelContainer
{
    private static readonly Color Accent = new(0.55f, 0.75f, 0.95f);

    private AdminNetwork _admin = null!;
    private VBoxContainer _content = null!;
    private ScrollContainer _scroll = null!;

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        var height = Mathf.Min(_content.GetCombinedMinimumSize().Y, GetViewportRect().Size.Y - 200);
        if (!Mathf.IsEqualApprox(_scroll.CustomMinimumSize.Y, height))
            _scroll.CustomMinimumSize = new Vector2(350, height);
    }
    private Label _message = null!;

    public bool IsOpen => Visible;

    public void Initialize(AdminNetwork admin)
    {
        _admin = admin;
        _admin.StatusReceived += () => { if (Visible) Rebuild(); };
        _admin.MessageReceived += text => _message.Text = text;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(380, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.08f, 0.1f, 0.95f), BorderColor = Accent,
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);
        var title = new Label { Text = "Administración (pruebas)" };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", Accent);
        box.AddChild(title);
        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 6);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.AddChild(_content);
        box.AddChild(_scroll);
        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) };
        _message.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 0.75f));
        box.AddChild(_message);
        var close = new Button { Text = "Cerrar (F10)" };
        close.Pressed += Toggle;
        box.AddChild(close);
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _message.Text = "";
            _admin.RequestStatus();
            Rebuild();
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren()) child.QueueFree();
        if (_admin.Status is not { } status)
        {
            _content.AddChild(new Label { Text = Multiplayer.IsServer() ? "Necesita un servidor con cuentas." : "Verificando la cuenta…" });
            return;
        }
        if (!status.IsAdmin)
        {
            _content.AddChild(new Label
            {
                Text = "Tu cuenta no es de administración. Agregala en config/admins.json (o Admin__Accounts en el servidor).",
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0),
            });
            return;
        }

        Section("Plata");
        var money = new HBoxContainer();
        foreach (var dollars in new[] { 100, 1_000, 10_000 })
        {
            var b = new Button { Text = $"+${dollars:N0}".Replace(',', '.') };
            b.Pressed += () => _admin.Grant(dollars * 100L, "", 0, 0);
            money.AddChild(b);
        }
        _content.AddChild(money);

        Section("Objetos");
        var items = new HBoxContainer();
        var which = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var item in status.Items)
            which.AddItem(item.Name);
        items.AddChild(which);
        var qty = new SpinBox { MinValue = 1, MaxValue = 1000, Value = 10, Rounded = true, CustomMinimumSize = new Vector2(80, 0) };
        items.AddChild(qty);
        var give = new Button { Text = "Dar" };
        give.Pressed += () =>
        {
            if (which.Selected >= 0 && which.Selected < status.Items.Count)
                _admin.Grant(0, status.Items[which.Selected].ItemCode, (int)qty.Value, 0);
        };
        items.AddChild(give);
        _content.AddChild(items);

        Section("Nivel");
        var levelRow = new HBoxContainer();
        var level = new SpinBox { MinValue = 1, MaxValue = 30, Value = 8, Rounded = true, CustomMinimumSize = new Vector2(80, 0) };
        levelRow.AddChild(level);
        var setLevel = new Button { Text = "Poner nivel" };
        setLevel.Pressed += () => _admin.Grant(0, "", 0, (int)level.Value);
        levelRow.AddChild(setLevel);
        _content.AddChild(levelRow);

        Section("Ir a");
        var places = new HFlowContainer();
        for (var i = 0; i < AdminNetwork.Places.Length; i++)
        {
            var index = i;
            var b = new Button { Text = AdminNetwork.Places[i].Name };
            b.Pressed += () => _admin.Teleport(index);
            places.AddChild(b);
        }
        _content.AddChild(places);

        var heal = new Button { Text = "Curarme (salud al máximo)" };
        heal.Pressed += _admin.Heal;
        _content.AddChild(heal);

        Section("Cuadrilla");
        var crew = new HFlowContainer();
        foreach (var (label, amount) in new[] { ("+100 prestigio", 100), ("+500", 500), ("+1500 (rango 6)", 1500) })
        {
            var b = new Button { Text = label };
            b.Pressed += () => _admin.Tool("prestigio", amount);
            crew.AddChild(b);
        }
        var board = new Button { Text = "Tablero nuevo" };
        board.Pressed += () => _admin.Tool("tablero", 1);
        crew.AddChild(board);
        _content.AddChild(crew);

        Section("Tiempo");
        var time = new HFlowContainer();
        foreach (var (label, amount) in new[] { ("Adelantar 1 ciclo", 1), ("12 ciclos (1 h)", 12) })
        {
            var b = new Button { Text = label, TooltipText = "Corre ventas de tu negocio, alquileres y envíos como si hubiera pasado ese tiempo" };
            b.Pressed += () => _admin.Tool("ciclos", amount);
            time.AddChild(b);
        }
        var orders = new Button { Text = "Pedidos listos ya" };
        orders.Pressed += () => _admin.Tool("pedidos", 1);
        time.AddChild(orders);
        _content.AddChild(time);

        Section("Eventos ahora");
        var events = new HFlowContainer();
        foreach (var (label, code) in new[] { ("Camión de reparto", "camion"), ("Cobrador", "cobrador"), ("Contrabando", "contrabando") })
        {
            var b = new Button { Text = label };
            b.Pressed += () => _admin.Tool($"evento-{code}", 1);
            events.AddChild(b);
        }
        _content.AddChild(events);
    }

    private void Section(string text)
    {
        var label = new Label { Text = text.ToUpperInvariant() };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(0.65f, 0.7f, 0.78f));
        _content.AddChild(label);
    }
}
