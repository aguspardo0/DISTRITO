using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Lo que el jugador ve del mercado: pizarras, libreta, diario, informante y libro de cuentas.</summary>
[Trait("Category", "Database")]
public class MarketInfoApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> Character(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser("mi");
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    [Fact]
    public async Task Pizarras_y_libreta_de_precios()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await Character(api);

        var boards = (await api.GetMarketBoardsAsync(Key)).Value!;
        var deposit = boards.Single(b => b.NpcCode == EconomyCatalog.DepositNpc);
        var coffee = deposit.Lines.Single(l => l.ItemCode == EconomyCatalog.CoffeeBagItem && !l.Buying);
        Assert.True(coffee.PriceCents >= EconomyCatalog.CoffeeBagPriceCents);
        Assert.Contains(deposit.Lines, l => l.Buying && l.ItemCode == EconomyCatalog.CoffeeBagItem);
        Assert.Contains(boards, b => b.NpcCode == EconomyCatalog.WorkshopNpc && b.Lines.Count == Vehicles.All.Count);
        var kiosk = boards.Single(b => b.NpcCode == MarketInfoRules.KioskNpc);
        Assert.StartsWith("«", kiosk.Note); // el titular del día en la pizarra del kiosco

        // Sin pasar por ningún lado, la libreta está vacía; al pasar frente al Almacén queda anotado.
        Assert.Empty((await api.GetMarketInfoAsync(Key, me)).Value!.Notes);
        Assert.True((await api.LookAtBoardAsync(Key, new MarketLookRequest(me, EconomyCatalog.DepositNpc))).IsSuccess);
        var notes = (await api.GetMarketInfoAsync(Key, me)).Value!.Notes;
        Assert.Equal(deposit.Lines.Count, notes.Count);
        Assert.All(notes, n => Assert.Equal("Almacén Moretti", n.Place));
        Assert.Contains(notes, n => n.ItemCode == EconomyCatalog.CoffeeBagItem && !n.Buying && n.PriceCents == coffee.PriceCents);

        // Volver a pasar actualiza, no duplica.
        await api.LookAtBoardAsync(Key, new MarketLookRequest(me, EconomyCatalog.DepositNpc));
        Assert.Equal(deposit.Lines.Count, (await api.GetMarketInfoAsync(Key, me)).Value!.Notes.Count);
    }

    [Fact]
    public async Task El_diario_se_compra_una_vez_por_dia_y_la_plata_va_al_kiosco()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await Character(api);

        var before = (await api.GetMarketInfoAsync(Key, me)).Value!;
        Assert.Null(before.Newspaper);
        Assert.NotNull(before.Teaser);
        Assert.Empty(before.Teaser!.Quotes); // sin comprarlo, solo el titular
        Assert.Contains("1952", before.Teaser.DateLine);

        var bought = (await api.BuyMarketInfoAsync(Key, new MarketBuyRequest(me, MarketInfoRules.Newspaper, Req()))).Value!;
        Assert.True(bought.Ok, bought.Message);
        Assert.True(bought.Snapshot.NewspaperToday);
        Assert.Equal(EconomyCatalog.StartingFundsCents - MarketInfoRules.NewspaperPriceCents, bought.Snapshot.BalanceCents);
        Assert.Equal("already_owned", (await api.BuyMarketInfoAsync(Key, new MarketBuyRequest(me, MarketInfoRules.Newspaper, Req()))).Value!.ErrorCode);

        var paper = (await api.GetMarketInfoAsync(Key, me)).Value!.Newspaper!;
        Assert.Equal(before.Teaser.Headline, paper.Headline); // la edición del día es una sola
        Assert.Contains(paper.Quotes, q => q.Place == "Almacén Moretti");
        Assert.NotEmpty(paper.Classifieds);
    }

    [Fact]
    public async Task Nino_vende_una_hora_de_informacion()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await Character(api);

        Assert.Null((await api.GetMarketInfoAsync(Key, me)).Value!.Report);
        Assert.Equal("unknown", (await api.BuyMarketInfoAsync(Key, new MarketBuyRequest(me, "radio", Req()))).Value!.ErrorCode);
        var paid = (await api.BuyMarketInfoAsync(Key, new MarketBuyRequest(me, MarketInfoRules.Informant, Req()))).Value!;
        Assert.True(paid.Ok, paid.Message);
        Assert.True(paid.Snapshot.HasInformant(DateTimeOffset.UtcNow));
        Assert.Equal(EconomyCatalog.StartingFundsCents - MarketInfoRules.InformantPriceCents, paid.Snapshot.BalanceCents);
        Assert.Equal("already_owned", (await api.BuyMarketInfoAsync(Key, new MarketBuyRequest(me, MarketInfoRules.Informant, Req()))).Value!.ErrorCode);

        var report = (await api.GetMarketInfoAsync(Key, me)).Value!.Report!;
        Assert.True(report.ValidUntil > DateTimeOffset.UtcNow.AddMinutes(55));
        Assert.Contains(report.Lines, l => l.Topic == "Almacén Moretti" && l.Text.Contains("quedan"));
        Assert.Contains(report.Lines, l => l.Topic == "Taller Caruso" && l.Text.Contains("puerta de atrás"));
        Assert.Contains(report.Lines, l => l.Topic == "Fletes en la calle");
    }

    [Fact]
    public async Task Libro_de_cuentas_con_clientes_perdidos()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var economy = TestBackend.Economy();
        var me = await Character(api);

        Assert.Empty((await api.GetMarketInfoAsync(Key, me)).Value!.Ledgers);
        Assert.True((await api.BusinessActionAsync(Key, "license", new BusinessActionRequest(me, Req()))).Value!.Ok);
        // Con lo que queda alcanza para una bolsa: el puesto vende una y después queda cerrado.
        Assert.True((await api.BuyAsync(Key, new BuyItemRequest(me, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Req()))).Value!.Ok);
        Assert.True((await api.BusinessActionAsync(Key, "restock", new BusinessActionRequest(me, Req()))).Value!.Ok);

        var start = economy.CurrentCycle();
        Assert.Equal(1, await economy.RunBusinessCyclesAsync(start + 1, default, me));
        Assert.Equal(0, await economy.RunBusinessCyclesAsync(start + 2, default, me)); // cerrado: no vende, pero vino gente

        var ledger = (await api.GetMarketInfoAsync(Key, me)).Value!.Ledgers.Single();
        Assert.Equal(0, ledger.Stock);
        Assert.Equal(1, ledger.SoldToday);
        Assert.True(ledger.WantedToday >= 1 + EconomyCatalog.CoffeeStand.BaseDemand, $"querían {ledger.WantedToday}");
        Assert.True(ledger.LostToday >= EconomyCatalog.CoffeeStand.BaseDemand);
        Assert.Equal(EconomyCatalog.CoffeeSalePriceCents, ledger.RevenueToday);
        Assert.Equal(2, ledger.Recent.Count);
        Assert.Equal(0, ledger.Recent[0].Sold); // el último ciclo, cerrado
        Assert.True(ledger.Recent[0].Lost > 0);

        // El ciclo cerrado no movió plata: el negocio tiene lo de la venta menos el alquiler.
        var business = (await api.GetEconomyAsync(Key, me)).Value!.Business!;
        Assert.Equal(EconomyCatalog.CoffeeSalePriceCents, business.RevenueCents);
        Assert.Equal(business.RevenueCents - business.RentCents, ledger.CashCents);
    }
}
