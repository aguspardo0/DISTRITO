namespace Distrito.Shared.Economy;

// Lo que el jugador ve del mercado (aprobado por el equipo, 2026-11-02): pizarras en los locales, la libreta de precios,
// el diario de Bellmont, los informantes y el libro de cuentas de los negocios. La radio quedó afuera.

/// <summary>Una línea de pizarra. <see cref="Buying"/>: el local compra ese producto a ese precio (venta a NPC).</summary>
public sealed record PriceLine(string ItemCode, string Name, long PriceCents, bool InStock, int Trend = 0, bool Buying = false);

/// <summary>Pizarra de un local: lo que vende y lo que compra, igual para todos (los descuentos personales van en el mostrador).</summary>
/// <summary><see cref="Crowd"/>: clientes atendidos en el último ciclo (la gente que se ve en el local).</summary>
public sealed record PriceBoardView(string NpcCode, string Place, List<PriceLine> Lines, DateTimeOffset At, string? Note = null, int Crowd = 0);

/// <summary>Un precio anotado en la libreta: lo que decía la pizarra cuando el jugador pasó.</summary>
public sealed record PriceNote(string NpcCode, string Place, string ItemCode, string Name, long PriceCents, bool InStock, bool Buying, DateTimeOffset SeenAt)
{
    public bool IsStale(DateTimeOffset now) => now - SeenAt > MarketInfoRules.NoteStaleAfter;
}

public sealed record NewspaperQuote(string Item, string Place, long PriceCents, int Trend, bool InStock, bool Buying);

/// <summary>Una edición del diario: sale una por día (hora de Argentina) y es igual para todos.</summary>
public sealed record NewspaperView(DateOnly Day, int Edition, string DateLine, string Headline, string Lead, List<string> News,
    List<NewspaperQuote> Quotes, List<string> Classifieds);

public sealed record InformantLine(string Topic, string Text);

/// <summary>Lo que sabe el informante. Mientras el pago está vigente, los datos son en vivo.</summary>
public sealed record InformantReport(DateTimeOffset ValidUntil, List<InformantLine> Lines);

public sealed record LedgerCycle(DateTimeOffset At, int Sold, int Wanted, long RevenueCents, long CostsCents)
{
    public int Lost => Math.Max(0, Wanted - Sold);
}

/// <summary>Libro de cuentas de un negocio propio: todo lo de hoy y los últimos ciclos.</summary>
public sealed record LedgerView(Guid BusinessId, string Name, string Kind, int Stock, long CashCents, long SalePriceCents, long UnitCostCents,
    long AveragePriceCents, int SoldToday, int WantedToday, long RevenueToday, long RentToday, long WagesToday, List<LedgerCycle> Recent,
    BusinessGrowthView? Growth = null)
{
    public int LostToday => Math.Max(0, WantedToday - SoldToday);
    public long CostOfGoodsToday => SoldToday * UnitCostCents;
    public long ProfitToday => RevenueToday - RentToday - WagesToday - CostOfGoodsToday;
}

/// <summary>Todo lo del mercado que puede ver un personaje (se pide al abrir la libreta, el diario, el informe o el libro).</summary>
public sealed record MarketInfoView(List<PriceNote> Notes, NewspaperView? Newspaper, NewspaperView? Teaser, InformantReport? Report, List<LedgerView> Ledgers);

public sealed record MarketLookRequest(Guid CharacterId, string NpcCode);

public sealed record MarketBuyRequest(Guid CharacterId, string What, string RequestId);

public sealed record OpenBusinessFact(string Name, string KindName, string Place, bool HasHelper, bool InStock);

