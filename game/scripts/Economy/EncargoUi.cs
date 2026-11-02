using Distrito.Server.Core.Npcs;
using Distrito.Server.Core.World;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>Colores y textos compartidos por la libreta, el seguimiento y los marcadores de encargos.</summary>
public static class EncargoLook
{
    public static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    public static readonly Color Muted = new(0.72f, 0.68f, 0.6f);
    public static readonly Color Good = new(0.6f, 0.85f, 0.55f);
    public static readonly Color Warn = new(0.92f, 0.5f, 0.38f);

    public static Color FamilyColor(string family) =>
        CityLayout.BarrioOf(family) is { } b ? new Color(b.Accent.R, b.Accent.G, b.Accent.B) : Gold;

    public static string Name(string code) => NpcInteractionRules.DisplayName(code);

    /// <summary>Dónde está alguien, para orientar: «Tano Ricci (Barrio Moretti)».</summary>
    public static string Where(string code)
    {
        if (NpcInteractionRules.Find(code) is not { } npc)
            return Name(code);
        var zone = CityLayout.BarrioAt(npc.Position.X, npc.Position.Z)?.Area.Name
                   ?? (CityLayout.Docks.Contains(npc.Position.X, npc.Position.Z) ? "Los Muelles" : "El Mercado");
        return $"{Name(code)} ({zone})";
    }

    public static Vector3? PositionOf(string? code) =>
        code is not null && NpcInteractionRules.Find(code) is { } npc ? new Vector3(npc.Position.X, 0, npc.Position.Z) : null;

    public static string TimeLeft(DateTimeOffset until)
    {
        var left = until - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero) return "vencido";
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours} h {left.Minutes:00} min" : $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    public static StyleBoxFlat Panel(float alpha = 0.95f, Color? border = null) => new()
    {
        BgColor = new Color(0.09f, 0.08f, 0.07f, alpha), BorderColor = border ?? new Color(0.55f, 0.43f, 0.22f),
        BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
    };

    public static Label Text(string text, Color? color = null, int width = 0, int size = 0)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (width > 0) label.CustomMinimumSize = new Vector2(width, 0);
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        if (size > 0) label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}

/// <summary>
/// Libreta de encargos (tecla L): los que tenés en curso (qué hacer ahora, cuánto falta, cuánto paga, abandonar) y los
/// que se ofrecen, con quién los da y dónde está. Los encargos se toman hablando con esa persona.
/// </summary>
public partial class EncargoPanel : PanelContainer
{
    private EncargoNetwork _encargos = null!;
    private Func<EconomySnapshot?> _snapshot = () => null;
    private VBoxContainer _active = null!;
    private VBoxContainer _offers = null!;
    private Label _footer = null!;
    private Label _message = null!;
    private double _tick;

    public bool IsOpen => Visible;

    /// <summary>Datos de ejemplo para las capturas de prueba (sin servidor).</summary>
    public Func<EconomySnapshot?>? PreviewData { get; set; }

