using System.Text.Json;
using System.Text.Json.Serialization;

namespace Distrito.Shared.Economy;

// Negocios que crecen (aprobado por el equipo, 2026-11-02): la clientela sale de la gente de Bellmont y se reparte entre los
// locales por precio, calidad, fama, atracción y distancia. Sin tope: capacidad, calidad y atracción se amplían sin límite.
// La fama sube atendiendo bien y baja de a poco si el local se descuida. Las mejoras las cobra el Taller Caruso.

public sealed class GrowthFame
{
    [JsonPropertyName("porCliente")] public double PerCustomer { get; set; } = 0.1;
    [JsonPropertyName("perdidoSinMercaderia")] public double LostNoStock { get; set; } = 0.4;
    [JsonPropertyName("perdidoPorCola")] public double LostQueue { get; set; } = 0.08;
    [JsonPropertyName("olvidoPorCiclo")] public double Decay { get; set; } = 0.0008;
    [JsonPropertyName("olvidoCerrado")] public double DecayClosed { get; set; } = 0.003;
    [JsonPropertyName("visitantesPorFama")] public double VisitorsPerFame { get; set; } = 0.004;
    [JsonPropertyName("niveles")] public double[] Levels { get; set; } = [0, 100, 400, 1200, 3000];
    [JsonPropertyName("critico")] public double Critic { get; set; } = 60;
}

public sealed class GrowthZone
{
    [JsonPropertyName("codigo")] public string Code { get; set; } = "";
    [JsonPropertyName("habitantes")] public int Habitants { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
    [JsonPropertyName("preferencias")] public Dictionary<string, double> Preferences { get; set; } = new();
}

public sealed class GrowthKind
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
    [JsonPropertyName("consumoDiario")] public double DailyConsumption { get; set; }
    [JsonPropertyName("capacidadBase")] public int BaseCapacity { get; set; } = 3;
    [JsonPropertyName("capacidadPorNivel")] public int CapacityPerLevel { get; set; } = 2;
    [JsonPropertyName("calidadPorNivel")] public double QualityPerLevel { get; set; } = 0.25;
    [JsonPropertyName("precioPorNivelDeCalidad")] public double FairPricePerQuality { get; set; } = 0.1;
    [JsonPropertyName("atraccionPorNivel")] public double AttractionPerLevel { get; set; } = 0.2;
    [JsonPropertyName("costoCapacidad")] public long CapacityCost { get; set; }
    [JsonPropertyName("costoCalidad")] public long QualityCost { get; set; }
    [JsonPropertyName("costoAtraccion")] public long AttractionCost { get; set; }
    [JsonPropertyName("crecimientoCosto")] public double CostGrowth { get; set; } = 1.8;
    [JsonPropertyName("alquilerPorNivel")] public double RentPerLevel { get; set; } = 0.15;
    [JsonPropertyName("picoDesde")] public int PeakFrom { get; set; }
    [JsonPropertyName("picoHasta")] public int PeakTo { get; set; }
    [JsonPropertyName("factorPico")] public double PeakFactor { get; set; } = 1;
    [JsonPropertyName("evento")] public string EventName { get; set; } = "";
    [JsonPropertyName("eventoDesde")] public int EventFrom { get; set; }
    [JsonPropertyName("eventoHasta")] public int EventTo { get; set; }
    [JsonPropertyName("eventoCiclos")] public int EventCycles { get; set; } = 12;
    [JsonPropertyName("eventoFactor")] public double EventFactor { get; set; } = 2.5;
    [JsonPropertyName("nivelesCapacidad")] public string[] CapacityNames { get; set; } = [];
    [JsonPropertyName("nivelesCalidad")] public string[] QualityNames { get; set; } = [];
    [JsonPropertyName("nivelesAtraccion")] public string[] AttractionNames { get; set; } = [];
    [JsonPropertyName("categorias")] public string[] Categories { get; set; } = [];
}