/// <summary>Lo que pasó en Bellmont, para escribir el diario del día.</summary>
public sealed record NewspaperFacts(
    DateOnly Day,
    List<PriceBoardView> Boards,
    Dictionary<string, long> PreviousPrices,
    List<FamilyMoodView> Families,
    int Robberies,
    int VehiclesForced,
    List<string> NewBusinesses,
    List<OpenBusinessFact> OpenBusinesses,
    List<ZoneView> Zones,
    List<EventView> Events,
    int Deliveries,
    int SupplierOrders,
    List<string>? Reviews = null,
    List<string>? Agenda = null,
    List<string>? Famous = null,
    string? BestReviewed = null);

public static class MarketInfoRules
{
    public const string KioskNpc = "npc:kiosco";
    public const string KioskName = "Kiosco de diarios";
    public const string InformantNpc = "npc:informante";
    public const string InformantName = "Nino, el que sabe";

    public const string Newspaper = "diario";
    public const string Informant = "informe";

    public const long NewspaperPriceCents = 10;     // diez centavos
    public const long InformantPriceCents = 500;    // cinco dólares
    public static readonly TimeSpan ReportDuration = TimeSpan.FromHours(1);

    /// <summary>Después de esto la anotación de la libreta se ve en gris: el precio puede haber cambiado.</summary>
    public static readonly TimeSpan NoteStaleAfter = TimeSpan.FromHours(6);

    /// <summary>Distancia a la que se lee una pizarra (la ve el servidor y la anota en la libreta).</summary>
    public const float BoardReadRange = 6f;

    public const string PaperName = "La Gaceta de Bellmont";

    public static readonly DateOnly FirstEdition = new(2026, 11, 2);

    /// <summary>Día del diario: el de Argentina.</summary>
    public static DateOnly DayOf(DateTimeOffset when) => DateOnly.FromDateTime(when.ToOffset(TerritoryRules.ArgentinaOffset).DateTime);

    public static int EditionOf(DateOnly day) => Math.Max(1, day.DayNumber - FirstEdition.DayNumber + 1);

    private static readonly string[] Weekdays = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"];
    private static readonly string[] Months =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    /// <summary>El mismo día y mes, en 1952 (Bellmont vive en 1952), con el día de la semana de entonces: «Domingo 2 de noviembre de 1952».</summary>
    public static string DateLine(DateOnly day)
    {
        var then = new DateOnly(1952, day.Month, Math.Min(day.Day, DateTime.DaysInMonth(1952, day.Month)));
        return $"{Weekdays[(int)then.DayOfWeek]} {then.Day} de {Months[then.Month - 1]} de 1952";
    }

    public static string Key(string npc, string item, bool buying) => $"{npc}|{item}|{(buying ? "c" : "v")}";

    public static int Trend(long now, long? before) => before is not { } b || b == now ? 0 : now > b ? 1 : -1;

    public static string Arrow(int trend) => trend > 0 ? "▲" : trend < 0 ? "▼" : "";

    /// <summary>Texto de la pizarra 3D (corto: se lee de lejos).</summary>
    public static string BoardText(PriceBoardView board)
    {
        var lines = new List<string> { board.Place.ToUpperInvariant() };
        foreach (var l in board.Lines.Where(l => !l.Buying))
            lines.Add(l.InStock ? $"{l.Name}  {EconomyCatalog.FormatMoney(l.PriceCents)} {Arrow(l.Trend)}".TrimEnd() : $"{l.Name}  AGOTADO");
        var buys = board.Lines.Where(l => l.Buying).ToList();
        if (buys.Count > 0)
        {
            lines.Add("— COMPRAMOS —");
            lines.AddRange(buys.Select(l => $"{l.Name}  {EconomyCatalog.FormatMoney(l.PriceCents)}"));
        }
        if (board.Note is { Length: > 0 } note)
            lines.Add(note);
        return string.Join("\n", lines);
    }

    // ---------------- El diario ----------------

