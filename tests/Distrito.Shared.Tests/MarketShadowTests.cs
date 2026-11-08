using Distrito.Shared.Market;

namespace Distrito.Shared.Tests;

/// <summary>Modo sombra (economía, etapa 2): calcula al lado de la economía real sin romper nada y avisa lo que hay que mirar.</summary>
public class MarketShadowTests
{
    private static MarketConfig Config()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config/mercado.json");
            if (File.Exists(candidate))
                return MarketConfig.Parse(File.ReadAllText(candidate));
        }
        throw new FileNotFoundException("config/mercado.json");
    }

    private static ShadowShopInput Shop(string id, int realSold, long price = 500, int stock = 20, double quality = 1) =>
        new(id, $"Puesto {id}", "bolsa-cafe", price, stock, Capacity: 3, quality, X: -3, Z: 8, HasHelper: false, realSold, realSold);

    [Fact]
    public void Un_dia_entero_cierra_con_resumen_y_sin_errores_de_plata()
    {
        var cfg = Config();
        var shadow = new MarketShadow(cfg);
        ShadowDay? day = null;
        for (var cycle = 0; cycle < cfg.CyclesPerDay; cycle++)
        {
            var result = shadow.Observe(1000 + cycle, [Shop("a", 1), Shop("b", 1)]);
            Assert.Equal(0, result.ConservedDiffCents);
            day ??= shadow.ClosedDay;
        }

        Assert.NotNull(day);
        Assert.Equal(1000, day!.FirstCycle);
        Assert.Equal(1000 + cfg.CyclesPerDay - 1, day.LastCycle);
        Assert.Equal(2 * cfg.CyclesPerDay, day.RealSold);
        Assert.True(day.ShadowSold > 0, "con mercadería, el mercado nuevo también vende");
        Assert.Equal(cfg.Zones.Count, day.Zones.Count);
        Assert.DoesNotContain(day.Alerts, a => a.StartsWith("ERROR"));
    }

    [Fact]
    public void Avisa_cuando_lo_real_y_el_mercado_nuevo_se_separan_mucho()
    {
        var cfg = Config();
        var shadow = new MarketShadow(cfg);
        ShadowDay? day = null;
        for (var cycle = 0; cycle < cfg.CyclesPerDay; cycle++)
        {
            shadow.Observe(cycle, [Shop("a", realSold: 0)]); // con mercadería pero sin clientes reales en todo el día
            day ??= shadow.ClosedDay;
        }

        Assert.NotNull(day);
        var a = day!.Shops.Single();
        Assert.Equal(0, a.RealSold);
        Assert.True(a.ShadowSold >= MarketShadow.MinUnitsToCompare, "con café a precio normal, la gente de Bellmont habría comprado");
        Assert.Contains(day.Alerts, x => x.Contains("Puesto a"));
    }

    [Fact]
    public void Los_locales_que_entran_salen_o_mejoran_no_crean_ni_borran_plata()
    {
        var cfg = Config();
        var shadow = new MarketShadow(cfg);
        for (var cycle = 0; cycle < 60; cycle++)
        {
            var shops = new List<ShadowShopInput> { Shop("a", 1, quality: cycle < 30 ? 1 : 1.5) };
            if (cycle is >= 10 and < 40)
                shops.Add(Shop("b", 1, price: 400));
            Assert.Equal(0, shadow.Observe(cycle, shops).ConservedDiffCents);
        }
        Assert.Single(shadow.Simulation.Shops);
    }

    [Fact]
    public void Al_reiniciar_retoma_la_plata_y_los_precios_de_cada_zona()
    {
        var cfg = Config();
        var saved = new MarketShadow(cfg).Zones()
            .Select(z => z with { BudgetCents = 12_345, ReferencePrices = new() { ["bolsa-cafe"] = 610 } }).ToList();

        var shadow = new MarketShadow(cfg);
        shadow.Restore(saved);

        Assert.All(shadow.Zones(), z =>
        {
            Assert.Equal(12_345, z.BudgetCents);
            Assert.Equal(610, z.ReferencePrices["bolsa-cafe"]);
        });
        Assert.Equal(0, shadow.Observe(1, [Shop("a", 1)]).ConservedDiffCents);
    }

    [Fact]
    public void La_diferencia_es_en_porcentaje_de_lo_real()
    {
        Assert.Equal(0, MarketShadow.Difference(0, 0));
        Assert.Equal(100, MarketShadow.Difference(0, 5));
        Assert.Equal(-50, MarketShadow.Difference(10, 5));
        Assert.Equal(20, MarketShadow.Difference(10, 12));
    }
}
