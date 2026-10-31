using System.Globalization;
using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

public class TerritoryRulesTests
{
    private static DateTimeOffset At(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void Temporadas_de_cuatro_semanas_desde_el_lunes_26_de_octubre()
    {
        Assert.Equal(1, TerritoryRules.SeasonAt(At("2026-10-31T22:00:00Z")));
        Assert.Equal(1, TerritoryRules.SeasonAt(At("2026-11-23T02:59:59Z")));
        Assert.Equal(2, TerritoryRules.SeasonAt(At("2026-11-23T03:00:00Z")));
        Assert.Equal(At("2026-11-23T03:00:00Z"), TerritoryRules.SeasonEnd(1));
    }

    [Fact]
    public void El_dia_se_cuenta_en_hora_de_argentina()
    {
        Assert.Equal(At("2026-10-31T03:00:00Z"), TerritoryRules.DayStart(At("2026-11-01T02:30:00Z"))); // 23:30 del 31
        Assert.Equal(At("2026-11-01T03:00:00Z"), TerritoryRules.DayStart(At("2026-11-01T03:30:00Z")));
    }

    [Fact]
    public void Tope_diario_y_umbral_de_control()
    {
        Assert.Equal(3, TerritoryRules.Grantable(3, 0));
        Assert.Equal(1, TerritoryRules.Grantable(3, 29));
        Assert.Equal(0, TerritoryRules.Grantable(3, 30));

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var t0 = At("2026-11-01T00:00:00Z");
        Assert.Null(TerritoryRules.Controller([(a, 19, t0)]));
        Assert.Equal(a, TerritoryRules.Controller([(a, 25, t0), (b, 22, t0)])!.Value.Crew);
        Assert.Equal(b, TerritoryRules.Controller([(a, 25, t0.AddHours(1)), (b, 25, t0)])!.Value.Crew); // empate: llegó primero
        Assert.Equal(105, TerritoryRules.WithControlBonus(100));
    }

    [Fact]
    public void Cada_npc_pertenece_a_una_zona()
    {
        Assert.Equal(TerritoryRules.Corner, TerritoryRules.ZoneOfNpc(EconomyCatalog.JazzClubNpc));
        Assert.Equal(TerritoryRules.Workshops, TerritoryRules.ZoneOfNpc(EconomyCatalog.WorkshopNpc));
        Assert.Equal(4, TerritoryRules.Zones.Count);
    }
}