public sealed class GrowthConfig
{
    [JsonPropertyName("ciclosPorDia")] public int CyclesPerDay { get; set; } = 288;
    [JsonPropertyName("elasticidad")] public double Elasticity { get; set; } = 1.5;
    [JsonPropertyName("pesoCalidad")] public double QualityWeight { get; set; } = 1.0;
    [JsonPropertyName("distanciaMetros")] public double DistanceScale { get; set; } = 90;
    [JsonPropertyName("noComproAca")] public double NoPurchaseWeight { get; set; } = 0.6;
    [JsonPropertyName("topeEfectoPrecio")] public double MaxPriceBoost { get; set; } = 1.6;
    [JsonPropertyName("precioMinimo")] public double MinPriceRatio { get; set; } = 0.5;
    [JsonPropertyName("precioMaximo")] public double MaxPriceRatio { get; set; } = 3.0;
    [JsonPropertyName("fama")] public GrowthFame Fame { get; set; } = new();
    [JsonPropertyName("zonas")] public List<GrowthZone> Zones { get; set; } = new();
    [JsonPropertyName("negocios")] public Dictionary<string, GrowthKind> Kinds { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static GrowthConfig Parse(string json) => JsonSerializer.Deserialize<GrowthConfig>(json, Options) ?? new GrowthConfig();

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (CyclesPerDay <= 0) errors.Add("ciclosPorDia tiene que ser mayor que 0");
        if (Zones.Count == 0) errors.Add("faltan zonas");
        foreach (var code in EconomyCatalog.BusinessTypes.Keys)
            if (!Kinds.TryGetValue(code, out var k)) errors.Add($"falta el negocio «{code}»");
            else if (k.CapacityCost <= 0 || k.QualityCost <= 0 || k.AttractionCost <= 0 || k.CostGrowth <= 1) errors.Add($"«{code}»: costos de mejora inválidos");
        if (Fame.Levels.Length != BusinessGrowth.FameNames.Length) errors.Add($"fama.niveles tiene que tener {BusinessGrowth.FameNames.Length} valores");
        return errors;
    }
}

/// <summary>Estado de un local para calcular su clientela (el propio y los de la competencia).</summary>
public sealed record GrowthState(Guid Id, string Kind, double Fame, long PriceCents, int Capacity, int Quality, int Attraction, bool HasHelper = false, int AdminBonus = 0)
{
    public int TotalLevels => Capacity + Quality + Attraction;
}

/// <summary>Clientes de un ciclo: los de los barrios, los que vienen por la fama y si había evento.</summary>
public sealed record GrowthDemand(double Local, double Visitors, double PriceEffect, bool Event)
{
    public double Total => Local + Visitors;
}

public static class BusinessGrowth
{
    public const string Capacity = "capacidad", Quality = "calidad", Attraction = "atraccion";
    public static readonly string[] Branches = [Capacity, Quality, Attraction];

    public static readonly string[] FameNames = ["Nuevo", "Conocido en la cuadra", "Popular", "Famoso", "Leyenda de Bellmont"];

    public static GrowthKind KindOf(GrowthConfig cfg, string kind) => cfg.Kinds[kind];

    public static double QualityOf(GrowthConfig cfg, GrowthState s) => 1 + KindOf(cfg, s.Kind).QualityPerLevel * s.Quality;

    public static double AttractionOf(GrowthConfig cfg, GrowthState s) => 1 + KindOf(cfg, s.Kind).AttractionPerLevel * s.Attraction;

    public static int CapacityOf(GrowthConfig cfg, GrowthState s)
    {
        var k = KindOf(cfg, s.Kind);
        return k.BaseCapacity + k.CapacityPerLevel * s.Capacity + (s.HasHelper ? k.CapacityPerLevel : 0) + s.AdminBonus;
    }

    /// <summary>Lo que la gente considera justo pagar: más calidad, más precio sin espantar (10 % por nivel).</summary>
    public static long FairPriceCents(GrowthConfig cfg, GrowthState s) =>
        (long)Math.Round(EconomyCatalog.BusinessTypes[s.Kind].SalePriceCents * (1 + KindOf(cfg, s.Kind).FairPricePerQuality * s.Quality) / 10.0) * 10;

    public static double Fairness(GrowthConfig cfg, GrowthState s) => FairPriceCents(cfg, s) / (double)Math.Max(1, s.PriceCents);

    public static double PriceEffect(GrowthConfig cfg, GrowthState s) => Math.Min(Math.Pow(Fairness(cfg, s), cfg.Elasticity), cfg.MaxPriceBoost);

    /// <summary>La fama atrae, sin techo pero cada vez menos por punto.</summary>
    public static double FameFactor(double fame) => 1 + 0.5 * Math.Log2(1 + Math.Max(0, fame) / 200);

    public static (long Min, long Max) PriceRange(GrowthConfig cfg, string kind)
    {
        var reference = EconomyCatalog.BusinessTypes[kind].SalePriceCents;
        return ((long)Math.Round(reference * cfg.MinPriceRatio), (long)Math.Round(reference * cfg.MaxPriceRatio));
    }

    public static long Rent(GrowthConfig cfg, GrowthState s) =>
        (long)Math.Round(EconomyCatalog.BusinessTypes[s.Kind].RentCents * (1 + KindOf(cfg, s.Kind).RentPerLevel * s.TotalLevels));

