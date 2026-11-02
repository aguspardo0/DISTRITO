using Distrito.Shared.Market;

namespace Distrito.Shared.Tests;

/// <summary>Mercado de prueba (economía, etapa 1): las fórmulas y los escenarios forzados tienen que dar lo esperable.</summary>
public class MarketTests
{
    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative);
    }

    private static MarketConfig Config() => MarketConfig.Parse(File.ReadAllText(RepoFile("config/mercado.json")));

    private static MarketScenario Scenario(string code) => MarketScenarios.All.Single(s => s.Code == code);

    private static (MarketSimulation Sim, long Before) Run(string code)
    {
        var cfg = Config();
        var start = MarketScenarios.BuildTestMarket(cfg);
        Scenario(code).Setup(start);
        var before = start.ConservedTotal();
        return (MarketScenarios.Run(cfg, Scenario(code)), before);
    }

    private static double MarketRef(SimDay d) => d.Zones.First(z => z.Zone == "el-mercado").ReferencePrice;

    // ---- Config y fórmulas ----

    [Fact]
    public void La_config_del_repo_es_valida()
    {
        Assert.Empty(Config().Validate());
    }

    [Fact]
    public void A_mayor_precio_menos_demanda_y_sin_plata_no_hay_compra()
    {
        var cfg = Config();
        double D(double price, long budget) => MarketMath.Demand(10, 500, price, budget, cfg.ReserveCycles, cfg.Elasticity, cfg.MaxPriceBoost);
        Assert.True(D(400, 1_000_000) > D(500, 1_000_000));
        Assert.True(D(500, 1_000_000) > D(700, 1_000_000));
        Assert.True(D(300, 1_000_000) <= 10 * cfg.MaxPriceBoost + 1e-9);
        Assert.Equal(0, D(500, 0));
    }

    [Fact]
    public void Las_cuotas_suman_lo_que_no_se_queda_en_casa()
    {
        var shares = MarketMath.Shares(new[] { 2.0, 1.0, 1.0 }, 0.3);
        Assert.Equal(3, shares.Length);
        Assert.All(shares, s => Assert.InRange(s, 0, 1));
        Assert.True(shares[0] > shares[1]);
        Assert.True(shares.Sum() < 1);
    }

    [Fact]
    public void El_precio_de_referencia_sube_con_escasez_y_baja_con_sobrante_dentro_del_tope()
    {
        var cfg = Config();
        var up = MarketMath.NextReferencePrice(500, 500, unmet: 50, unsold: 0, demand: 100, cfg);
        var down = MarketMath.NextReferencePrice(500, 500, unmet: 0, unsold: 50, demand: 100, cfg);
        Assert.True(up > 500 && up <= 500 * (1 + cfg.DailyPriceCap) + 1e-6);
        Assert.True(down < 500 && down >= 500 * (1 - cfg.DailyPriceCap) - 1e-6);
    }

    // ---- Escenarios ----

    [Theory]
    [InlineData("base")]
    [InlineData("sobreoferta")]
    [InlineData("escasez")]
    [InlineData("suba")]
    [InlineData("competencia")]
    [InlineData("asalto")]
    public void No_se_crea_ni_se_pierde_plata(string code)
    {
        var (sim, before) = Run(code);
        Assert.Equal(before, sim.ConservedTotal());
        Assert.All(sim.Shops, s => Assert.True(s.Cash >= 0, $"{s.Name} quedó en negativo"));
        Assert.True(sim.Supplier.Cash >= 0 && sim.Factory.Cash >= 0 && sim.Truck.Cash >= 0);
    }

    [Theory]
    [InlineData("base")]
    [InlineData("sobreoferta")]
    [InlineData("escasez")]
    [InlineData("suba")]
    [InlineData("competencia")]
    [InlineData("asalto")]
    public void Nunca_se_vende_mas_de_lo_que_la_gente_quiere(string code)
    {
        var (sim, _) = Run(code);
        // Lo vendido se anota en la zona del local, no en la del comprador: se compara el total del día.
        Assert.All(sim.Days, d => Assert.True(d.Zones.Sum(z => z.Served) <= d.Zones.Sum(z => z.Wanted) + d.Zones.Count,
            $"día {d.Day}: {d.Zones.Sum(z => z.Served)} > {d.Zones.Sum(z => z.Wanted):0}"));
    }

    [Fact]
    public void Mercado_normal_los_dos_puestos_ganan()
    {
        var (sim, _) = Run("base");
        var last = sim.Days[^1];
        Assert.All(last.Shops, s => Assert.True(s.ProfitCents > 0 && s.Sold > 0, $"{s.Shop} no gana"));
        Assert.All(sim.Days, d => Assert.Equal(0, d.LostInTransit));
    }

    [Fact]
    public void Sobreoferta_cada_puesto_vende_menos_y_el_precio_baja()
    {
        var (normal, _) = Run("base");
        var (over, _) = Run("sobreoferta");
        var perShopNormal = normal.Days[^1].Shops.Average(s => s.Sold);
        var perShopOver = over.Days[^1].Shops.Average(s => s.Sold);
        Assert.True(perShopOver < perShopNormal * 0.6);
        Assert.True(MarketRef(over.Days[^1]) < MarketRef(normal.Days[^1]));
        Assert.True(over.Days[^1].Zones.Sum(z => z.Idle) > normal.Days[^1].Zones.Sum(z => z.Idle));
    }

    [Fact]
    public void Escasez_sin_insumos_falta_mercaderia_sube_el_precio_y_despues_se_recupera()
    {
        var (sim, _) = Run("escasez");
        var dry = sim.Days[3]; // día 4: puerto cerrado desde el 2, ya no queda nada
        Assert.Equal(0, dry.Zones.Sum(z => z.Served));
        Assert.True(dry.Zones.Sum(z => z.Shortage) > 0);
        Assert.True(MarketRef(dry) > MarketRef(sim.Days[0]));
        Assert.True(sim.Days[^1].Zones.Sum(z => z.Served) > 0, "no se recuperó al reabrir el puerto");
    }

    [Fact]
    public void Suba_de_precios_el_caro_vende_menos_pero_vende()
    {
        var (sim, _) = Run("suba");
        var last = sim.Days[^1];
        var a = last.Shops.Single(s => s.Shop == "Puesto A");
        var b = last.Shops.Single(s => s.Shop == "Puesto B");
        Assert.True(b.Sold < a.Sold);
        Assert.True(b.Sold > 0);
    }

    [Fact]
    public void Competencia_el_que_baja_el_precio_vende_igual_o_mas_pero_gana_menos()
    {
        var (sim, _) = Run("competencia");
        var last = sim.Days[^1];
        var a = last.Shops.Single(s => s.Shop == "Puesto A");
        var b = last.Shops.Single(s => s.Shop == "Puesto B");
        Assert.True(b.Sold >= a.Sold);
        Assert.True(b.ProfitCents < a.ProfitCents);
    }

    [Fact]
    public void Falta_de_abastecimiento_el_asalto_corta_un_dia_y_la_cadena_se_recupera()
    {
        var (sim, _) = Run("asalto");
        var robbed = sim.Days[2];
        Assert.True(robbed.LostInTransit > 0);
        Assert.True(robbed.Zones.Sum(z => z.Shortage) > sim.Days[1].Zones.Sum(z => z.Shortage) * 3);
        var after = sim.Days[3];
        Assert.True(after.Produced > 0 && after.Zones.Sum(z => z.Served) > robbed.Zones.Sum(z => z.Served));
        Assert.Equal(0, sim.Supplier.Debt);
    }
}
