using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

/// <summary>Pizarras, libreta y diario: textos y reglas que ve el jugador.</summary>
public class MarketInfoRulesTests
{
    private static readonly DateOnly Day = new(2026, 11, 2);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    private static PriceBoardView Deposit(bool coffeeInStock = true, long coffee = 300) => new(EconomyCatalog.DepositNpc, "Almacén Moretti",
    [
        new(EconomyCatalog.CoffeeBagItem, "Bolsa de café", coffee, coffeeInStock),
        new(EconomyCatalog.DrinksCrateItem, "Cajón de bebidas", 400, true),
        new(EconomyCatalog.CoffeeBagItem, "Bolsa de café", 180, true, Buying: true),
    ], Now);

    private static NewspaperFacts Facts(List<PriceBoardView>? boards = null, List<FamilyMoodView>? families = null, int robberies = 0,
        Dictionary<string, long>? before = null, List<OpenBusinessFact>? open = null) =>
        new(Day, boards ?? [Deposit()], before ?? new(), families ?? [new(EconomyCatalog.Moretti, "Familia Moretti", FamilyRules.Neutral, 0, 0)],
            robberies, 0, [], open ?? [], [], [], 5, 0);

    [Fact]
    public void La_fecha_es_la_misma_de_hoy_pero_en_1952_con_su_dia_de_la_semana()
    {
        Assert.Equal("Domingo 2 de noviembre de 1952", MarketInfoRules.DateLine(Day));
        Assert.Equal("Viernes 29 de febrero de 1952", MarketInfoRules.DateLine(new DateOnly(2028, 2, 29)));
        Assert.Equal(1, MarketInfoRules.EditionOf(Day));
        Assert.Equal(31, MarketInfoRules.EditionOf(new DateOnly(2026, 12, 2)));
    }

    [Fact]
    public void El_dia_del_diario_es_el_de_Argentina()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), MarketInfoRules.DayOf(new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero))); // 23 h en Argentina
        Assert.Equal(Day, MarketInfoRules.DayOf(new DateTimeOffset(2026, 11, 2, 4, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void La_pizarra_muestra_precios_agotados_lo_que_compran_y_la_nota()
    {
        var text = MarketInfoRules.BoardText(Deposit(coffeeInStock: false) with { Note = "1 local abierto" });
        Assert.StartsWith("ALMACÉN MORETTI", text);
        Assert.Contains("Bolsa de café  AGOTADO", text);
        Assert.Contains("Cajón de bebidas  $4.00", text);
        Assert.Contains("— COMPRAMOS —", text);
        Assert.Contains("Bolsa de café  $1.80", text);
        Assert.EndsWith("1 local abierto", text);
    }

    [Fact]
    public void Flechas_de_suba_y_baja()
    {
        Assert.Equal(1, MarketInfoRules.Trend(330, 300));
        Assert.Equal(-1, MarketInfoRules.Trend(270, 300));
        Assert.Equal(0, MarketInfoRules.Trend(300, 300));
        Assert.Equal(0, MarketInfoRules.Trend(300, null));
        Assert.Equal("▲", MarketInfoRules.Arrow(1));
    }

    [Fact]
    public void La_libreta_envejece()
    {
        var note = new PriceNote(EconomyCatalog.DepositNpc, "Almacén Moretti", EconomyCatalog.CoffeeBagItem, "Bolsa de café", 300, true, false, Now);
        Assert.False(note.IsStale(Now.AddHours(5)));
        Assert.True(note.IsStale(Now.AddHours(7)));
    }

    [Fact]
    public void Titular_rivalidad_primero()
    {
        var paper = MarketInfoRules.Write(Facts(families: [new(EconomyCatalog.Moretti, "Familia Moretti", FamilyRules.Rivalry, 0, 4)], robberies: 5));
        Assert.Equal("TENSIÓN: LOS MORETTI SE PONEN DUROS", paper.Headline);
        Assert.Contains("10 %", paper.Lead);
        Assert.Contains(paper.News, n => n.StartsWith("Policiales: 5 asaltos"));
    }

    [Fact]
    public void Titular_asaltos_faltantes_suba_y_calma()
    {
        Assert.Equal("OLA DE ASALTOS EN BELLMONT", MarketInfoRules.Write(Facts(robberies: 3)).Headline);
        Assert.Equal("FALTA BOLSA DE CAFÉ EN EL MERCADO", MarketInfoRules.Write(Facts(boards: [Deposit(coffeeInStock: false)])).Headline);
        var up = MarketInfoRules.Write(Facts(boards: [Deposit(coffee: 330)],
            before: new() { [MarketInfoRules.Key(EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, false)] = 300 }));
        Assert.Equal("SUBE BOLSA DE CAFÉ", up.Headline);
        Assert.Contains(up.Quotes, q => q.Item == "Bolsa de café" && !q.Buying && q.Trend == 1);
        var calm = MarketInfoRules.Write(Facts());
        Assert.Equal("CALMA EN EL MERCADO", calm.Headline);
        Assert.Contains("5 entregas", calm.Lead);
    }

    [Fact]
    public void Cotizaciones_y_avisos()
    {
        var paper = MarketInfoRules.Write(Facts(open: [new("Café Don Sal", "Puesto de café", "la Esquina del café", false, true)]));
        Assert.Equal(3, paper.Quotes.Count);
        Assert.Single(paper.Quotes, q => q.Buying);
        Assert.Contains(paper.Classifieds, a => a.StartsWith("SE BUSCA AYUDANTE para «Café Don Sal»"));
        Assert.Contains(paper.Classifieds, a => a.StartsWith("ALMACÉN MORETTI"));
        Assert.True(paper.Classifieds.Count >= 3);
        var again = MarketInfoRules.Write(Facts(open: [new("Café Don Sal", "Puesto de café", "la Esquina del café", false, true)]));
        Assert.Equal(paper.Headline, again.Headline); // mismos hechos, mismo diario
        Assert.Equal(paper.Classifieds, again.Classifieds);
    }
}