    public static int LevelOf(GrowthState s, string branch) => branch switch
    {
        Capacity => s.Capacity,
        Quality => s.Quality,
        Attraction => s.Attraction,
        _ => throw new ArgumentException(branch),
    };

    /// <summary>Precio del próximo nivel de una rama (sin tope: crece en cada nivel).</summary>
    public static long UpgradeCost(GrowthConfig cfg, string kind, string branch, int currentLevel)
    {
        var k = KindOf(cfg, kind);
        var basePrice = branch switch { Capacity => k.CapacityCost, Quality => k.QualityCost, Attraction => k.AttractionCost, _ => throw new ArgumentException(branch) };
        return (long)Math.Round(basePrice * Math.Pow(k.CostGrowth, currentLevel) / 100.0) * 100;
    }

    public static string UpgradeName(GrowthConfig cfg, string kind, string branch, int level)
    {
        var k = KindOf(cfg, kind);
        var names = branch switch { Capacity => k.CapacityNames, Quality => k.QualityNames, _ => k.AttractionNames };
        if (level < 1) return "—";
        return level <= names.Length ? names[level - 1] : $"{names[^1]} {level - names.Length + 1}";
    }

    public static string BranchName(string branch) => branch switch { Capacity => "Capacidad", Quality => "Calidad", _ => "Atracción" };

    public static string Category(GrowthConfig cfg, GrowthState s)
    {
        var names = KindOf(cfg, s.Kind).Categories;
        var total = s.TotalLevels;
        var i = Math.Min(total < 4 ? 0 : total < 12 ? 1 : total < 24 ? 2 : 3, names.Length - 1);
        var stars = total >= 24 ? (total - 24) / 12 : 0; // después de la última categoría, una estrella cada 12 mejoras
        return stars switch { 0 => names[i], <= 5 => $"{names[i]} {new string('★', stars)}", _ => $"{names[i]} ★×{stars}" };
    }

    public static int FameLevel(GrowthConfig cfg, double fame)
    {
        var level = 0;
        for (var i = 0; i < cfg.Fame.Levels.Length; i++)
            if (fame >= cfg.Fame.Levels[i]) level = i;
        return level;
    }

    /// <summary>Nombre de la fama: después de «Leyenda» siguen las estrellas (cada vez que se duplica).</summary>
    public static string FameName(GrowthConfig cfg, double fame)
    {
        var level = FameLevel(cfg, fame);
        var top = cfg.Fame.Levels[^1];
        if (level < FameNames.Length - 1 || fame < top * 2)
            return FameNames[level];
        return $"{FameNames[^1]} {new string('★', Math.Min(5, (int)Math.Log2(fame / top)))}";
    }

    /// <summary>Lo que falta para el próximo nivel de fama (después de Leyenda, para la próxima estrella).</summary>
    public static double NextFameGoal(GrowthConfig cfg, double fame)
    {
        foreach (var t in cfg.Fame.Levels)
            if (fame < t) return t;
        var goal = cfg.Fame.Levels[^1] * 2;
        while (goal <= fame) goal *= 2;
        return goal;
    }

    // ---------------- Horarios ----------------

    private static bool InHours(int hour, int from, int to) => from <= to ? hour >= from && hour < to : hour >= from || hour < to;

    public static double PeakFactor(GrowthConfig cfg, string kind, DateTimeOffset at)
    {
        var k = KindOf(cfg, kind);
        return InHours(at.ToOffset(TerritoryRules.ArgentinaOffset).Hour, k.PeakFrom, k.PeakTo) ? k.PeakFactor : 1;
    }

