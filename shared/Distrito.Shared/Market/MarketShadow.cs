namespace Distrito.Shared.Market;

/// <summary>Un local real, como está al empezar el ciclo, y lo que vendió de verdad en ese ciclo.</summary>
public sealed record ShadowShopInput(string Id, string Name, string Product, long PriceCents, int Stock, int Capacity, double Quality,
    double X, double Z, bool HasHelper, int RealSold, int RealWanted);

/// <summary>Lo de un local en un ciclo: lo que vendió de verdad y lo que el mercado nuevo dice que habría vendido.</summary>
public sealed record ShadowShopCycle(string Id, string Name, long PriceCents, int Stock, int Capacity, int RealSold, int RealWanted, int ShadowSold);

public sealed record ShadowZoneState(string Zone, long BudgetCents, Dictionary<string, double> ReferencePrices);

public sealed record ShadowCycle(long Cycle, int Day, List<ShadowShopCycle> Shops, List<ShadowZoneState> Zones, long ConservedDiffCents, List<string> Alerts);

public sealed record ShadowShopDay(string Id, string Name, long LastPriceCents, int RealSold, int ShadowSold, double DifferencePct);

public sealed record ShadowZoneDay(string Zone, double Demand, double Wanted, int Served, double Shortage, double Idle,
    double ReferenceBefore, double ReferenceAfter, long BudgetCents);

/// <summary>Resumen de un día del modo sombra: cuánto se vendió de verdad, cuánto habría vendido el mercado nuevo y qué hay que mirar.</summary>
public sealed record ShadowDay(int Day, long FirstCycle, long LastCycle, int RealSold, int ShadowSold, double DifferencePct,
    List<ShadowShopDay> Shops, List<ShadowZoneDay> Zones, List<string> Alerts);

/// <summary>
/// Modo sombra de la economía de mercado (etapa 2): corre el mercado nuevo al lado de la economía real, ciclo por ciclo,
/// con los locales reales (precio, mercadería, capacidad, calidad) y anota cuánto habría vendido cada uno y cómo se
/// moverían los precios de referencia de cada zona. NO decide nada: no cobra, no paga y no cambia nada del juego.
/// La cadena de abastecimiento (tostadero, Almacén y camión) es la simulada de la etapa 1, porque en el juego todavía no
/// existe; la mercadería de cada local se toma de la real en cada ciclo.
/// </summary>
public sealed class MarketShadow
{
    /// <summary>Diferencia diaria entre lo real y lo simulado que dispara un aviso (con al menos <see cref="MinUnitsToCompare"/> unidades).</summary>
    public const double DivergenceAlert = 0.5;
    public const int MinUnitsToCompare = 20;
    /// <summary>Faltante (lo pedido que no se pudo atender) que dispara un aviso.</summary>
    public const double ShortageAlert = 0.3;
    /// <summary>Una zona con menos que esta parte de un día de ingreso está casi sin plata.</summary>
    public const double PoorZoneAlert = 0.05;

    private readonly MarketConfig _cfg;
    private readonly MarketSimulation _sim;
    private readonly Dictionary<string, string> _names = new();
    private readonly Dictionary<string, (int Real, int Shadow, long Price)> _today = new();
    private readonly List<string> _dayAlerts = new();
    private long _expected;
    private long _firstCycleToday = -1;

    public MarketShadow(MarketConfig cfg)
    {
        _cfg = cfg;
        _sim = MarketScenarios.BuildTestMarket(cfg);
        _sim.Shops.Clear(); // los locales son los reales: se agregan en cada ciclo
        _expected = _sim.ConservedTotal();
    }

    public MarketSimulation Simulation => _sim;

    /// <summary>El día que se cerró en el último <see cref="Observe"/> (o null).</summary>
    public ShadowDay? ClosedDay { get; private set; }

