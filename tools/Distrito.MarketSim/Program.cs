using System.Globalization;
using System.Text;
using Distrito.Shared.Market;

// Simulador del mercado de prueba: corre los escenarios con config/mercado.json y deja un informe (markdown + CSV).
// Uso: dotnet run --project tools/Distrito.MarketSim -- [config/mercado.json] [carpeta-de-salida]
var configPath = args.Length > 0 ? args[0] : "config/mercado.json";
var outDir = args.Length > 1 ? args[1] : "market-report";
Directory.CreateDirectory(outDir);
var cfg = MarketConfig.Parse(File.ReadAllText(configPath));
if (cfg.Validate() is { Count: > 0 } errors)
{
    Console.Error.WriteLine("config/mercado.json tiene errores:\n" + string.Join("\n", errors));
    return 2;
}

var inv = CultureInfo.InvariantCulture;
string Money(long cents) => (cents / 100.0).ToString("0.00", inv);
var md = new StringBuilder("# Mercado de prueba — resultados del simulador\n\n");
var failed = false;
foreach (var scenario in MarketScenarios.All)
{
    var start = MarketScenarios.BuildTestMarket(cfg);
    scenario.Setup(start);
    var conserved = start.ConservedTotal();
    var sim = MarketScenarios.Run(cfg, scenario);
    var ok = sim.ConservedTotal() == conserved;
    failed |= !ok;
    md.Append($"## {scenario.Name}\n\n{scenario.Forced}. Plata conservada: {(ok ? "sí" : "NO")}.\n\n");
    md.Append("| Día | Demanda | A los locales | Atendida | Escasez | Ociosa | Precio ref. Mercado | Ref. Caruso | Stock Almacén | Producido | Perdido |");
    foreach (var s in sim.Shops) md.Append($" {s.Name} vend. | {s.Name} ganancia |");
    md.Append('\n').Append(string.Concat(Enumerable.Repeat("| --- ", 11 + 2 * sim.Shops.Count))).Append("|\n");
    var csv = new StringBuilder("dia,zona,demanda,a_los_locales,atendida,escasez,ociosa,oportunidad,precio_ref,presupuesto\n");
    var shopsCsv = new StringBuilder("dia,local,vendidas,ofrecidas,stock_final,ingresos,costos,ganancia\n");
    foreach (var d in sim.Days)
    {
        var market = d.Zones.First(z => z.Zone == "el-mercado");
        var caruso = d.Zones.First(z => z.Zone == "barrio-caruso");
        md.Append($"| {d.Day} | {d.Zones.Sum(z => z.Demand).ToString("0", inv)} | {d.Zones.Sum(z => z.Wanted).ToString("0", inv)} | {d.Zones.Sum(z => z.Served)} | {d.Zones.Sum(z => z.Shortage).ToString("0", inv)} | {d.Zones.Sum(z => z.Idle).ToString("0", inv)} | {Money((long)market.ReferencePrice)} | {Money((long)caruso.ReferencePrice)} | {d.SupplierStock} | {d.Produced} | {d.LostInTransit} |");
        foreach (var s in d.Shops) md.Append($" {s.Sold} | {Money(s.ProfitCents)} |");
        md.Append('\n');
        foreach (var z in d.Zones)
            csv.Append(string.Join(',', d.Day, z.Zone, z.Demand.ToString("0.0", inv), z.Wanted.ToString("0.0", inv), z.Served, z.Shortage.ToString("0.0", inv), z.Idle.ToString("0.0", inv), z.Opportunity.ToString("0.0", inv), Money((long)z.ReferencePrice), Money(z.BudgetCents))).Append('\n');
        foreach (var s in d.Shops)
            shopsCsv.Append(string.Join(',', d.Day, s.Shop, s.Sold, s.Offered, s.StockEnd, Money(s.Revenue), Money(s.Cost), Money(s.ProfitCents))).Append('\n');
    }
    md.Append('\n');
    File.WriteAllText(Path.Combine(outDir, $"{scenario.Code}-zonas.csv"), csv.ToString());
    File.WriteAllText(Path.Combine(outDir, $"{scenario.Code}-locales.csv"), shopsCsv.ToString());
    var last = sim.Days[^1];
    Console.WriteLine($"[{scenario.Code}] conservada={ok} demanda_dia7={last.Zones.Sum(z => z.Demand):0} atendida={last.Zones.Sum(z => z.Served)} "
        + string.Join(" ", last.Shops.Select(s => $"{s.Shop}:vende={s.Sold},gana={Money(s.ProfitCents)}"))
        + $" ref_mercado={Money((long)last.Zones.First(z => z.Zone == "el-mercado").ReferencePrice)} almacen={last.SupplierStock}");
}
// Negocios que crecen: el ritmo de fama, clientela y mejoras (config/negocios.json).
var growthPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", "negocios.json");
if (File.Exists(growthPath))
{
    var growthCfg = GrowthConfig.Parse(File.ReadAllText(growthPath));
    if (growthCfg.Validate() is { Count: > 0 } growthErrors)
    {
        Console.Error.WriteLine("config/negocios.json tiene errores:\n" + string.Join("\n", growthErrors));
        return 2;
    }
    var growth = GrowthSim.Run(growthCfg);
    md.Append(growth);
    File.WriteAllText(Path.Combine(outDir, "crecimiento.md"), growth);
}
File.WriteAllText(Path.Combine(outDir, "informe.md"), md.ToString());
Console.WriteLine(md.ToString());
return failed ? 1 : 0;