    public void Initialize(EncargoNetwork encargos, EconomyNetwork economy)
    {
        _encargos = encargos;
        _snapshot = () => PreviewData?.Invoke() ?? economy.LastSnapshot;
        economy.EconomyChanged += _ => { if (Visible) Rebuild(); };
        encargos.MessageReceived += text => { if (Visible) _message.Text = text; };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(860, 0);
        AddThemeStyleboxOverride("panel", EncargoLook.Panel());
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);
        var title = EncargoLook.Text("Libreta de encargos", EncargoLook.Gold, size: 20);
        box.AddChild(title);
        box.AddChild(EncargoLook.Text("Los encargos los da la gente de cada familia, en su barrio. Hablá con ellos para tomarlos; "
            + "el capo recibe recién cuando la familia confía en vos.", EncargoLook.Muted, 830));

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 14);
        box.AddChild(columns);
        columns.AddChild(Column("En curso", out _active, 360));
        columns.AddChild(new VSeparator());
        columns.AddChild(Column("Trabajo disponible", out _offers, 450));

        _message = EncargoLook.Text("", EncargoLook.Good, 830);
        box.AddChild(_message);
        _footer = EncargoLook.Text("", EncargoLook.Muted, 830);
        box.AddChild(_footer);
        var close = new Button { Text = "Cerrar (L)" };
        close.Pressed += Toggle;
        box.AddChild(close);
    }

    private static Control Column(string heading, out VBoxContainer list, int width)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        column.AddChild(EncargoLook.Text(heading, EncargoLook.Gold, size: 16));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(width, 400) };
        list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);
        column.AddChild(scroll);
        return column;
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _message.Text = "";
            Rebuild();
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _tick += delta;
        if (_tick >= 1) // los relojes de los encargos
        {
            _tick = 0;
            UpdateFooter();
        }
    }

    private void UpdateFooter()
    {
        var next = EncargoRules.SlotEnd(EncargoRules.SlotOf(DateTimeOffset.UtcNow));
        _footer.Text = $"Encargos nuevos en {EncargoLook.TimeLeft(next)} · el del día cambia a medianoche · hasta {EncargoRules.MaxActive} en curso. "
                       + "Asaltos y repartos se desbloquean en nivel 3; paquetes y contrarreloj, en nivel 2.";
    }

    private void Rebuild()
    {
        foreach (var child in _active.GetChildren()) child.QueueFree();
        foreach (var child in _offers.GetChildren()) child.QueueFree();
        UpdateFooter();
        var s = _snapshot();
        if (s?.Encargos is null)
        {
            _active.AddChild(EncargoLook.Text(Multiplayer.IsServer() ? "Los encargos necesitan un servidor con cuentas." : "Cargando…", EncargoLook.Muted));
            return;
        }

        var active = s.ActiveEncargos.ToList();
        if (active.Count == 0)
            _active.AddChild(EncargoLook.Text("No tenés encargos en curso. Elegí uno de la derecha y andá a hablar con quien lo da.", EncargoLook.Muted, 350));
        foreach (var e in active)
            _active.AddChild(ActiveCard(e));

        var offers = s.OfferedEncargos.OrderByDescending(e => e.Daily).ThenBy(e => e.Family).ToList();
        if (offers.Count == 0)
            _offers.AddChild(EncargoLook.Text("Por ahora no hay más trabajo. Volvé cuando se renueve.", EncargoLook.Muted, 440));
        foreach (var e in offers)
            _offers.AddChild(OfferCard(e, s));
    }

    private Control ActiveCard(EncargoView e)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", EncargoLook.Panel(0.6f, EncargoLook.FamilyColor(e.Family)));
        var box = new VBoxContainer();
        card.AddChild(box);
        box.AddChild(EncargoLook.Text(e.Title, EncargoLook.FamilyColor(e.Family).Lightened(0.3f), 330, 15));
        box.AddChild(EncargoLook.Text($"Ahora: {e.StepText(EncargoLook.Name)}", null, 330));
        if (EncargoKinds.IsEvent(e.Kind) && e.Stage == 0)
            box.AddChild(new ProgressBar { MaxValue = e.Target, Value = e.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(320, 8) });
        var time = e.FastUntil is { } fast && fast > DateTimeOffset.UtcNow
            ? $"Premio por rapidez: {EncargoLook.TimeLeft(fast)} · vence en {EncargoLook.TimeLeft(e.ExpiresAt)}"
            : $"Vence en {EncargoLook.TimeLeft(e.ExpiresAt)}";
        box.AddChild(EncargoLook.Text(time, e.ExpiresAt - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(3) ? EncargoLook.Warn : EncargoLook.Muted, 330));
        box.AddChild(EncargoLook.Text($"Paga {EconomyCatalog.FormatMoney(e.RewardCents)} · +{e.Xp} XP · +{e.Reputation} con los {EncargoGenerator.FamilyName(e.Family)}",
            EncargoLook.Good, 330));
        var abandon = new Button { Text = "Abandonar" };
        abandon.Pressed += () =>
        {
            _encargos.Abandon(e.Id);
            _message.Text = "Dejando el encargo…";
        };
        box.AddChild(abandon);
        return card;
    }

    private static Control OfferCard(EncargoView e, EconomySnapshot s)
    {
        var card = new PanelContainer();
        var border = e.Daily ? EncargoLook.Gold : EncargoLook.FamilyColor(e.Family).Darkened(0.2f);
        card.AddThemeStyleboxOverride("panel", EncargoLook.Panel(e.Daily ? 0.8f : 0.5f, border));
        var box = new VBoxContainer();
        card.AddChild(box);
        var head = $"{(e.Story ? "◆ Historia — " : e.Daily ? "★ " : "")}{e.Title}";
        box.AddChild(EncargoLook.Text(head, e.Daily ? EncargoLook.Gold : EncargoLook.FamilyColor(e.Family).Lightened(0.3f), 420, 15));
        box.AddChild(EncargoLook.Text(e.Detail, null, 420));
        box.AddChild(EncargoLook.Text($"{EncargoKinds.Label(e.Kind)} · lo da {EncargoLook.Where(e.GiverNpc)}", EncargoLook.Muted, 420));
        box.AddChild(EncargoLook.Text($"Paga {EconomyCatalog.FormatMoney(e.RewardCents)} · +{e.Xp} XP · +{e.Reputation} de reputación "
                                      + $"(tenés {s.ReputationWith(e.Family)}; el capo recibe desde {FamilyPeople.BossReputation})", EncargoLook.Good, 420));
        return card;
    }
}