    /// <summary>El evento del día de cada rubro (mañana de feria, noche de fiesta): a qué hora empieza, igual para todos.</summary>
    public static DateTimeOffset EventStart(GrowthConfig cfg, string kind, DateOnly day)
    {
        var k = KindOf(cfg, kind);
        var span = (k.EventTo - k.EventFrom + 24) % 24;
        if (span == 0) span = 24;
        var seed = (uint)(day.DayNumber * 2654435761u ^ (uint)kind.Length * 40503u);
        var hour = (k.EventFrom + (int)(seed % (uint)span)) % 24;
        var local = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TerritoryRules.ArgentinaOffset).AddHours(hour);
        if (hour < k.EventFrom) local = local.AddDays(1); // pasó la medianoche: es la madrugada siguiente
        return local.ToUniversalTime();
    }

    public static DateTimeOffset EventEnd(GrowthConfig cfg, string kind, DateOnly day) =>
        EventStart(cfg, kind, day).AddSeconds(KindOf(cfg, kind).EventCycles * 86400.0 / cfg.CyclesPerDay);

    public static bool InEvent(GrowthConfig cfg, string kind, DateTimeOffset at)
    {
        var day = MarketInfoRules.DayOf(at);
        foreach (var d in new[] { day, day.AddDays(-1) })
            if (at >= EventStart(cfg, kind, d) && at < EventEnd(cfg, kind, d))
                return true;
        return false;
    }

    /// <summary>A qué hora pasa el crítico de La Gaceta por un local ese día (entre las 10 y las 22).</summary>
    public static DateTimeOffset CriticAt(Guid business, DateOnly day)
    {
        var h = (uint)business.GetHashCode() ^ (uint)(day.DayNumber * 2246822519u);
        var minutes = 10 * 60 + (int)(h % (12 * 60));
        return new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TerritoryRules.ArgentinaOffset).AddMinutes(minutes).ToUniversalTime();
    }

    // ---------------- Clientela ----------------

    /// <summary>
    /// Clientes que quieren comprar en el local <paramref name="me"/> en un ciclo: la gente de cada barrio se reparte entre
    /// todos los locales del rubro (con la opción de no comprar), y además llegan los que vienen por la fama del lugar.
    /// </summary>
    public static GrowthDemand Demand(GrowthConfig cfg, GrowthState me, IReadOnlyList<GrowthState> all, DateTimeOffset at)
    {
        var k = KindOf(cfg, me.Kind);
        var evt = InEvent(cfg, me.Kind, at);
        var moment = PeakFactor(cfg, me.Kind, at) * (evt ? k.EventFactor : 1);
        var rivals = all.Where(s => s.Kind == me.Kind && s.Id != me.Id).Append(me).ToList();
        double Pull(GrowthState s, double distance) =>
            PriceEffect(cfg, s) * Math.Pow(QualityOf(cfg, s), cfg.QualityWeight) * Math.Exp(-distance / cfg.DistanceScale) * FameFactor(s.Fame) * AttractionOf(cfg, s);

        var local = 0.0;
        foreach (var z in cfg.Zones)
        {
            var need = z.Habitants * k.DailyConsumption * z.Preferences.GetValueOrDefault(me.Kind, 1.0) / cfg.CyclesPerDay * moment;
            var distance = Math.Sqrt(Math.Pow(z.X - k.X, 2) + Math.Pow(z.Z - k.Z, 2));
            var total = cfg.NoPurchaseWeight + rivals.Sum(s => Pull(s, distance));
            local += need * Pull(me, distance) / total;
        }
        var price = PriceEffect(cfg, me);
        var visitors = me.Fame * cfg.Fame.VisitorsPerFame * price * Math.Sqrt(QualityOf(cfg, me)) * AttractionOf(cfg, me) * moment;
        return new GrowthDemand(local, visitors, price, evt);
    }

    /// <summary>
    /// Fama después de un ciclo: sube con cada cliente atendido (más con calidad y precio justo), baja con los que se fueron
    /// sin comprar y se olvida de a poco (más rápido con el local cerrado). Nunca baja de 0.
    /// </summary>
    public static double NextFame(GrowthConfig cfg, GrowthState s, int served, double lostNoStock, double lostQueue, bool open)
    {
        var f = cfg.Fame;
        var gain = served * f.PerCustomer * QualityOf(cfg, s) * Math.Min(1.5, Fairness(cfg, s));
        // Cerrado, la fama solo se olvida (de a poco): no se cobra cada cliente que se fue.
        var loss = open ? lostNoStock * f.LostNoStock + lostQueue * f.LostQueue + s.Fame * f.Decay : s.Fame * f.DecayClosed;
        return Math.Max(0, s.Fame + gain - loss);
    }

    /// <summary>Estrellas del crítico: calidad y precio justo, con el local abierto. Cerrado: una estrella.</summary>
    public static int CriticStars(GrowthConfig cfg, GrowthState s, bool open)
    {
        if (!open) return 1;
        var score = QualityOf(cfg, s) * Math.Min(1.5, Fairness(cfg, s));
        return score < 0.8 ? 2 : score < 1.15 ? 3 : score < 1.6 ? 4 : 5;
    }

    public static double CriticFame(GrowthConfig cfg, int stars) => (stars - 3) * cfg.Fame.Critic;

    public static string CriticText(int stars, string name) => stars switch
    {
        1 => $"«Fuimos a {name} y estaba cerrado. Sin mercadería. Una pena.»",
        2 => $"«{name}: caro para lo que es. No volveríamos.»",
        3 => $"«{name} cumple. Correcto, sin sorpresas.»",
        4 => $"«{name}: muy recomendable. Buena atención y buen producto.»",
        _ => $"«{name} es lo mejor de Bellmont. Hay que ir.»",
    };
}
