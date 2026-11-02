namespace Distrito.Shared.Market;

/// <summary>Un escenario del mercado de prueba: qué se fuerza y cuántos días corre.</summary>
public sealed record MarketScenario(string Code, string Name, string Forced, int Days, Action<MarketSimulation> Setup);

/// <summary>
/// La cadena de prueba de la etapa 1 (informe de integración): el tostadero del Barrio Caruso, el Almacén Moretti, el
/// camión de reparto, dos puestos de café en el Mercado y las cinco zonas. Los números de los locales son los aprobados
/// hoy para el puesto de café ($5 la venta, alquiler $1 y empleado $1 por ciclo, bolsas a $3 en el Almacén); los de la
/// fábrica y el camión son propuestas para calibrar.
/// </summary>
public static class MarketScenarios
{
    public const string Coffee = "bolsa-cafe";
    public const int Days = 7;

    public static MarketSimulation BuildTestMarket(MarketConfig cfg)
    {
        var sim = new MarketSimulation(cfg,
            new SimSupplier { Stock = 200, SellPriceCents = 300, Cash = 30_000 },
            new SimFactory
            {
                Zone = "barrio-caruso", MaxPerCycle = 8, Workers = 4, WagePerWorker = 50, Productivity = 2, Waste = 0.03,
                Recipe = 1, Inputs = 200, Cash = 30_000, SellPriceCents = 200, InputPriceCents = 80, ReorderBelow = 100, ReorderAmount = 400,
                ChainTarget = 400,
            },
            new SimTruck { Capacity = 40, TripCycles = 2, FeePerTrip = 300, MinLoad = 20, ShipWhenSupplierBelow = 300 });
        sim.Shops.Add(Stand("Puesto A", -3, 8));
        sim.Shops.Add(Stand("Puesto B", 6, -4));
        return sim;
    }

    public static SimShop Stand(string name, double x, double z, long price = 500) => new()
    {
        Name = name, Zone = "el-mercado", X = x, Z = z, Product = Coffee, PriceCents = price, Stock = 20, Capacity = 3,
        Cash = 5_000, RentPerCycle = 100, WagePerCycle = 100,
    };

    public static readonly IReadOnlyList<MarketScenario> All =
    [
        new("base", "Mercado normal", "Dos puestos al precio de siempre", Days, _ => { }),
        new("sobreoferta", "Sobreoferta", "Seis puestos con el mismo precio", Days, sim =>
        {
            for (var i = 0; i < 4; i++)
                sim.Shops.Add(Stand($"Puesto {(char)('C' + i)}", -10 + 6 * i, 14));
        }),
        new("escasez", "Escasez", "El puerto para del día 2 al 4: el tostadero no consigue café verde", Days, sim =>
        {
            sim.PortOpen = day => day is < 1 or > 3;
            sim.BeforeCycle = (s, day, cycle) => { if (day == 1 && cycle == 0) s.Factory.Inputs = 0; };
        }),
        new("suba", "Suba de precios", "El Puesto B cobra 50 % más ($7,50)", Days, sim => sim.Shops[1].PriceCents = 750),
        new("competencia", "Competencia", "El Puesto B baja 20 % ($4)", Days, sim => sim.Shops[1].PriceCents = 400),
        new("asalto", "Falta de abastecimiento", "Asaltan cada viaje del camión el día 3", Days, sim => sim.TruckRobbed = day => day == 2),
    ];

    public static MarketSimulation Run(MarketConfig cfg, MarketScenario scenario)
    {
        var sim = BuildTestMarket(cfg);
        scenario.Setup(sim);
        sim.RunDays(scenario.Days);
        return sim;
    }
}
