using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>
/// Ventana del mercado (tecla P), con cuatro hojas: la libreta de precios (lo que viste en las pizarras), el diario del día,
/// lo que sabe Nino (si le pagaste) y el libro de cuentas de tus negocios. Los datos los manda el servidor al abrir.
/// </summary>
public partial class MarketPanel : PanelContainer
{
    public const string Notes = "libreta", Paper = "diario", Report = "informe", Ledger = "libro";

    private static readonly Color Ink = new(0.13f, 0.11f, 0.09f);
    private static readonly Color InkMuted = new(0.35f, 0.31f, 0.26f);
    private static readonly Color PaperColor = new(0.93f, 0.9f, 0.82f);
    private static readonly Color Red = new(0.62f, 0.12f, 0.1f);

    private MarketNetwork _market = null!;
    private readonly Dictionary<string, Button> _tabs = new();
    private VBoxContainer _content = null!;
    private PanelContainer _sheet = null!;
    private string _tab = Notes;
    private string _message = "";

    public bool IsOpen => Visible;

    public void Initialize(MarketNetwork market)
    {
        _market = market;
        _market.InfoReceived += () => { if (Visible) Rebuild(); };
        _market.MessageReceived += text => { _message = text; if (Visible) Rebuild(); };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(900, 0);
        AddThemeStyleboxOverride("panel", EncargoLook.Panel());
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        box.AddChild(tabs);
        foreach (var (code, label) in new[] { (Notes, "Libreta de precios"), (Paper, "El diario"), (Report, "Lo que sabe Nino"), (Ledger, "Mi negocio") })
        {
            var button = new Button { Text = label, ToggleMode = true, CustomMinimumSize = new Vector2(150, 0) };
            button.Pressed += () => ShowTab(code);
            tabs.AddChild(button);
            _tabs[code] = button;
        }

        _sheet = new PanelContainer();
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(870, 520) };
        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_content);
        _sheet.AddChild(scroll);
        box.AddChild(_sheet);

        var close = new Button { Text = "Cerrar (P)" };
        close.Pressed += Toggle;
        box.AddChild(close);
    }

    public void Toggle()
    {
        if (Visible)
            Close();
        else
            Open(_tab);
    }

    public void Open(string tab)
    {
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ShowTab(tab);
        _market.RequestInfo();
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ShowTab(string tab)
    {
        _tab = tab;
        foreach (var (code, button) in _tabs)
            button.ButtonPressed = code == tab;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren())
            child.QueueFree();
        var paper = _tab == Paper;
        _sheet.AddThemeStyleboxOverride("panel", paper
            ? new StyleBoxFlat { BgColor = PaperColor, ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 14, ContentMarginBottom = 14 }
            : new StyleBoxEmpty());
        var info = _market.Info;
        if (info is null)
        {
            _content.AddChild(EncargoLook.Text(Multiplayer.MultiplayerPeer is not null && Multiplayer.IsServer() ? "Esto necesita un servidor con cuentas." : "Cargando…", EncargoLook.Muted));
            return;
        }
        switch (_tab)
        {
            case Paper: BuildPaper(info); break;
            case Report: BuildReport(info); break;
            case Ledger: BuildLedger(info); break;
            default: BuildNotes(info); break;
        }
    }

    private static string Ago(DateTimeOffset at)
    {
        var d = DateTimeOffset.UtcNow - at;
        return d.TotalMinutes < 1 ? "recién" : d.TotalHours < 1 ? $"hace {(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"hace {(int)d.TotalHours} h" : $"hace {(int)d.TotalDays} días";
    }

    private static string Hour(DateTimeOffset at) => at.ToOffset(TerritoryRules.ArgentinaOffset).ToString("HH:mm");

    private static Label Cell(string text, Color color, int width, int size = 0, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = EncargoLook.Text(text, color, width, size);
        l.AutowrapMode = TextServer.AutowrapMode.Off;
        l.HorizontalAlignment = align;
        return l;
    }

    private void Row(params Control[] cells)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        foreach (var c in cells)
            row.AddChild(c);
        _content.AddChild(row);
    }

    // ---------------- Libreta ----------------

    private void BuildNotes(MarketInfoView info)
    {
        _content.AddChild(EncargoLook.Text("Libreta de precios", EncargoLook.Gold, size: 20));
        _content.AddChild(EncargoLook.Text("Cada vez que pasás frente a la pizarra de un local, anotás lo que dice. "
            + $"Lo anotado hace más de {(int)MarketInfoRules.NoteStaleAfter.TotalHours} horas se ve en gris: el precio puede haber cambiado.", EncargoLook.Muted, 860));
        if (info.Notes.Count == 0)
        {
            _content.AddChild(EncargoLook.Text("Todavía no anotaste nada. Las pizarras están al lado del Almacén Moretti, el Taller Caruso, la Esquina del café, "
                + "el Club de jazz y el kiosco de diarios.", EncargoLook.Muted, 860));
            return;
        }

        // Oportunidades: algo que alguien vende más barato de lo que otro compra.
        var now = DateTimeOffset.UtcNow;
        foreach (var group in info.Notes.GroupBy(n => n.ItemCode))
        {
            var cheapest = group.Where(n => !n.Buying && n.InStock).MinBy(n => n.PriceCents);
            var best = group.Where(n => n.Buying).MaxBy(n => n.PriceCents);
            if (cheapest is not null && best is not null && best.PriceCents > cheapest.PriceCents && best.NpcCode != cheapest.NpcCode)
                _content.AddChild(EncargoLook.Text($"Oportunidad: {cheapest.Name} se consigue a {EconomyCatalog.FormatMoney(cheapest.PriceCents)} en {cheapest.Place} "
                    + $"y lo compran a {EconomyCatalog.FormatMoney(best.PriceCents)} en {best.Place}.", EncargoLook.Good, 860));
        }

        foreach (var place in info.Notes.GroupBy(n => n.Place))
        {
            var seen = place.Max(n => n.SeenAt);
            var stale = now - seen > MarketInfoRules.NoteStaleAfter;
            _content.AddChild(new HSeparator());
            Row(Cell(place.Key, stale ? EncargoLook.Muted : EncargoLook.Gold, 420, 17), Cell($"visto {Ago(seen)}{(stale ? " (viejo)" : "")}", EncargoLook.Muted, 300));
            foreach (var n in place.OrderBy(n => n.Buying).ThenBy(n => n.Name))
            {
                var color = stale ? EncargoLook.Muted : n.Buying ? EncargoLook.Good : n.InStock ? Colors.White : EncargoLook.Warn;
                var what = n.Buying ? $"Compran {n.Name.ToLowerInvariant()}" : n.Name;
                Row(Cell("   " + what, color, 420), Cell(n.Buying || n.InStock ? EconomyCatalog.FormatMoney(n.PriceCents) : "AGOTADO", color, 140, align: HorizontalAlignment.Right));
            }
        }
    }

    // ---------------- Diario ----------------

    private void BuildPaper(MarketInfoView info)
    {
        var paper = info.Newspaper ?? info.Teaser;
        if (paper is null)
        {
            _content.AddChild(Cell("Hoy todavía no salió el diario.", InkMuted, 820));
            return;
        }
        var masthead = Cell(MarketInfoRules.PaperName.ToUpperInvariant(), Ink, 820, 34, HorizontalAlignment.Center);
        _content.AddChild(masthead);
        _content.AddChild(Cell($"{paper.DateLine}  ·  Edición N.º {paper.Edition}  ·  Diez centavos", InkMuted, 820, 14, HorizontalAlignment.Center));
        _content.AddChild(Rule());
        var headline = EncargoLook.Text(paper.Headline, Ink, 820, 30);
        headline.HorizontalAlignment = HorizontalAlignment.Center;
        _content.AddChild(headline);
        var lead = EncargoLook.Text(paper.Lead, InkMuted, 820, 16);
        lead.HorizontalAlignment = HorizontalAlignment.Center;
        _content.AddChild(lead);
        _content.AddChild(Rule());

        if (info.Newspaper is null)
        {
            var buy = EncargoLook.Text($"Para leer las noticias, las cotizaciones y los avisos, comprá el diario en el kiosco de la calle principal "
                + $"({EconomyCatalog.FormatMoney(MarketInfoRules.NewspaperPriceCents)}).", Red, 820, 16);
            buy.HorizontalAlignment = HorizontalAlignment.Center;
            _content.AddChild(buy);
            return;
        }

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 18);
        _content.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        left.AddChild(Cell("NOTICIAS DE LA CIUDAD", Ink, 400, 16));
        if (paper.News.Count == 0)
            left.AddChild(EncargoLook.Text("Un día tranquilo en Bellmont.", InkMuted, 400));
        foreach (var n in paper.News)
            left.AddChild(EncargoLook.Text("■ " + n, Ink, 400));
        columns.AddChild(left);
        columns.AddChild(new VSeparator());

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        right.AddChild(Cell("COTIZACIONES DEL MERCADO", Ink, 400, 16));
        foreach (var q in paper.Quotes.Where(q => !q.Buying))
            right.AddChild(QuoteRow(q));
        var buys = paper.Quotes.Where(q => q.Buying).ToList();
        if (buys.Count > 0)
        {
            right.AddChild(Cell("LO QUE PAGAN LOS LOCALES", Ink, 400, 14));
            foreach (var q in buys)
                right.AddChild(QuoteRow(q));
        }
        columns.AddChild(right);

        _content.AddChild(Rule());
        _content.AddChild(Cell("AVISOS CLASIFICADOS", Ink, 820, 16, HorizontalAlignment.Center));
        var ads = new GridContainer { Columns = 2 };
        ads.AddThemeConstantOverride("h_separation", 18);
        ads.AddThemeConstantOverride("v_separation", 8);
        foreach (var a in paper.Classifieds)
            ads.AddChild(EncargoLook.Text(a, Ink, 400, 13));
        _content.AddChild(ads);
    }

    private static Control QuoteRow(NewspaperQuote q)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Cell($"{q.Item} ({q.Place})", Ink, 270, 13));
        row.AddChild(Cell(q.InStock || q.Buying ? EconomyCatalog.FormatMoney(q.PriceCents) : "agotado", q.InStock || q.Buying ? Ink : Red, 80, 13, HorizontalAlignment.Right));
        row.AddChild(Cell(MarketInfoRules.Arrow(q.Trend), q.Trend > 0 ? Red : new Color(0.15f, 0.4f, 0.2f), 20, 13));
        return row;
    }

    private static Control Rule() => new ColorRect { Color = Ink, CustomMinimumSize = new Vector2(820, 2) };

    // ---------------- Nino ----------------

    private void BuildReport(MarketInfoView info)
    {
        _content.AddChild(EncargoLook.Text("Lo que sabe Nino", EncargoLook.Gold, size: 20));
        if (info.Report is not { } report)
        {
            _content.AddChild(EncargoLook.Text($"Nino vende información en un pasaje de la Calle Principal, entre la ferretería y el correo: "
                + $"{EconomyCatalog.FormatMoney(MarketInfoRules.InformantPriceCents)} por una hora. Sabe lo que no sale en el diario: cuánta mercadería le queda "
                + "a cada uno, quién tiene cola de pedidos, qué vende el Taller Caruso por la puerta de atrás y qué fletes andan por la calle.", EncargoLook.Muted, 860));
            return;
        }
        _content.AddChild(EncargoLook.Text($"Datos en vivo hasta las {Hour(report.ValidUntil)} ({EncargoLook.TimeLeft(report.ValidUntil)}). Se actualizan cada vez que abrís esta hoja.",
            EncargoLook.Muted, 860));
        foreach (var topic in report.Lines.GroupBy(l => l.Topic))
        {
            _content.AddChild(new HSeparator());
            _content.AddChild(EncargoLook.Text(topic.Key, EncargoLook.Gold, size: 16));
            foreach (var l in topic)
                _content.AddChild(EncargoLook.Text(l.Text, Colors.White, 860));
        }
    }

    // ---------------- Mi negocio: fama, mejoras y precio ----------------

    private void BuildGrowth(LedgerView l, BusinessGrowthView g)
    {
        var m = EconomyCatalog.FormatMoney;
        // Fama: barra hacia el próximo nivel (sin techo: después de Leyenda siguen las estrellas).
        var fameRow = new HBoxContainer();
        fameRow.AddThemeConstantOverride("separation", 10);
        fameRow.AddChild(Cell($"Fama: {g.FameName}", EncargoLook.Gold, 300, 16));
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, g.NextFameGoal), Value = g.Fame, ShowPercentage = false, CustomMinimumSize = new Vector2(300, 18) };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.2f, 0.18f, 0.15f), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = EncargoLook.Gold, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        fameRow.AddChild(bar);
        fameRow.AddChild(Cell($"{g.Fame:0} / {g.NextFameGoal:0}", EncargoLook.Muted, 160));
        _content.AddChild(fameRow);
        Row(Cell($"Atendés hasta {g.Capacity} por ciclo · último ciclo: {g.LastCustomers}", Colors.White, 420),
            Cell($"Calidad ×{g.Quality:0.00} · Atracción ×{g.Attraction:0.00} · Alquiler {m(g.RentCents)}", Colors.White, 420));
        var eventLocal = g.EventStart.ToOffset(TerritoryRules.ArgentinaOffset);
        var now = DateTimeOffset.UtcNow;
        var when = now >= g.EventStart && now < g.EventEnd ? "¡ahora!" : now >= g.EventEnd ? "ya pasó hoy" : $"hoy a las {eventLocal:HH:mm}";
        _content.AddChild(EncargoLook.Text($"{g.EventName}: {when} (viene el doble de gente; tené mercadería y lugar).", EncargoLook.Good, 860));
        if (g.ReviewStars is { } stars)
            _content.AddChild(EncargoLook.Text($"Última nota de La Gaceta: {new string('★', stars)}{new string('☆', 5 - stars)} {g.ReviewText}",
                stars >= 4 ? EncargoLook.Good : stars <= 2 ? EncargoLook.Warn : Colors.White, 860));

        // Mejoras: tres ramas sin tope; cada nivel cuesta más. Se paga con la caja del negocio y, si falta, con tu bolsillo.
        _content.AddChild(EncargoLook.Text("Mejoras del local (las hace el Taller Caruso)", EncargoLook.Gold, size: 15));
        foreach (var u in g.Upgrades)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(Cell($"{u.BranchName} (nivel {u.Level})", EncargoLook.Gold, 170));
            row.AddChild(EncargoLook.Text($"Ahora: {u.Current} → {u.Next}. {u.Effect}.", Colors.White, 470));
            var buy = new Button { Text = $"Mejorar ({m(u.CostCents)})", CustomMinimumSize = new Vector2(170, 0) };
            var branch = u.Branch;
            buy.Pressed += () => { _message = "Encargando la obra…"; _market.Upgrade(l.Kind, branch); Rebuild(); };
            row.AddChild(buy);
            _content.AddChild(row);
        }

        // Precio: la gente compara con lo que considera justo para tu calidad.
        var priceRow = new HBoxContainer();
        priceRow.AddThemeConstantOverride("separation", 8);
        var fairness = g.FairPriceCents / (double)Math.Max(1, g.PriceCents);
        var verdict = fairness < 0.87 ? "caro para tu calidad" : fairness > 1.15 ? "barato para tu calidad" : "justo";
        priceRow.AddChild(Cell($"Precio: {m(g.PriceCents)} ({verdict}; justo: {m(g.FairPriceCents)})", fairness < 0.87 ? EncargoLook.Warn : Colors.White, 420));
        var step = Math.Max(10, g.FairPriceCents / 20 / 10 * 10);
        foreach (var (label, value) in new[] { ($"− {m(step)}", g.PriceCents - step), ($"+ {m(step)}", g.PriceCents + step), ("Precio justo", g.FairPriceCents) })
        {
            var b = new Button { Text = label, Disabled = value < g.MinPriceCents || value > g.MaxPriceCents || value == g.PriceCents };
            var target = value;
            b.Pressed += () => { _message = "Cambiando el precio…"; _market.SetPrice(l.Kind, target); Rebuild(); };
            priceRow.AddChild(b);
        }
        // Precio libre: también se puede escribir el que quieras.
        var typed = new LineEdit { PlaceholderText = "otro: 7,50", CustomMinimumSize = new Vector2(100, 0) };
        var apply = new Button { Text = "Poner" };
        void Apply()
        {
            if (TryParseMoney(typed.Text, out var cents) && cents >= g.MinPriceCents && cents <= g.MaxPriceCents)
            {
                _message = "Cambiando el precio…";
                _market.SetPrice(l.Kind, cents);
            }
            else
                _message = "Escribí un precio en pesos, por ejemplo 7,50.";
            Rebuild();
        }
        apply.Pressed += Apply;
        typed.TextSubmitted += _ => Apply();
        priceRow.AddChild(typed);
        priceRow.AddChild(apply);
        _content.AddChild(priceRow);
    }

    /// <summary>«7,50», «7.50», «$7» o «7» → centavos.</summary>
    internal static bool TryParseMoney(string text, out long cents)
    {
        cents = 0;
        var clean = text.Trim().TrimStart('$').Trim().Replace(',', '.');
        if (!decimal.TryParse(clean, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return false;
        cents = (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
        return cents > 0;
    }

    // ---------------- Libro de cuentas ----------------

    private void BuildLedger(MarketInfoView info)
    {
        var m = EconomyCatalog.FormatMoney;
        _content.AddChild(EncargoLook.Text("Mi negocio", EncargoLook.Gold, size: 20));
        if (info.Ledgers.Count == 0)
        {
            _content.AddChild(EncargoLook.Text("No tenés negocios. Las licencias para un puesto de café o un club de jazz las da la Oficina Valenti.", EncargoLook.Muted, 860));
            return;
        }
        if (_message.Length > 0)
            _content.AddChild(EncargoLook.Text(_message, EncargoLook.Good, 860));
        foreach (var l in info.Ledgers)
        {
            _content.AddChild(new HSeparator());
            var type = EconomyCatalog.BusinessTypes.GetValueOrDefault(l.Kind);
            Row(Cell($"«{l.Name}»", EncargoLook.Gold, 420, 18), Cell(l.Growth is { } g0 ? $"{g0.Category} · {type?.Name}" : type?.Name ?? "", EncargoLook.Muted, 420));
            if (l.Growth is { } g)
                BuildGrowth(l, g);
            _content.AddChild(EncargoLook.Text("Cuentas", EncargoLook.Gold, size: 15));
            Row(Cell($"Mercadería: {l.Stock} × {type?.ItemName ?? "unidades"}", l.Stock > 0 ? Colors.White : EncargoLook.Warn, 420),
                Cell($"Caja del negocio: {m(l.CashCents)}", Colors.White, 300));
            Row(Cell($"Tu precio: {m(l.SalePriceCents)}  ·  promedio de la zona: {m(l.AveragePriceCents)}", Colors.White, 420),
                Cell($"Costo por unidad: {m(l.UnitCostCents)}  ·  margen: {m(l.SalePriceCents - l.UnitCostCents)}", Colors.White, 420));

            _content.AddChild(EncargoLook.Text("Hoy", EncargoLook.Gold, size: 15));
            Row(Cell($"Vendidas: {l.SoldToday}", Colors.White, 200), Cell($"Ingresos: {m(l.RevenueToday)}", Colors.White, 200),
                Cell($"Mercadería vendida: {m(l.CostOfGoodsToday)}", Colors.White, 260));
            Row(Cell($"Alquiler: {m(l.RentToday)}", Colors.White, 200), Cell($"Sueldos: {m(l.WagesToday)}", Colors.White, 200),
                Cell($"Ganancia: {m(l.ProfitToday)}", l.ProfitToday >= 0 ? EncargoLook.Good : EncargoLook.Warn, 260));
            if (l.LostToday > 0)
                _content.AddChild(EncargoLook.Text($"{l.LostToday} clientes se fueron sin comprar hoy porque no había mercadería. "
                    + $"Eran {m(l.LostToday * (l.SalePriceCents - l.UnitCostCents))} de margen.", EncargoLook.Warn, 860));
            else if (l.WantedToday > 0)
                _content.AddChild(EncargoLook.Text("Hoy atendiste a todos los que vinieron.", EncargoLook.Good, 860));

            if (l.Recent.Count > 0)
            {
                _content.AddChild(EncargoLook.Text("Últimos ciclos", EncargoLook.Gold, size: 15));
                Row(Cell("Hora", EncargoLook.Muted, 70), Cell("Vinieron", EncargoLook.Muted, 90, align: HorizontalAlignment.Right),
                    Cell("Vendidas", EncargoLook.Muted, 90, align: HorizontalAlignment.Right), Cell("Se fueron", EncargoLook.Muted, 90, align: HorizontalAlignment.Right),
                    Cell("Ingresos", EncargoLook.Muted, 110, align: HorizontalAlignment.Right), Cell("Costos", EncargoLook.Muted, 110, align: HorizontalAlignment.Right));
                foreach (var c in l.Recent)
                    Row(Cell(Hour(c.At), Colors.White, 70), Cell(c.Wanted.ToString(), Colors.White, 90, align: HorizontalAlignment.Right),
                        Cell(c.Sold.ToString(), Colors.White, 90, align: HorizontalAlignment.Right),
                        Cell(c.Lost.ToString(), c.Lost > 0 ? EncargoLook.Warn : Colors.White, 90, align: HorizontalAlignment.Right),
                        Cell(m(c.RevenueCents), Colors.White, 110, align: HorizontalAlignment.Right), Cell(m(c.CostsCents), Colors.White, 110, align: HorizontalAlignment.Right));
            }
        }
    }
}
