using System.Globalization;
using System.Text;
using Distrito.Shared.Economy;

/// <summary>
/// Negocios que crecen: simula días enteros (288 ciclos) con las mismas reglas que el backend (BusinessGrowth) para
/// calibrar el ritmo. Cada dueño reinvierte lo que gana según su estrategia y cobra el precio justo de su calidad.
/// </summary>
public static class GrowthSim
{
    private sealed class Shop
    {
        public required string Name;
        public required GrowthState State;
        public long Cash;
        public double Carry;
        public string[] Plan = [BusinessGrowth.Capacity, BusinessGrowth.Quality, BusinessGrowth.Attraction];
        public Func<int, bool> Closed = _ => false;
        public long Reserve = 5_000;
        public bool Invests = true;
        public int ServedToday, WantedToday, LostToday, CriticStars;
        public long ProfitToday, Invested;
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string M(long cents) => (cents / 100.0).ToString("#,0", Inv);

    public static string Run(GrowthConfig cfg)
    {
        var md = new StringBuilder("# Negocios que crecen — simulación\n\n");
        md.Append(Scenario(cfg, "Un puesto de café que reinvierte todo", 21,
            [Owner(cfg, "Puesto", EconomyCatalog.CoffeeStandLocation)]));
        md.Append(Scenario(cfg, "Dos puestos: uno apuesta a la calidad, el otro a la capacidad", 14,
        [
            Owner(cfg, "Calidad", EconomyCatalog.CoffeeStandLocation, [BusinessGrowth.Quality, BusinessGrowth.Quality, BusinessGrowth.Capacity]),
            Owner(cfg, "Capacidad", EconomyCatalog.CoffeeStandLocation, [BusinessGrowth.Capacity, BusinessGrowth.Attraction]),
        ]));
        var careless = Owner(cfg, "Descuidado", EconomyCatalog.CoffeeStandLocation);
        careless.Closed = day => day is >= 5 and <= 7;
        md.Append(Scenario(cfg, "Puesto que se queda sin mercadería los días 6 a 8", 12, [careless]));
        md.Append(Scenario(cfg, "Un club de jazz que reinvierte todo", 21, [Owner(cfg, "Club", EconomyCatalog.JazzClubKind)]));
        var lazy = Owner(cfg, "Sin mejoras", EconomyCatalog.CoffeeStandLocation);
        lazy.Invests = false;
        md.Append(Scenario(cfg, "Puesto que nunca mejora (contra uno que reinvierte)", 14, [lazy, Owner(cfg, "Reinvierte", EconomyCatalog.CoffeeStandLocation)]));
        return md.ToString();
    }

    private static Shop Owner(GrowthConfig cfg, string name, string kind, string[]? plan = null) => new()
    {
        Name = name,
        State = new GrowthState(Guid.NewGuid(), kind, 0, EconomyCatalog.BusinessTypes[kind].SalePriceCents, 0, 0, 0),
        Cash = 2_000,
        Plan = plan ?? [BusinessGrowth.Capacity, BusinessGrowth.Quality, BusinessGrowth.Attraction],
    };

