using Distrito.Shared.Progression;

namespace Distrito.Shared.Tests;

public class SkillTreeTests
{
    [Fact]
    public void Puntos_niveles_y_requisitos()
    {
        var none = new Dictionary<string, int>();
        Assert.Equal(0, SkillTree.PointsFor(1));
        Assert.Equal(7, SkillTree.PointsFor(8));
        Assert.NotNull(SkillTree.WhyNot(SkillTree.ToughSkin, 1, none));                 // nivel 1: sin puntos
        Assert.Null(SkillTree.WhyNot(SkillTree.ToughSkin, 2, none));
        Assert.NotNull(SkillTree.WhyNot(SkillTree.SteadyHand, 8, none));                // pide Piel dura
        Assert.NotNull(SkillTree.WhyNot(SkillTree.SteadyHand, 4, new Dictionary<string, int> { [SkillTree.ToughSkin] = 1 })); // pide nivel 5
        Assert.Null(SkillTree.WhyNot(SkillTree.SteadyHand, 5, new Dictionary<string, int> { [SkillTree.ToughSkin] = 1 }));
        Assert.NotNull(SkillTree.WhyNot(SkillTree.ToughSkin, 9, new Dictionary<string, int> { [SkillTree.ToughSkin] = 2 }));  // al máximo
        Assert.Equal(20, SkillTree.Effect(new Dictionary<string, int> { [SkillTree.ToughSkin] = 2 }, SkillTree.ToughSkin));
    }

    [Fact]
    public void Los_nodos_son_coherentes()
    {
        Assert.Equal(SkillTree.Nodes.Count, SkillTree.Nodes.Select(n => n.Code).Distinct().Count());
        foreach (var n in SkillTree.Nodes)
        {
            Assert.Contains(SkillTree.Branches, b => b.Code == n.Branch);
            if (n.Requires is { } req)
            {
                var parent = SkillTree.Find(req);
                Assert.NotNull(parent);
                Assert.Equal(n.Branch, parent!.Branch);
                Assert.True(parent.Tier < n.Tier);
            }
        }
    }
}