/// <summary>Seguimiento en pantalla (arriba a la derecha): qué hacer ahora en cada encargo, a cuántos metros y el reloj.</summary>
public partial class EncargoTracker : PanelContainer
{
    private Func<EconomySnapshot?> _snapshot = () => null;
    private Func<Vector3?> _me = () => null;
    private VBoxContainer _box = null!;
    private double _tick;

    /// <summary>Se esconde mientras esto dé verdadero (por ejemplo, con la libreta abierta, que ya muestra lo mismo).</summary>
    public Func<bool> HiddenWhen { get; set; } = () => false;

    public void Initialize(EconomyNetwork economy, Func<Vector3?> me, Func<EconomySnapshot?>? preview = null)
    {
        _snapshot = () => preview?.Invoke() ?? economy.LastSnapshot;
        _me = me;
        economy.EconomyChanged += _ => Refresh();
    }

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", EncargoLook.Panel(0.72f));
        _box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _box.AddThemeConstantOverride("separation", 6);
        AddChild(_box);
    }

    public override void _Process(double delta)
    {
        _tick += delta;
        if (_tick < 0.5)
            return;
        _tick = 0;
        Refresh();
    }

    public void Refresh()
    {
        var active = _snapshot()?.ActiveEncargos.ToList() ?? [];
        Visible = active.Count > 0 && !HiddenWhen();
        foreach (var child in _box.GetChildren()) child.QueueFree();
        if (active.Count == 0)
            return;
        _box.AddChild(EncargoLook.Text("ENCARGOS  ·  [L] libreta", EncargoLook.Gold, size: 12));
        var me = _me();
        foreach (var e in active)
        {
            var color = EncargoLook.FamilyColor(e.Family);
            _box.AddChild(EncargoLook.Text(e.Title, color.Lightened(0.3f), 300, 14));
            var step = e.StepText(EncargoLook.Name);
            if (EncargoLook.PositionOf(e.NextNpc) is { } target && me is { } here)
                step += $"  ({new Vector2(target.X - here.X, target.Z - here.Z).Length():0} m)";
            _box.AddChild(EncargoLook.Text(step, null, 300, 13));
            var clock = e.FastUntil is { } fast && fast > DateTimeOffset.UtcNow
                ? $"Premio por rapidez: {EncargoLook.TimeLeft(fast)}"
                : $"Vence en {EncargoLook.TimeLeft(e.ExpiresAt)}";
            _box.AddChild(EncargoLook.Text(clock, e.ExpiresAt - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(3) ? EncargoLook.Warn : EncargoLook.Muted, 300, 12));
        }
    }
}

