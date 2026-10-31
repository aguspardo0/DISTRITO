using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

public class FamilyRulesTests
{
    [Theory]
    [InlineData(0, 0, FamilyRules.Neutral)]
    [InlineData(9, 9, FamilyRules.Neutral)]
    [InlineData(10, 0, FamilyRules.Cooperation)]
    [InlineData(0, 10, FamilyRules.Rivalry)]
    [InlineData(30, 10, FamilyRules.Rivalry)] // si pasan las dos, gana rivalidad
    public void Estado_por_contratos_y_reputacion_perdida(int contracts, int lost, string mood) =>
        Assert.Equal(mood, FamilyRules.Decide(contracts, lost));

    [Fact]
    public void Efectos_aprobados()
    {
        Assert.Equal(1_650, FamilyRules.ContractPay(1_500, FamilyRules.Cooperation));
        Assert.Equal(1_350, FamilyRules.ContractPay(1_500, FamilyRules.Rivalry));
        Assert.Equal(1_500, FamilyRules.ContractPay(1_500, FamilyRules.Neutral));
        Assert.Equal(330, FamilyRules.SellPrice(300, FamilyRules.Rivalry));
        Assert.Equal(300, FamilyRules.SellPrice(300, FamilyRules.Cooperation));
    }

    [Fact]
    public void La_ventana_termina_al_comienzo_de_la_hora()
    {
        var end = FamilyRules.WindowEnd(DateTimeOffset.Parse("2026-10-31T16:47:12Z", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(DateTimeOffset.Parse("2026-10-31T16:00:00Z", System.Globalization.CultureInfo.InvariantCulture), end);
    }
}
