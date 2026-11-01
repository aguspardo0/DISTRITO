using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

public class CasinoRulesTests
{
    [Fact]
    public void Ruleta_paga_y_el_cero_es_de_la_casa()
    {
        Assert.Equal(200, CasinoRules.RoulettePayout("rojo", 0, 1, 100));
        Assert.Equal(0, CasinoRules.RoulettePayout("rojo", 0, 2, 100));
        Assert.Equal(0, CasinoRules.RoulettePayout("par", 0, 0, 100));
        Assert.Equal(3600, CasinoRules.RoulettePayout("numero", 17, 17, 100));
        // Ventaja de la casa ~2,7 %: devolución esperada de una apuesta sencilla = 36/37.
        var expected = Enumerable.Range(0, 37).Sum(ball => CasinoRules.RoulettePayout("negro", 0, ball, 100)) / 37.0;
        Assert.InRange(expected, 97.2, 97.4);
    }

    [Fact]
    public void Dados_de_pase()
    {
        var seq = new Queue<int>([3, 4]);
        Assert.True(CasinoRules.PassLine(() => seq.Dequeue()).Win);   // 7 de entrada
        seq = new Queue<int>([1, 1]);
        Assert.False(CasinoRules.PassLine(() => seq.Dequeue()).Win);  // 2
        seq = new Queue<int>([2, 2, 1, 2, 3, 1]);                      // punto 4, sale 3, después 4
        var (win, rolls) = CasinoRules.PassLine(() => seq.Dequeue());
        Assert.True(win);
        Assert.Equal(3, rolls.Count);
    }

    [Fact]
    public void Valor_de_manos_de_blackjack()
    {
        Assert.Equal(21, CasinoRules.HandValue([1, 13]));
        Assert.True(CasinoRules.IsBlackjack([1, 12]));
        Assert.Equal(12, CasinoRules.HandValue([1, 1]));
        Assert.Equal(22, CasinoRules.HandValue([10, 10, 2]));
        Assert.Equal(17, CasinoRules.HandValue([1, 6]));
    }
}
