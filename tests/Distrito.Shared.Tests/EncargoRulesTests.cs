using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

public class EncargoRulesTests
{
    private static readonly Guid Someone = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public void Las_ofertas_son_las_mismas_para_el_mismo_turno_y_cambian_con_el_turno()
    {
        var a = EncargoGenerator.Generate(Someone, 1000, 5);
        var b = EncargoGenerator.Generate(Someone, 1000, 5);
        var c = EncargoGenerator.Generate(Someone, 1001, 5);
        Assert.Equal(a.Select(x => (x.Id, x.Title, x.RewardCents)), b.Select(x => (x.Id, x.Title, x.RewardCents)));
        Assert.Equal(EncargoRules.Offers, a.Count);
        Assert.Empty(a.Select(x => x.Id).Intersect(c.Select(x => x.Id)));
        Assert.Equal(a.Count, a.Select(x => x.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(10)] [InlineData(30)]
    public void Los_numeros_respetan_lo_aprobado(int level)
    {
        for (long slot = 0; slot < 40; slot++)
            foreach (var d in EncargoGenerator.Generate(Guid.NewGuid(), slot, level))
            {
                Assert.InRange(d.RewardCents, 800, EncargoRules.MaxRewardCents);
                Assert.InRange(d.Xp, 60, 150);
                Assert.InRange(d.Reputation, 2, 5);
                Assert.True(EncargoKinds.MinLevel(d.Kind) <= level, $"{d.Kind} no está desbloqueado en nivel {level}");
                Assert.False(d.Daily);
            }
    }

    [Fact]
    public void La_paga_crece_con_el_nivel_y_tiene_tope()
    {
        Assert.Equal(1_000, EncargoRules.Reward(1, EncargoKinds.Entrega, 0).Cents);   // $8 + $1,50 → $9,50 → $10
        Assert.True(EncargoRules.Reward(10, EncargoKinds.Entrega, 2).Cents > EncargoRules.Reward(2, EncargoKinds.Entrega, 2).Cents);
        Assert.Equal(EncargoRules.MaxRewardCents, EncargoRules.Reward(30, EncargoKinds.Reparto, 4).Cents);
    }

    [Fact]
    public void El_encargo_del_dia_paga_dos_veces_y_media()
    {
        for (var day = 0; day < 30; day++)
        {
            var d = EncargoGenerator.Daily(Someone, day, 8);
            Assert.True(d.Daily);
            Assert.StartsWith("Encargo del día", d.Title);
            Assert.InRange(d.RewardCents, 2_000, (long)(EncargoRules.MaxRewardCents * EncargoRules.DailyMultiplier) + 100);
            Assert.InRange(d.Xp, 150, 375);
        }
        Assert.Equal(EncargoGenerator.Daily(Someone, 7, 8).Id, EncargoGenerator.Daily(Someone, 7, 8).Id);
    }

    [Fact]
    public void Las_familias_se_mezclan_y_cada_una_encarga_contra_sus_rivales()
    {
        for (long slot = 0; slot < 60; slot++)
        {
            var offers = EncargoGenerator.Generate(Guid.NewGuid(), slot, 12);
            Assert.True(offers.Select(o => o.Family).Distinct().Count() >= 2, "las ofertas deberían ser de al menos dos familias");
            foreach (var o in offers)
            {
                var giver = FamilyPeople.Find(o.GiverNpc)!;
                Assert.Equal(o.Family, giver.Family);
                Assert.Equal(EncargoKinds.Street.Contains(o.Kind) ? "calle" : "negocios", giver.Role);
                if (o.Kind == EncargoKinds.Asalto)
                    Assert.NotEqual(o.Family, EncargoRules.VictimFamily(o.TargetNpc!));
                if (o.Kind is EncargoKinds.Cobro or EncargoKinds.Vigilar)
                    Assert.NotEqual(o.Family, FamilyRules.FamilyOfNpc(o.TargetNpc!));
                if (o.Kind == EncargoKinds.Recado)
                    Assert.NotEqual(o.Family, FamilyRules.FamilyOfNpc(o.TargetNpc!));
            }
        }
    }

    [Fact]
    public void El_paso_actual_dice_a_quien_ir()
    {
        var d = Enumerable.Range(0, 50).SelectMany(slot => EncargoGenerator.Generate(Someone, slot, 12))
            .First(x => x.Kind is EncargoKinds.Cobro or EncargoKinds.Paquete or EncargoKinds.Recado);
        var offer = EncargoGenerator.ToOffer(d, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(d.GiverNpc, offer.NextNpc);
        var active = offer with { Status = EncargoStatus.Active };
        Assert.Equal(d.TargetNpc, active.NextNpc);
        Assert.Equal(d.GiverNpc, (active with { Stage = 1 }).NextNpc);
        Assert.Contains("Volvé", (active with { Stage = 1 }).StepText(c => c));
    }

    [Fact]
    public void Cada_historia_tiene_cuatro_capitulos_150_pesos_y_800_de_experiencia()
    {
        Assert.Equal(3, FamilyStories.All.Count);
        foreach (var story in FamilyStories.All)
        {
            Assert.Equal(4, story.Steps.Count);
            Assert.Equal(15_000, story.Steps.Sum(s => s.RewardCents));
            Assert.Equal(800, story.Steps.Sum(s => s.Xp));
            Assert.Equal(story.Steps.Count, story.Steps.Select(s => s.Title).Distinct().Count());
            foreach (var (step, i) in story.Steps.Select((s, i) => (s, i)))
            {
                var d = FamilyStories.ToDraft(Guid.NewGuid(), story.Family, i);
                Assert.Equal(FamilyPeople.BossOf(story.Family), d.GiverNpc); // los da y los cobra el capo
                // Siempre contra los rivales: nunca se le cobra, vigila o asalta a la propia familia.
                if (step.Target is { } target && step.Kind is EncargoKinds.Cobro or EncargoKinds.Vigilar or EncargoKinds.Asalto)
                    Assert.NotEqual(story.Family, step.Kind == EncargoKinds.Asalto ? EncargoRules.VictimFamily(target) : FamilyRules.FamilyOfNpc(target));
            }
        }
        Assert.NotEqual(FamilyStories.OfferId(Someone, "moretti", 0, 0), FamilyStories.OfferId(Someone, "moretti", 0, 1));
    }
}