    private static string Scenario(GrowthConfig cfg, string title, int days, List<Shop> owners)
    {
        var sb = new StringBuilder($"## {title}\n\n| Día | Local | Fama | Nivel | Quisieron | Atendidos | Se fueron | Ganancia | Invertido | Caja | Cap/Cal/Atr | Categoría | Precio | Crítico |\n");
        sb.Append(string.Concat(Enumerable.Repeat("| --- ", 14))).Append("|\n");
        var start = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TerritoryRules.ArgentinaOffset);
        var cycleSeconds = 86400.0 / cfg.CyclesPerDay;
        for (var day = 0; day < days; day++)
        {
            foreach (var o in owners) { o.ServedToday = o.WantedToday = o.LostToday = 0; o.ProfitToday = 0; o.Invested = 0; o.CriticStars = 0; }
            var date = MarketInfoRules.DayOf(start.AddDays(day));
            for (var c = 0; c < cfg.CyclesPerDay; c++)
            {
                var at = start.AddDays(day).AddSeconds(c * cycleSeconds);
                var states = owners.Select(o => o.State).ToList();
                foreach (var o in owners)
                {
                    var type = EconomyCatalog.BusinessTypes[o.State.Kind];
                    var demand = BusinessGrowth.Demand(cfg, o.State, states, at);
                    o.Carry += demand.Total;
                    var wanted = (int)o.Carry;
                    o.Carry -= wanted;
                    var open = !o.Closed(day);
                    var cap = BusinessGrowth.CapacityOf(cfg, o.State);
                    var served = open ? Math.Min(wanted, cap) : 0;
                    var lostNoStock = open ? 0 : wanted;
                    var lostQueue = open ? wanted - served : 0;
                    var rent = open ? BusinessGrowth.Rent(cfg, o.State) : 0;
                    var profit = served * (o.State.PriceCents - type.ItemPriceCents) - rent;
                    o.Cash += profit;
                    o.ProfitToday += profit;
                    o.ServedToday += served;
                    o.WantedToday += wanted;
                    o.LostToday += wanted - served;
                    var fame = BusinessGrowth.NextFame(cfg, o.State, served, lostNoStock, lostQueue, open);
                    var critic = BusinessGrowth.CriticAt(o.State.Id, date);
                    if (critic >= at && critic < at.AddSeconds(cycleSeconds))
                    {
                        o.CriticStars = BusinessGrowth.CriticStars(cfg, o.State, open);
                        fame = Math.Max(0, fame + BusinessGrowth.CriticFame(cfg, o.CriticStars));
                    }
                    o.State = o.State with { Fame = fame };
                }
            }
            foreach (var o in owners)
            {
                if (o.Invests)
                    Invest(cfg, o);
                var s = o.State;
                sb.Append($"| {day + 1} | {o.Name} | {s.Fame:0} | {BusinessGrowth.FameName(cfg, s.Fame)} | {o.WantedToday} | {o.ServedToday} | {o.LostToday} | {M(o.ProfitToday)} | {M(o.Invested)} | {M(o.Cash)} | "
                    + $"{s.Capacity}/{s.Quality}/{s.Attraction} | {BusinessGrowth.Category(cfg, s)} | {M(s.PriceCents)} | {(o.CriticStars > 0 ? new string('★', o.CriticStars) : "")} |\n");
            }
        }
        var lastLine = string.Join(" ", owners.Select(o => $"{o.Name}:fama={o.State.Fame:0},atendidos_dia={o.ServedToday},caja=${M(o.Cash)},niveles={o.State.TotalLevels}"));
        Console.WriteLine($"[crecimiento] {title}: {lastLine}");
        return sb.Append('\n').ToString();
    }

    /// <summary>Compra mejoras siguiendo el plan mientras le alcance (sin tocar la reserva) y cobra el precio justo.</summary>
    private static void Invest(GrowthConfig cfg, Shop o)
    {
        for (var guard = 0; guard < 20; guard++)
        {
            var bought = false;
            foreach (var branch in o.Plan)
            {
                var cost = BusinessGrowth.UpgradeCost(cfg, o.State.Kind, branch, BusinessGrowth.LevelOf(o.State, branch));
                if (o.Cash - cost < o.Reserve)
                    continue;
                o.Cash -= cost;
                o.Invested += cost;
                o.State = branch switch
                {
                    BusinessGrowth.Capacity => o.State with { Capacity = o.State.Capacity + 1 },
                    BusinessGrowth.Quality => o.State with { Quality = o.State.Quality + 1 },
                    _ => o.State with { Attraction = o.State.Attraction + 1 },
                };
                bought = true;
            }
            if (!bought)
                break;
        }
        o.State = o.State with { PriceCents = BusinessGrowth.FairPriceCents(cfg, o.State) };
    }
}
