using System.Text.Json;
using System.Text.Json.Serialization;

namespace Distrito.Shared.Market;

/// <summary>Una zona de consumidores (el Mercado, cada barrio, Los Muelles): habitantes, ingreso y preferencias.</summary>
public sealed class MarketZoneConfig
{
    [JsonPropertyName("codigo")] public string Code { get; set; } = "";
    [JsonPropertyName("habitantes")] public int Habitants { get; set; }
    [JsonPropertyName("ingresoDiarioCentavos")] public long DailyIncomeCents { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
    [JsonPropertyName("preferencias")] public Dictionary<string, double> Preferences { get; set; } = new();

    public double PreferenceFor(string product) => Preferences.TryGetValue(product, out var w) ? w : 1.0;
}

public sealed class MarketProductConfig
{
    [JsonPropertyName("codigo")] public string Code { get; set; } = "";
    [JsonPropertyName("consumoDiarioPorHabitante")] public double DailyConsumption { get; set; }
    [JsonPropertyName("precioBaseCentavos")] public long BasePriceCents { get; set; }
}

/// <summary>
/// Parámetros del mercado (config/mercado.json). Aprobado por el equipo (2026-11-01) como idea; los valores son iniciales
/// y se calibran con el simulador. Etapa 1: no afecta al juego.
/// </summary>
public sealed class MarketConfig
{
    [JsonPropertyName("ciclosPorDia")] public int CyclesPerDay { get; set; } = 288;
    [JsonPropertyName("elasticidad")] public double Elasticity { get; set; } = 1.5;
    [JsonPropertyName("pesoCalidad")] public double QualityWeight { get; set; } = 0.5;
    [JsonPropertyName("distanciaMetros")] public double DistanceScale { get; set; } = 60;
    [JsonPropertyName("noComproAca")] public double NoPurchaseWeight { get; set; } = 0.3;
    [JsonPropertyName("ciclosDeReserva")] public int ReserveCycles { get; set; } = 48;
    [JsonPropertyName("velocidadPrecio")] public double PriceSpeed { get; set; } = 0.5;
    [JsonPropertyName("topeDiarioPrecio")] public double DailyPriceCap { get; set; } = 0.05;
    [JsonPropertyName("precioMinimoRelativo")] public double MinPriceRatio { get; set; } = 0.5;
    [JsonPropertyName("precioMaximoRelativo")] public double MaxPriceRatio { get; set; } = 2.0;
    [JsonPropertyName("zonas")] public List<MarketZoneConfig> Zones { get; set; } = new();
    [JsonPropertyName("productos")] public List<MarketProductConfig> Products { get; set; } = new();

    public MarketProductConfig Product(string code) => Products.First(p => p.Code == code);

    public static MarketConfig Parse(string json) =>
        JsonSerializer.Deserialize<MarketConfig>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
        ?? throw new ArgumentException("config/mercado.json vacío");

    /// <summary>Errores claros al editar mal el archivo.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (CyclesPerDay <= 0) errors.Add("ciclosPorDia tiene que ser mayor que 0.");
        if (Elasticity <= 0) errors.Add("elasticidad tiene que ser mayor que 0.");
        if (DistanceScale <= 0) errors.Add("distanciaMetros tiene que ser mayor que 0.");
        if (NoPurchaseWeight < 0) errors.Add("noComproAca no puede ser negativo.");
        if (ReserveCycles <= 0) errors.Add("ciclosDeReserva tiene que ser mayor que 0.");
        if (DailyPriceCap is <= 0 or >= 1) errors.Add("topeDiarioPrecio va entre 0 y 1 (por ejemplo 0.05 = 5 %).");
        if (MinPriceRatio <= 0 || MaxPriceRatio <= MinPriceRatio) errors.Add("precioMinimoRelativo y precioMaximoRelativo: 0 < mínimo < máximo.");
        if (Zones.Count == 0) errors.Add("Falta al menos una zona.");
        foreach (var z in Zones.Where(z => z.Habitants < 0 || z.DailyIncomeCents < 0))
            errors.Add($"zona {z.Code}: habitantes e ingreso no pueden ser negativos.");
        if (Zones.Select(z => z.Code).Distinct().Count() != Zones.Count) errors.Add("Hay zonas repetidas.");
        foreach (var p in Products.Where(p => p.DailyConsumption < 0 || p.BasePriceCents <= 0))
            errors.Add($"producto {p.Code}: consumo no negativo y precio base mayor que 0.");
        return errors;
    }
}

