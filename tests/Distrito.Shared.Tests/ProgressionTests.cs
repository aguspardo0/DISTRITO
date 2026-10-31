using Distrito.Shared.Progression;

namespace Distrito.Shared.Tests;

public class ProgressionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]   // nivel 1 → 2: 100
    [InlineData(299, 2)]
    [InlineData(300, 3)]   // nivel 2 → 3: 200 más
    [InlineData(43_500, 30)]
    [InlineData(10_000_000, 30)] // tope
    public void Nivel_por_experiencia(long xp, int level) => Assert.Equal(level, ProgressionRules.LevelForXp(xp));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(15, 3)]
    [InlineData(50, 5)]
    [InlineData(225, 10)]
    [InlineData(10_000, 10)]
    public void Nivel_de_oficio_por_practica(int practice, int level) => Assert.Equal(level, ProgressionRules.SkillLevel(practice));

    [Fact]
    public void Ventajas_aprobadas()
    {
        Assert.Equal(1_500, ProgressionRules.WithNegotiationBonus(1_500, 1));
        Assert.Equal(1_545, ProgressionRules.WithNegotiationBonus(1_500, 4)); // +3 %
        Assert.Equal(0, ProgressionRules.ExtraDeadlineSeconds(1));
        Assert.Equal(30, ProgressionRules.ExtraDeadlineSeconds(4));
        Assert.Equal(0, ProgressionRules.ExtraSalesPerCycle(4));
        Assert.Equal(1, ProgressionRules.ExtraSalesPerCycle(5));
        Assert.Equal(2, ProgressionRules.ExtraSalesPerCycle(10));
    }

    [Fact]
    public void Vista_de_progreso()
    {
        var p = new ProgressView(150, 5, 0, 50);
        Assert.Equal(2, p.Level);
        Assert.Equal(300, p.XpForNext);
        Assert.Equal(2, p.LogisticsLevel);
        Assert.Equal(5, p.AdministrationLevel);
        Assert.Equal(3, p.Skills.Count);
    }
}
