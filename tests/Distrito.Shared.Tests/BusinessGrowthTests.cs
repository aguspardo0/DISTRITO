using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

/// <summary>Negocios que crecen: clientela, calidad, precio, fama, mejoras sin tope y eventos.</summary>
public class BusinessGrowthTests
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

    private static readonly GrowthConfig Cfg = GrowthConfig.Parse(File.ReadAllText(RepoFile("config/negocios.json")));
    private const string Cafe = EconomyCatalog.CoffeeStandLocation;
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero); // 12:00 en Argentina

    private static GrowthState Stand(double fame = 0, long price = 500, int cap = 0, int quality = 0, int attraction = 0) =>
        new(Guid.NewGuid(), Cafe, fame, price, cap, quality, attraction);

    private static double Customers(GrowthState me, params GrowthState[] rivals) =>
        BusinessGrowth.Demand(Cfg, me, [.. rivals, me], Noon).Total;

    [Fact]
    public void La_config_del_repo_es_valida()
    {
        Assert.Empty(Cfg.Validate());
    }

    [Fact]
    public void Mas_caro_viene_menos_gente_y_la_calidad_trae_mas_aunque_cobre_el_precio_justo()
    {
        var basic = Stand();
        Assert.True(Customers(basic) > 1, $"un puesto nuevo tiene clientes: {Customers(basic):0.00}");
        Assert.True(Customers(Stand(price: 900)) < Customers(basic));

        var better = Stand(quality: 4);
        var fair = BusinessGrowth.FairPriceCents(Cfg, better);
        Assert.True(fair > 500);
        Assert.True(Customers(better with { PriceCents = fair }) > Customers(basic), "con más calidad y precio justo viene más gente");
        // Contra un rival de calidad, el básico pierde gente.
        Assert.True(Customers(basic, better with { PriceCents = fair }) < Customers(basic));
    }

    [Fact]
    public void La_fama_y_la_atraccion_traen_gente_sin_techo()
    {
        var a = Customers(Stand(fame: 0));
        var b = Customers(Stand(fame: 2000));
        var c = Customers(Stand(fame: 50_000));
        Assert.True(b > a && c > b);
        Assert.True(Customers(Stand(attraction: 3)) > a);
    }

    [Fact]
    public void Capacidad_y_mejoras_sin_tope_cada_nivel_mas_caro()
    {
        Assert.Equal(3, BusinessGrowth.CapacityOf(Cfg, Stand()));
        Assert.Equal(13, BusinessGrowth.CapacityOf(Cfg, Stand(cap: 10)));
        Assert.Equal(4, BusinessGrowth.CapacityOf(Cfg, Stand() with { HasHelper = true }));
        long last = 0;
        for (var level = 0; level < 30; level++)
        {
            var cost = BusinessGrowth.UpgradeCost(Cfg, Cafe, BusinessGrowth.Capacity, level);
            Assert.True(cost > last);
            last = cost;
        }
        Assert.Equal("Mostrador grande", BusinessGrowth.UpgradeName(Cfg, Cafe, BusinessGrowth.Capacity, 1));
        Assert.Equal("Segundo salón 3", BusinessGrowth.UpgradeName(Cfg, Cafe, BusinessGrowth.Capacity, 7)); // después de los nombres, sigue numerado
        Assert.True(BusinessGrowth.Rent(Cfg, Stand(cap: 4)) > BusinessGrowth.Rent(Cfg, Stand()));
    }

    [Fact]
    public void Categorias_y_nombres_de_fama()
    {
        Assert.Equal("Puesto", BusinessGrowth.Category(Cfg, Stand()));
        Assert.Equal("Café", BusinessGrowth.Category(Cfg, Stand(cap: 2, quality: 2)));
        Assert.Equal("Gran confitería", BusinessGrowth.Category(Cfg, Stand(cap: 8, quality: 8, attraction: 8)));
        Assert.Equal("Gran confitería ★", BusinessGrowth.Category(Cfg, Stand(cap: 12, quality: 12, attraction: 12)));
        Assert.Equal("Nuevo", BusinessGrowth.FameName(Cfg, 0));
        Assert.Equal("Popular", BusinessGrowth.FameName(Cfg, 700));
        Assert.Equal("Leyenda de Bellmont", BusinessGrowth.FameName(Cfg, 6000));
        Assert.Equal("Leyenda de Bellmont ★★", BusinessGrowth.FameName(Cfg, 25_000));
        Assert.Equal(24_000, BusinessGrowth.NextFameGoal(Cfg, 13_000));
    }

    [Fact]
    public void La_fama_sube_atendiendo_bien_y_baja_de_a_poco_cerrado()
    {
        var s = Stand(fame: 1000);
        Assert.True(BusinessGrowth.NextFame(Cfg, s, served: 5, 0, 0, open: true) > 1000);
        var closed = BusinessGrowth.NextFame(Cfg, s, 0, lostNoStock: 50, 0, open: false);
        Assert.True(closed < 1000 && closed > 990, $"cerrado baja de a poco: {closed}");
        // Caro para la calidad: suma menos fama por cliente.
        Assert.True(BusinessGrowth.NextFame(Cfg, s with { PriceCents = 1200 }, 5, 0, 0, true) < BusinessGrowth.NextFame(Cfg, s, 5, 0, 0, true));
    }

    [Fact]
    public void El_critico_y_el_evento_del_dia()
    {
        Assert.Equal(1, BusinessGrowth.CriticStars(Cfg, Stand(), open: false));
        Assert.Equal(3, BusinessGrowth.CriticStars(Cfg, Stand(), open: true));
        Assert.Equal(5, BusinessGrowth.CriticStars(Cfg, Stand(quality: 6) with { PriceCents = 600 }, open: true));
        Assert.True(BusinessGrowth.CriticFame(Cfg, 5) > 0 && BusinessGrowth.CriticFame(Cfg, 1) < 0);
        var day = new DateOnly(2026, 11, 3);
        var critic = BusinessGrowth.CriticAt(Guid.NewGuid(), day).ToOffset(TerritoryRules.ArgentinaOffset);
        Assert.InRange(critic.Hour, 10, 21);

        foreach (var kind in EconomyCatalog.BusinessTypes.Keys)
        {
            var start = BusinessGrowth.EventStart(Cfg, kind, day);
            Assert.True(BusinessGrowth.InEvent(Cfg, kind, start.AddMinutes(1)));
            Assert.False(BusinessGrowth.InEvent(Cfg, kind, BusinessGrowth.EventEnd(Cfg, kind, day).AddMinutes(1)));
            var me = new GrowthState(Guid.NewGuid(), kind, 0, EconomyCatalog.BusinessTypes[kind].SalePriceCents, 0, 0, 0);
            Assert.True(BusinessGrowth.Demand(Cfg, me, [me], start.AddMinutes(1)).Event);
        }
    }

    [Fact]
    public void Vista_del_dueno()
    {
        var s = Stand(fame: 700, cap: 1, quality: 2);
        var view = BusinessGrowthViews.Build(Cfg, s, 3, new DateOnly(2026, 11, 3), 4, "«x» muy bien");
        Assert.Equal("Popular", view.FameName);
        Assert.Equal(4, view.Capacity);
        Assert.Equal(3, view.Upgrades.Count);
        Assert.Equal("Mesitas en la vereda", view.Upgrades.Single(u => u.Branch == BusinessGrowth.Capacity).Next);
        Assert.Equal(BusinessGrowth.UpgradeCost(Cfg, Cafe, BusinessGrowth.Quality, 2), view.Upgrades.Single(u => u.Branch == BusinessGrowth.Quality).CostCents);
        Assert.Equal(250, view.MinPriceCents);
        Assert.Equal(1500, view.MaxPriceCents);
    }
}