/// <summary>Marcadores en el mundo 3D: un rombo de luz sobre la persona o el lugar al que hay que ir ahora.</summary>
public partial class EncargoMarkers : Node3D
{
    private Func<EconomySnapshot?> _snapshot = () => null;
    private readonly Dictionary<string, Node3D> _markers = new();
    private double _time;

    public void Initialize(EconomyNetwork economy, Func<EconomySnapshot?>? preview = null)
    {
        _snapshot = () => preview?.Invoke() ?? economy.LastSnapshot;
        economy.EconomyChanged += _ => Refresh();
    }

    /// <summary>Otros destinos a marcar (los del tutorial), en dorado.</summary>
    public Func<IEnumerable<string>> Extra { get; set; } = () => [];

    public void Refresh()
    {
        var wanted = (_snapshot()?.ActiveEncargos ?? []).Where(e => e.NextNpc is not null)
            .GroupBy(e => e.NextNpc!).ToDictionary(g => g.Key, g => (EncargoView?)g.First());
        foreach (var code in Extra())
            wanted.TryAdd(code, null);
        foreach (var gone in _markers.Keys.Except(wanted.Keys).ToList())
        {
            _markers[gone].QueueFree();
            _markers.Remove(gone);
        }
        foreach (var (code, e) in wanted)
        {
            if (_markers.ContainsKey(code) || EncargoLook.PositionOf(code) is not { } at)
                continue;
            var color = e is null ? EncargoLook.Gold : EncargoLook.FamilyColor(e.Family);
            var marker = new Node3D { Name = $"Marker_{code.Replace(':', '_')}", Position = at };
            var glow = new StandardMaterial3D
            {
                AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 2.5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            marker.AddChild(new MeshInstance3D { Name = "Diamond", Mesh = new PrismMesh { Size = new Vector3(0.5f, 0.6f, 0.5f) }, MaterialOverride = glow, Position = new Vector3(0, 3.25f, 0), RotationDegrees = new Vector3(180, 0, 0) });
            // Columna de luz que se ve de lejos, entre los edificios.
            var beam = new StandardMaterial3D
            {
                AlbedoColor = color with { A = 0.22f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            marker.AddChild(new MeshInstance3D { Name = "Beam", Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.18f, Height = 30f }, MaterialOverride = beam, Position = new Vector3(0, 18f, 0) });
            marker.AddChild(new OmniLight3D { LightColor = color, LightEnergy = 1.2f, OmniRange = 5f, Position = new Vector3(0, 3f, 0) });
            AddChild(marker);
            _markers[code] = marker;
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        foreach (var m in _markers.Values)
            if (m.GetNodeOrNull<Node3D>("Diamond") is { } d)
            {
                d.Position = new Vector3(0, 3.25f + 0.15f * Mathf.Sin((float)_time * 2.5f), 0);
                d.RotateY((float)delta * 1.8f);
            }
    }
}

/// <summary>Aviso grande al cumplir un encargo: título, lo que cobraste y si subiste de nivel o el capo te quiere ver.</summary>
public partial class RewardBanner : PanelContainer
{
    private Label _title = null!;
    private Label _body = null!;
    private double _hideAt, _clock;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(520, 0);
        AddThemeStyleboxOverride("panel", EncargoLook.Panel(0.92f, EncargoLook.Gold));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);
        _title = EncargoLook.Text("ENCARGO CUMPLIDO", EncargoLook.Gold, 500, 24);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_title);
        _body = EncargoLook.Text("", null, 500, 15);
        _body.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_body);
    }

    /// <summary>Muestra el aviso si el texto es el de un encargo cumplido (lo arma el backend).</summary>
    public void ShowIfCompleted(string text)
    {
        var at = text.IndexOf("¡Encargo cumplido!", StringComparison.Ordinal);
        if (at < 0)
            return;
        var lines = text[at..].Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        _body.Text = string.Join("\n", lines);
        Visible = true;
        _hideAt = _clock + 6;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (Visible && _clock >= _hideAt)
            Visible = false;
    }
}