    private static readonly string[] FlavorAds =
    [
        "VENDO rocola de 24 discos, funciona perfecto. Preguntar por Tony en el bar del barrio Moretti.",
        "SE ALQUILA pieza con baño compartido en el barrio Caruso. Solo caballeros, pago por semana.",
        "TELEVISORES en cuotas sin interés. ¡Vea la pelea del sábado en su casa!",
        "CLASES DE MANEJO con camioneta propia. Barrio Valenti, de lunes a sábado.",
        "PERDIDO: perro salchicha, responde a «Bruno». Se gratificará.",
        "COMPRO chatarra y repuestos usados. Los Muelles, galpón 3.",
        "SASTRERÍA BIANCHI: trajes cruzados a medida en una semana.",
        "DINER de la avenida busca cocinero con experiencia. Presentarse de mañana.",
        "PELUQUERÍA DE CABALLEROS: corte y afeitado, 50 centavos.",
        "SE OFRECE contador con experiencia para negocios chicos. Discreción garantizada.",
    ];

    /// <summary>Arma la edición del día con lo que pasó. Determinista: los mismos hechos dan el mismo diario.</summary>
    public static NewspaperView Write(NewspaperFacts f)
    {
        var m = EconomyCatalog.FormatMoney;
        var quotes = new List<NewspaperQuote>();
        foreach (var board in f.Boards)
            foreach (var l in board.Lines)
                quotes.Add(new NewspaperQuote(l.Name, board.Place, l.PriceCents,
                    Trend(l.PriceCents, f.PreviousPrices.TryGetValue(Key(board.NpcCode, l.ItemCode, l.Buying), out var p) ? p : null), l.InStock, l.Buying));

        var stockouts = f.Boards.SelectMany(b => b.Lines.Where(l => !l.Buying && !l.InStock).Select(l => (b.Place, l.Name))).ToList();
        var rivals = f.Families.Where(x => x.Mood == FamilyRules.Rivalry).ToList();
        var friends = f.Families.Where(x => x.Mood == FamilyRules.Cooperation).ToList();
        var rises = quotes.Where(q => q.Trend > 0 && !q.Buying).ToList();
        var falls = quotes.Where(q => q.Trend < 0 && !q.Buying).ToList();
        var controlled = f.Zones.Where(z => z.ControllerCrew is not null).ToList();
        var events = f.Events.OrderBy(e => e.StartsAt).ToList();

        // Título: lo más fuerte que pasó.
        string headline, lead;
        if (rivals.Count > 0)
        {
            var names = string.Join(" y ", rivals.Select(r => r.Name.Replace("Familia ", "los ")));
            headline = $"TENSIÓN: {names.ToUpperInvariant()} SE PONEN DUROS";
            lead = $"En los locales de {names} ya se cobra un 10 % más. Nadie quiere hablar del tema.";
        }
        else if (f.Robberies + f.VehiclesForced >= 3)
        {
            headline = "OLA DE ASALTOS EN BELLMONT";
            lead = $"{f.Robberies + f.VehiclesForced} golpes en las últimas 24 horas. Los comerciantes piden más vigilancia.";
        }
        else if (f.BestReviewed is { } star)
        {
            headline = $"LA CRÍTICA SE RINDE ANTE «{star.ToUpperInvariant()}»";
            lead = "Cinco estrellas para el local de moda. Ya hay cola en la puerta.";
        }
        else if (stockouts.Count > 0)
        {
            headline = $"FALTA {stockouts[0].Name.ToUpperInvariant()} EN EL MERCADO";
            lead = $"En el {stockouts[0].Place} no queda nada. Quien tenga mercadería guardada hoy pone el precio.";
        }
        else if (rises.Count > 0)
        {
            headline = $"SUBE {rises[0].Item.ToUpperInvariant()}";
            lead = $"En el {rises[0].Place} ahora cuesta {m(rises[0].PriceCents)}.";
        }
        else if (events.Count > 0)
        {
            var e = events[0];
            headline = $"HOY: {e.Name.ToUpperInvariant()}";
            lead = $"Desde las {e.StartsAt.ToOffset(TerritoryRules.ArgentinaOffset):HH:mm} en {e.ZoneName}. Habrá movimiento.";
        }
        else
        {
            headline = "CALMA EN EL MERCADO";
            lead = f.Deliveries > 0
                ? $"Bellmont trabaja: {f.Deliveries} entregas en las últimas 24 horas y los precios, firmes."
                : "Precios firmes y poco movimiento. Buen día para hacer negocios.";
        }

        var news = new List<string>();
        foreach (var r in rivals.Skip(headline.StartsWith("TENSIÓN") ? rivals.Count : 0))
            news.Add($"{r.Name}: de mal humor, cobran más caro.");
        foreach (var c in friends)
            news.Add($"{c.Name}: buen momento con la calle. Pagan mejor los trabajos.");
        if (f.Robberies + f.VehiclesForced > 0 && !headline.StartsWith("OLA"))
            news.Add($"Policiales: {f.Robberies + f.VehiclesForced} {(f.Robberies + f.VehiclesForced == 1 ? "asalto" : "asaltos")} en las últimas 24 horas.");
        foreach (var (place, name) in stockouts.Skip(headline.StartsWith("FALTA") ? 1 : 0))
            news.Add($"Agotado: {name} en el {place}.");
        foreach (var q in rises.Skip(headline.StartsWith("SUBE") ? 1 : 0))
            news.Add($"Sube {q.Item} en el {q.Place}: {m(q.PriceCents)}.");
        foreach (var q in falls)
            news.Add($"Baja {q.Item} en el {q.Place}: {m(q.PriceCents)}.");
        foreach (var b in f.NewBusinesses)
            news.Add($"Abrió {b}.");
        foreach (var r in f.Reviews ?? [])
            news.Add($"Crítica: {r}");
        foreach (var famous in f.Famous ?? [])
            news.Add($"Los más nombrados: {famous}.");
        foreach (var a in f.Agenda ?? [])
            news.Add($"Agenda: {a}.");
        foreach (var z in controlled)
            news.Add($"En {z.Name} manda «{z.ControllerCrew}».");
        foreach (var e in events.Skip(headline.StartsWith("HOY") ? 1 : 0).Take(3))
            news.Add($"Agenda: {e.Name}, {e.StartsAt.ToOffset(TerritoryRules.ArgentinaOffset):HH:mm} en {e.ZoneName}.");
        if (f.Deliveries > 0 && !headline.StartsWith("CALMA"))
            news.Add($"Trabajo: {f.Deliveries} entregas en las últimas 24 horas.");

        var ads = new List<string>();
        foreach (var b in f.OpenBusinesses.Where(b => !b.HasHelper).Take(2))
            ads.Add($"SE BUSCA {(b.KindName.Contains("café") ? "AYUDANTE" : "MÚSICO")} para «{b.Name}», {b.Place}. Presentarse en el local.");
        foreach (var b in f.OpenBusinesses.Where(b => b.InStock).Take(3))
            ads.Add($"«{b.Name.ToUpperInvariant()}»: {b.KindName.ToLowerInvariant()} abierto todos los días en {b.Place}.");
        if (f.SupplierOrders > 0)
            ads.Add($"ALMACÉN MORETTI toma pedidos por mayor. Hoy tiene {f.SupplierOrders} en preparación: pida con tiempo.");
        else
            ads.Add("ALMACÉN MORETTI toma pedidos por mayor. Envíos al depósito o a su negocio.");
        var seed = f.Day.DayNumber;
        ads.Add(FlavorAds[seed % FlavorAds.Length]);
        ads.Add(FlavorAds[(seed * 7 + 3) % FlavorAds.Length]);

        return new NewspaperView(f.Day, EditionOf(f.Day), DateLine(f.Day), headline, lead, news.Distinct().ToList(), quotes, ads.Distinct().ToList());
    }
}
