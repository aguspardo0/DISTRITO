using Distrito.Shared.Crews;

namespace Distrito.Shared.Tests;

public class CrewProgressTests
{
    [Fact]
    public void Rangos_de_prestigio()
    {
        Assert.Equal(1, PrestigeRules.RankFor(0));
        Assert.Equal(2, PrestigeRules.RankFor(100));
        Assert.Equal(3, PrestigeRules.RankFor(300));
        Assert.Equal(10, PrestigeRules.RankFor(100_000));
        Assert.Equal(3, PrestigeRules.MissionSlots(1));
        Assert.Equal(4, PrestigeRules.MissionSlots(4));
        Assert.Equal(5, PrestigeRules.MissionSlots(9));
        Assert.Equal(5, PrestigeRules.MorettiDiscountPercent(3));
        Assert.True(PrestigeRules.CasinoVip(6));
    }

    [Fact]
    public void El_tablero_es_igual_para_toda_la_cuadrilla_y_cambia_con_el_turno()
    {
        var crew = Guid.NewGuid();
        var a = MissionGenerator.Generate(crew, 100, 1);
        var b = MissionGenerator.Generate(crew, 100, 1);
        Assert.Equal(a.Select(m => m.Title), b.Select(m => m.Title));
        Assert.Equal(3, a.Count);
        Assert.Equal(3, a.Select(m => m.Kind).Distinct().Count()); // tipos distintos
        Assert.Equal(5, MissionGenerator.Generate(crew, 100, 10).Count);
    }

    [Fact]
    public void Pagas_y_prestigio_en_los_rangos_aprobados()
    {
        for (var slot = 0; slot < 200; slot++)
            foreach (var rank in new[] { 1, 5, 7 })
                foreach (var m in MissionGenerator.Generate(Guid.NewGuid(), slot, rank))
                {
                    Assert.InRange(m.RewardCents, 2_000, 8_000);
                    Assert.InRange(m.Prestige, 10, 40);
                    Assert.True(m.Target > 0);
                }
    }

    [Fact]
    public void Sedes_puerta_y_adentro()
    {
        var (x, z) = CrewBaseRules.Inside(0);
        Assert.True(CrewBaseRules.IsInside(0, x, z));
        var (fx, fz) = CrewBaseRules.Front(0);
        Assert.False(CrewBaseRules.IsInside(0, fx, fz));
        Assert.True(CrewBaseRules.AtBase(0, fx, fz));
        Assert.Equal(0, CrewBaseRules.ShedAt(fx, fz, 3.5f));
    }
}