/// <summary>
/// Las fórmulas del mercado (diseño propio; ver el informe de integración). Funciones puras: las usa el simulador y, más
/// adelante, el servidor. Todo con números de punto flotante; la plata se redondea afuera, en centavos enteros.
/// </summary>
public static class MarketMath
{
    /// <summary>1. Necesidad diaria de una zona por un producto: habitantes × consumo × preferencia.</summary>
    public static double Need(int habitants, double dailyConsumption, double preference) => habitants * dailyConsumption * preference;

    /// <summary>
    /// 2. Demanda del ciclo: la necesidad del ciclo frenada por el precio (contra la referencia) y por lo que la zona puede
    /// gastar sin quedarse sin reserva.
    /// </summary>
    public static double Demand(double needPerCycle, double referencePrice, double averagePrice, long budgetCents, int reserveCycles, double elasticity)
    {
        if (needPerCycle <= 0 || averagePrice <= 0)
            return 0;
        var byPrice = needPerCycle * Math.Pow(referencePrice / averagePrice, elasticity);
        var byBudget = Math.Max(0, budgetCents) / (reserveCycles * averagePrice);
        return Math.Min(byPrice, byBudget);
    }

    /// <summary>3. Atracción de un local para la gente de una zona: precio, calidad y distancia.</summary>
    public static double Attraction(double referencePrice, double price, double quality, double distance, MarketConfig cfg) =>
        price <= 0 ? 0 : Math.Pow(referencePrice / price, cfg.Elasticity) * Math.Pow(Math.Max(quality, 0.01), cfg.QualityWeight) * Math.Exp(-distance / cfg.DistanceScale);

    /// <summary>4. Reparto: la parte de cada local, con la opción «no compro acá». La suma nunca llega a 1.</summary>
    public static double[] Shares(IReadOnlyList<double> attractions, double noPurchaseWeight)
    {
        var total = noPurchaseWeight + attractions.Sum();
        return total <= 0 ? attractions.Select(_ => 0.0).ToArray() : attractions.Select(a => a / total).ToArray();
    }

    /// <summary>5. Precio de referencia del día siguiente: sube con la demanda sin atender, baja con lo que no se vendió.</summary>
    public static double NextReferencePrice(double current, double basePrice, double unmet, double unsold, double demand, MarketConfig cfg)
    {
        var change = 1 + cfg.PriceSpeed * (unmet - unsold) / Math.Max(demand, 1);
        change = Math.Clamp(change, 1 - cfg.DailyPriceCap, 1 + cfg.DailyPriceCap);
        return Math.Clamp(current * change, basePrice * cfg.MinPriceRatio, basePrice * cfg.MaxPriceRatio);
    }

    /// <summary>6. Producción del ciclo: el menor entre la capacidad, los insumos y lo que hacen los obreros pagos, menos la merma.</summary>
    public static double Production(double maxPerCycle, double inputs, double recipe, int paidWorkers, double productivity, double waste) =>
        Math.Max(0, Math.Min(maxPerCycle, Math.Min(recipe <= 0 ? maxPerCycle : inputs / recipe, paidWorkers * productivity)) * (1 - waste));

    /// <summary>7. Margen por unidad: precio menos insumo menos los costos fijos repartidos en lo vendido.</summary>
    public static double UnitMargin(double price, double unitCost, double fixedCosts, double units) =>
        units <= 0 ? -fixedCosts : price - unitCost - fixedCosts / units;
}
