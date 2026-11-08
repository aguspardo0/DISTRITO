using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

/// <summary>La competencia que no es de ningún jugador (Café La Perla, El Gato Negro): se lleva clientes, no plata.</summary>
public class GrowthRivalTests
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
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    private static GrowthConfig Without()
    {
        var c = GrowthConfig.Parse(File.ReadAllText(RepoFile("config/negocios.json")));
        c.Rivals.Clear();
        return c;
    }

    private static GrowthState Stand(double fame = 0, long price = 500, int quality = 0, int attraction = 0) =>
        new(Guid.NewGuid(), EconomyCatalog.CoffeeStandLocation, fame, price, 0, quality, attraction);

    [Fact]
    public void Hay_un_cafe_y_un_club_de_la_competencia_y_la_config_es_valida()
    {
        Assert.Empty(Cfg.Validate());
        Assert.Contains(Cfg.Rivals, r => r.Name == "Café La Perla" && r.Kind == EconomyCatalog.CoffeeStandLocation);
        Assert.Contains(Cfg.Rivals, r => r.Kind == EconomyCatalog.JazzClubKind);
        Assert.Equal(Cfg.Rivals.Count, Cfg.Rivals.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void La_competencia_se_lleva_parte_de_la_gente_del_puesto_nuevo()
    {
        var me = Stand();
        var with = BusinessGrowth.Demand(Cfg, me, [me], Noon).Local;
        var without = BusinessGrowth.Demand(Without(), me, [me], Noon).Local;
        Assert.True(with < without, $"con La Perla {with:0.00}, sin {without:0.00}");
        // Pero no lo deja sin clientes: un puesto nuevo igual vende (la capacidad de base es 3 por ciclo).
        Assert.True(with > 2, $"el puesto nuevo quedó con {with:0.00} clientes por ciclo");
    }

    [Fact]
    public void Un_puesto_mejor_y_mas_barato_le_saca_la_gente_a_la_competencia()
    {
        var perla = Cfg.Rivals.First(r => r.Kind == EconomyCatalog.CoffeeStandLocation).State;
        var weak = Stand();
        var strong = Stand(fame: 2000, price: 550, quality: 3, attraction: 2);
        var perlaVsWeak = BusinessGrowth.Demand(Cfg, perla, [weak], Noon).Local;
        var perlaVsStrong = BusinessGrowth.Demand(Cfg, perla, [strong], Noon).Local;
        Assert.True(perlaVsStrong < perlaVsWeak * 0.8, $"La Perla: {perlaVsWeak:0.00} contra un puesto nuevo, {perlaVsStrong:0.00} contra uno bueno");
        Assert.True(BusinessGrowth.Demand(Cfg, strong, [strong], Noon).Local > perlaVsStrong);
    }

    [Fact]
    public void Un_competidor_mal_cargado_se_rechaza()
    {
        var bad = GrowthConfig.Parse(File.ReadAllText(RepoFile("config/negocios.json")));
        bad.Rivals.Add(new GrowthRival { Name = "Fantasma", Kind = "no-existe", PriceCents = 0 });
        Assert.Contains(bad.Validate(), e => e.Contains("Fantasma"));
    }
}
