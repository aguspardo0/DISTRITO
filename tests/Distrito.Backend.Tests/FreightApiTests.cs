using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Inventario, etapa 8: fletes en consignación y pedidos con envío directo al negocio.</summary>
[Trait("Category", "Database")]
public class FreightApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> Rich(DistritoApiClient api, string user, int level = 3, long cents = 0)
    {
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        Assert.True((await api.AdminGrantAsync(Key, new AdminGrantRequest(id, cents, null, 0, level, Req()))).Value!.Ok);
        return id;
    }

    [Fact]
    public async Task El_flete_entrega_la_mercaderia_y_paga_solo_el_transporte()
    {
        var user = TestBackend.UniqueUser("fl");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = user });
        var api = TestBackend.Api(factory);
        var me = await Rich(api, user);
        var accepted = (await api.AcceptContractAsync(Key, new AcceptContractRequest(me, EconomyCatalog.PartsFreight.Code, Req()))).Value!;
        Assert.True(accepted.Ok, accepted.Message);
        Assert.Equal(20, accepted.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem)); // se la cargan al aceptar
        Assert.Equal(EconomyCatalog.StartingFundsCents, accepted.Snapshot.BalanceCents); // no la paga

        var contract = accepted.Snapshot.ActiveContract(EconomyCatalog.PartsFreight.Code)!;
        var delivered = (await api.DeliverAsync(Key, new DeliverContractRequest(me, contract.Id, Req(), []))).Value!;
        Assert.True(delivered.Ok, delivered.Message);
        Assert.Equal(0, delivered.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));
        Assert.True(delivered.Snapshot.BalanceCents >= EconomyCatalog.StartingFundsCents + 6_000 * 9 / 10); // ~$60 (según ánimo)
    }

    [Fact]
    public async Task Flete_vencido_devuelve_y_cobra_lo_que_falta()
    {
        var user = TestBackend.UniqueUser("fv");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = user });
        var api = TestBackend.Api(factory);
        var me = await Rich(api, user, cents: 10_000);
        Assert.True((await api.AcceptContractAsync(Key, new AcceptContractRequest(me, EconomyCatalog.DrinksFreight.Code, Req()))).Value!.Ok);
        // Se queda con 2 cajones (los suelta en la calle) y deja vencer el flete.
        await api.DropItemAsync(Key, new DropItemRequest(me, EconomyCatalog.DrinksCrateItem, 2, Guid.NewGuid(), Req(), 0, 0, 0));
        await using var later = TestBackend.Create(clock: new TestBackend.FixedClock(DateTimeOffset.UtcNow.AddHours(1)),
            settings: new Dictionary<string, string> { ["Admin:Accounts"] = user });
        var after = (await TestBackend.Api(later).GetEconomyAsync(Key, me)).Value!;
        Assert.Equal(0, after.QuantityOf(EconomyCatalog.DrinksCrateItem));        // los 13 que tenía volvieron
        Assert.True(after.BalanceCents <= EconomyCatalog.StartingFundsCents + 10_000 - 2 * 400); // los 2 que faltan se cobran
    }
}