    /// <summary>Retoma la plata y los precios de cada zona (al reiniciar el backend).</summary>
    public void Restore(IEnumerable<ShadowZoneState> zones)
    {
        foreach (var z in zones)
            _sim.RestoreZone(z.Zone, z.BudgetCents, z.ReferencePrices);
        _expected = _sim.ConservedTotal();
    }

    public List<ShadowZoneState> Zones() =>
        _sim.ZoneCodes.Select(code => new ShadowZoneState(code, _sim.ZoneBudget(code),
            _cfg.Products.ToDictionary(p => p.Code, p => Math.Round(_sim.ReferencePrice(code, p.Code), 2)))).ToList();

    /// <summary>Un ciclo: copia los locales reales al mercado simulado, lo avanza un ciclo y compara.</summary>
    public ShadowCycle Observe(long cycle, IReadOnlyList<ShadowShopInput> shops)
    {
        ClosedDay = null;
        if (_firstCycleToday < 0)
            _firstCycleToday = cycle;
        var alerts = new List<string>();
        Sync(shops.Where(s => _cfg.Products.Any(p => p.Code == s.Product)).ToList());

        var before = _sim.Shops.ToDictionary(s => s.Name, s => s.SoldToday);
        var daysBefore = _sim.Days.Count;
        _sim.Step();
        var closed = _sim.Days.Count > daysBefore ? _sim.Days[^1] : null;

        var result = new List<ShadowShopCycle>();
        foreach (var real in shops)
        {
            var sim = _sim.Shops.FirstOrDefault(s => s.Name == real.Id);
            var soldNow = sim is null ? 0
                : closed is not null ? closed.Shops.FirstOrDefault(d => d.Shop == real.Id)?.Sold ?? 0
                : sim.SoldToday;
            var shadow = sim is null ? 0 : soldNow - before.GetValueOrDefault(real.Id);
            result.Add(new ShadowShopCycle(real.Id, real.Name, real.PriceCents, real.Stock, real.Capacity, real.RealSold, real.RealWanted, shadow));
            var acc = _today.GetValueOrDefault(real.Id);
            _today[real.Id] = (acc.Real + real.RealSold, acc.Shadow + shadow, real.PriceCents);
        }

        var diff = _sim.ConservedTotal() - _expected;
        if (diff != 0)
        {
            alerts.Add($"ERROR: la plata del mercado simulado no cierra por {diff} centavos (es un error del simulador, no del juego).");
            _expected = _sim.ConservedTotal();
        }
        _dayAlerts.AddRange(alerts);

        if (closed is not null)
            ClosedDay = CloseDay(closed, cycle);
        return new ShadowCycle(cycle, _sim.Day, result, Zones(), diff, alerts);
    }

    /// <summary>Pone en el simulado los locales reales: los nuevos entran, los que ya no están salen y los demás se actualizan.</summary>
    private void Sync(IReadOnlyList<ShadowShopInput> shops)
    {
        var ids = shops.Select(s => s.Id).ToHashSet();
        foreach (var gone in _sim.Shops.Where(s => !ids.Contains(s.Name)).ToList())
        {
            _sim.MoveZoneBudget(gone.Zone, gone.Cash); // lo que tenía el local vuelve a su barrio: la plata no desaparece
            _sim.Shops.Remove(gone);
        }
        foreach (var real in shops)
        {
            _names[real.Id] = real.Name;
            var index = _sim.Shops.FindIndex(s => s.Name == real.Id);
            var existing = index >= 0 ? _sim.Shops[index] : null;
            // La calidad no se puede cambiar en un local simulado: si mejoró, se reemplaza por uno igual con la nueva.
            if (existing is null || Math.Abs(existing.Quality - real.Quality) > 1e-9)
            {
                var shop = new SimShop
                {
                    Name = real.Id, Zone = ZoneOf(real.X, real.Z), X = real.X, Z = real.Z, Product = real.Product, Quality = real.Quality,
                    Cash = existing?.Cash ?? 0, RentPerCycle = 100, WagePerCycle = real.HasHelper ? 100 : 0,
                    RestockBelow = 0, // no repone desde el simulado: la mercadería es la real
                };
                if (existing is null)
                    _sim.Shops.Add(shop);
                else
                    _sim.Shops[index] = shop;
                existing = shop;
            }
            existing.PriceCents = real.PriceCents;
            existing.Stock = real.Stock;
            existing.Capacity = real.Capacity;
        }
    }

