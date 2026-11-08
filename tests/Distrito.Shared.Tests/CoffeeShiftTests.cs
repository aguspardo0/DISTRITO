using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

/// <summary>Turnos de empleado en la Esquina del café (primera hora de juego).</summary>
public class CoffeeShiftTests
{
    private static readonly CoffeeOrder Order = new(1, "Un camionero", 2, CoffeeShiftRules.WithMilk);

    [Fact]
    public void Bien_servido_es_perfecto_y_rebalsado_no_sirve()
    {
        Assert.Equal(1, CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.86, 2, CoffeeShiftRules.WithMilk), 10));
        Assert.Equal(0, CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.99, 2, CoffeeShiftRules.WithMilk), 10));
        Assert.Equal(0, CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.1, 2, CoffeeShiftRules.WithMilk), 10));
        // Lo que pidió, a medias: la mitad; nada de lo que pidió: casi nada.
        Assert.Equal(0.5, CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.85, 1, CoffeeShiftRules.WithMilk), 10));
        Assert.Equal(0.2, CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.85, 0, ""), 10));
        // Aguado (la urna vacía) y lento valen menos.
        Assert.Equal(0.5, CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.85, 2, CoffeeShiftRules.WithMilk, Watery: true), 10));
        Assert.True(CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.85, 2, CoffeeShiftRules.WithMilk), 45) < 1);
        Assert.True(CoffeeShiftRules.Score(Order, new CoffeeServe(1, 0.7, 2, CoffeeShiftRules.WithMilk), 10) < 1);
    }

    [Fact]
    public void Se_cobra_el_sueldo_con_seis_cafes_y_la_propina_segun_como_salieron()
    {
        Assert.Equal((0, 0), CoffeeShiftRules.Earnings([1, 1, 1, 1, 1]));
        Assert.Equal((300, 100), CoffeeShiftRules.Earnings(Enumerable.Repeat(1.0, 14).ToList()));
        var (pay, tip) = CoffeeShiftRules.Earnings(Enumerable.Repeat(0.5, 12).ToList());
        Assert.Equal(300, pay);
        Assert.Equal(50, tip);
        Assert.True(CoffeeShiftRules.Earnings(Enumerable.Repeat(1.0, 6).ToList()).Tip < 100); // pocos cafés, menos propina
    }

    [Fact]
    public void Los_pedidos_son_variados_y_siempre_iguales_para_el_mismo_turno()
    {
        var orders = Enumerable.Range(0, 20).Select(i => CoffeeShiftRules.Order(1234, i)).ToList();
        Assert.Equal(orders, Enumerable.Range(0, 20).Select(i => CoffeeShiftRules.Order(1234, i)));
        Assert.True(orders.Select(o => o.Milk).Distinct().Count() == 3);
        Assert.True(orders.Select(o => o.Sugar).Distinct().Count() == 3);
        Assert.True(orders.Select(o => o.Customer).Distinct().Count() >= 5);
        Assert.Equal("con leche, un terrón", new CoffeeOrder(0, "x", 1, CoffeeShiftRules.WithMilk).Text);
        Assert.Equal("negro, sin azúcar", new CoffeeOrder(0, "x", 0, "").Text);
    }
}

/// <summary>Las misiones del café (historia de Don Aldo).</summary>
public class CafeStoryTests
{
    [Fact]
    public void Son_cuatro_capitulos_que_da_Don_Aldo_con_premios_de_5_a_15_pesos()
    {
        var cafe = FamilyStories.Cafe;
        Assert.Equal(FamilyStories.CafeKey, cafe.StoryKey);
        Assert.True(cafe.NeedsCoffeeStand);
        Assert.Equal(4, cafe.Steps.Count);
        Assert.All(cafe.Steps, s => Assert.InRange(s.RewardCents, 500, 1_500));
        Assert.Same(cafe, FamilyStories.Of(FamilyStories.CafeKey));
        Assert.Same(FamilyStories.Moretti, FamilyStories.Of(EconomyCatalog.Moretti));
        for (var i = 0; i < cafe.Steps.Count; i++)
        {
            var d = FamilyStories.ToDraft(Guid.NewGuid(), FamilyStories.CafeKey, i);
            Assert.Equal(EconomyCatalog.CoffeeCornerNpc, d.GiverNpc);
            Assert.Equal(cafe.Steps[i].Family, d.Family);
            Assert.Same(cafe, FamilyStories.OfTitle(d.Title));
        }
        // Las de las familias siguen dándolas los capos.
        Assert.Equal(FamilyPeople.BossOf(EconomyCatalog.Caruso), FamilyStories.ToDraft(Guid.NewGuid(), EconomyCatalog.Caruso, 0).GiverNpc);
    }
}
