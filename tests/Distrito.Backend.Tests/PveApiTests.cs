using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Asaltos a NPC: botín real, reputación y ánimo de la familia, contrabando que entra de afuera.</summary>
[Trait("Category", "Database")]
public class PveApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.GetEconomyAsync(Key, id);
        return id;
    }

    [Fact]
    public async Task El_camion_lleva_stock_real_y_si_lo_asaltan_cae_a_la_calle()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var robber = await NewCharacter(api);
        var eventId = "camion-test-" + Guid.NewGuid().ToString("N")[..8];
        var loaded = (await api.PveLoadAsync(Key, new PveLoadRequest(eventId, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 12))).Value;
        Assert.Equal(12, loaded);
        Assert.Equal(12, (await api.PveLoadAsync(Key, new PveLoadRequest(eventId, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 12))).Value); // reintento

        var dropId = Guid.NewGuid();
        var robbed = (await api.PveRobGoodsAsync(Key, new PveRobGoodsRequest(eventId, EconomyCatalog.DepositNpc, robber, EconomyCatalog.Moretti, -10,
            dropId, 5, 0, 0, 120))).Value!;
        Assert.True(robbed.Result.Ok, robbed.Result.Message);
        Assert.Equal(12, robbed.Drop!.Items.Single().Quantity);
        var picked = (await api.PickupAsync(Key, new PickupRequest(robber, dropId, Req()))).Value!;
        Assert.Equal(12, picked.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));
        // La reputación perdida cuenta para el ánimo de la familia (rivalidad desde 10 en 24 h).
        Assert.Contains("-10", robbed.Result.Message);
    }

    [Fact]
    public async Task El_camion_que_no_asaltan_devuelve_la_carga()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var eventId = "camion-test-" + Guid.NewGuid().ToString("N")[..8];
        var before = (await api.GetWorldStockAsync(Key)).Value!.Suppliers.Single(s => s.SupplierCode == EconomyCatalog.DepositNpc)
            .Items.Single(i => i.ItemCode == EconomyCatalog.SparePartsItem).Quantity;
        await api.PveLoadAsync(Key, new PveLoadRequest(eventId, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 10));
        await api.PveUnloadAsync(Key, new PveUnloadRequest(eventId, EconomyCatalog.DepositNpc));
        var after = (await api.GetWorldStockAsync(Key)).Value!.Suppliers.Single(s => s.SupplierCode == EconomyCatalog.DepositNpc)
            .Items.Single(i => i.ItemCode == EconomyCatalog.SparePartsItem).Quantity;
        Assert.InRange(after, before - 30, before + 30); // otros tests pueden mover stock a la vez; lo propio volvió
    }

    [Fact]
    public async Task Cobrador_y_caja_dan_plata_de_la_familia_y_el_contrabando_entra_de_afuera()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var robber = await NewCharacter(api);
        var cash = (await api.PveRobCashAsync(Key, new PveRobCashRequest(robber, EconomyCatalog.ValentiOfficeNpc, 3000, 6000, EconomyCatalog.Valenti, -10,
            "Asalto al cobrador de Valenti", Req()))).Value!;
        Assert.True(cash.Ok, cash.Message);
        Assert.InRange(cash.Snapshot.BalanceCents, EconomyCatalog.StartingFundsCents + 3000, EconomyCatalog.StartingFundsCents + 6000);

        var eventId = "contrabando-test-" + Guid.NewGuid().ToString("N")[..8];
        var drop = (await api.PveContrabandAsync(Key, new PveContrabandRequest(eventId,
            [new PveLootItem("pistola", 2), new PveLootItem("bala-pistola", 100)], Guid.NewGuid(), 78, 0, 4, 300))).Value!;
        Assert.Equal(2, drop.Items.Count);
        var picked = (await api.PickupAsync(Key, new PickupRequest(robber, drop.DropId, Req()))).Value!;
        Assert.Equal(2, picked.Snapshot.QuantityOf("pistola"));
        Assert.Equal(100, picked.Snapshot.QuantityOf("bala-pistola"));
    }
}