    private string ZoneOf(double x, double z) =>
        _cfg.Zones.OrderBy(zone => (zone.X - x) * (zone.X - x) + (zone.Z - z) * (zone.Z - z)).First().Code;

    private ShadowDay CloseDay(SimDay simDay, long lastCycle)
    {
        var shops = _today.Select(kv => new ShadowShopDay(kv.Key, _names.GetValueOrDefault(kv.Key, kv.Key), kv.Value.Price, kv.Value.Real, kv.Value.Shadow,
            Difference(kv.Value.Real, kv.Value.Shadow))).OrderBy(s => s.Name).ToList();
        var zones = simDay.Zones.Select(z => new ShadowZoneDay(z.Zone, Math.Round(z.Demand, 1), Math.Round(z.Wanted, 1), z.Served,
            Math.Round(z.Shortage, 1), Math.Round(z.Idle, 1), Math.Round(z.ReferencePrice, 2),
            Math.Round(_sim.ReferencePrice(z.Zone, _cfg.Products[0].Code), 2), z.BudgetCents)).ToList();

        var alerts = new List<string>(_dayAlerts);
        foreach (var s in shops.Where(s => s.RealSold + s.ShadowSold >= MinUnitsToCompare && Math.Abs(s.DifferencePct) > DivergenceAlert * 100))
            alerts.Add($"«{s.Name}» vendió {s.RealSold} y con el mercado nuevo habría vendido {s.ShadowSold} ({s.DifferencePct:+0;-0} %).");
        var product = _cfg.Products[0];
        foreach (var z in zones)
        {
            var cfgZone = _cfg.Zones.First(c => c.Code == z.Zone);
            if (z.ReferenceAfter <= product.BasePriceCents * _cfg.MinPriceRatio + 0.5)
                alerts.Add($"{z.Zone}: el precio de referencia llegó al piso ({Money(z.ReferenceAfter)}).");
            if (z.ReferenceAfter >= product.BasePriceCents * _cfg.MaxPriceRatio - 0.5)
                alerts.Add($"{z.Zone}: el precio de referencia llegó al techo ({Money(z.ReferenceAfter)}).");
            if (z.Wanted > 1 && z.Shortage / z.Wanted > ShortageAlert)
                alerts.Add($"{z.Zone}: faltó mercadería para el {z.Shortage / z.Wanted:P0} de lo que la gente quiso comprar.");
            if (cfgZone.Habitants > 0 && z.BudgetCents < cfgZone.Habitants * cfgZone.DailyIncomeCents * PoorZoneAlert)
                alerts.Add($"{z.Zone}: la gente se quedó casi sin plata ({Money(z.BudgetCents)}).");
        }

        var real = shops.Sum(s => s.RealSold);
        var shadow = shops.Sum(s => s.ShadowSold);
        var day = new ShadowDay(simDay.Day, _firstCycleToday, lastCycle, real, shadow, Difference(real, shadow), shops, zones, alerts);
        _today.Clear();
        _dayAlerts.Clear();
        _firstCycleToday = -1;
        return day;
    }

    /// <summary>Cuánto más (o menos) habría vendido el mercado nuevo, en % de lo real.</summary>
    public static double Difference(int real, int shadow) =>
        real == 0 ? (shadow == 0 ? 0 : 100) : Math.Round((shadow - real) * 100.0 / real, 1);

    private static string Money(double cents) => $"${cents / 100:0.00}";
}
