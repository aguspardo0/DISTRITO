using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Inventario, etapas 1–2: ver lo que se lleva, darlo en mano y soltarlo en la calle, sin duplicar nada.</summary>
[Trait("Category", "Database")]
public class InventoryApiTests
{
    private const string Key = TestBackend.ServerKey;

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.GetEconomyAsync(Key, id);
        return id;
    }

    private static Task<ApiResult<EconomyActionResult>> BuyCoffee(DistritoApiClient api, Guid who, int n) =>
        api.BuyAsync(Key, new BuyItemRequest(who, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, n, Guid.NewGuid().ToString()));

    [Fact]
    public async Task El_inventario_muestra_clase_descripcion_peso_y_sin_limite()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await NewCharacter(api);
        await BuyCoffee(api, me, 3);

        var view = (await api.GetInventoryAsync(Key, me)).Value!;
        var coffee = view.Items.Single();
        Assert.Equal(ItemKinds.RawMaterial, coffee.Kind);
        Assert.NotEmpty(coffee.Description);
        Assert.Equal(3, coffee.Quantity);
        Assert.Equal(15, view.WeightKg, 3); // 3 × 5 kg
        Assert.Null(view.CapacityKg);       // decisión del equipo: por ahora sin límite
        var bought = Assert.Single(view.History!);
        Assert.True(bought.In);
        Assert.Equal(3, bought.Quantity);
        Assert.Contains("Compra", bought.Reason);
    }

    [Fact]
    public async Task Dar_en_mano_mueve_lo_justo_y_no_se_repite()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var giver = await NewCharacter(api);
        var friend = await NewCharacter(api);
        await BuyCoffee(api, giver, 3);

        var request = new GiveItemRequest(giver, friend, EconomyCatalog.CoffeeBagItem, 2, Guid.NewGuid().ToString());
        var first = (await api.GiveItemAsync(Key, request)).Value!;
        Assert.True(first.Ok, first.Message);
        Assert.Equal(1, first.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.True((await api.GiveItemAsync(Key, request)).Value!.Ok); // reintento con el mismo id: no da dos veces
        Assert.Equal(1, (await api.GetEconomyAsync(Key, giver)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.Equal(2, (await api.GetEconomyAsync(Key, friend)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));

        // No se puede dar lo que no se tiene, ni a uno mismo, ni cantidades raras.
        Assert.Equal("not_enough_items", (await api.GiveItemAsync(Key, new GiveItemRequest(giver, friend, EconomyCatalog.CoffeeBagItem, 5, Guid.NewGuid().ToString()))).Value!.ErrorCode);
        Assert.Equal("same_character", (await api.GiveItemAsync(Key, new GiveItemRequest(giver, giver, EconomyCatalog.CoffeeBagItem, 1, Guid.NewGuid().ToString()))).Value!.ErrorCode);
        Assert.Equal("invalid_quantity", (await api.GiveItemAsync(Key, new GiveItemRequest(giver, friend, EconomyCatalog.CoffeeBagItem, -1, Guid.NewGuid().ToString()))).Value!.ErrorCode);
        Assert.Equal(1, (await api.GetEconomyAsync(Key, giver)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));
    }

    [Fact]
    public async Task Soltar_deja_un_bulto_en_la_calle_que_otro_levanta()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await NewCharacter(api);
        var other = await NewCharacter(api);
        await BuyCoffee(api, me, 2);

        var dropId = Guid.NewGuid();
        var request = new DropItemRequest(me, EconomyCatalog.CoffeeBagItem, 1, dropId, Guid.NewGuid().ToString(), 2, 0, 3, 120);
        var dropped = (await api.DropItemAsync(Key, request)).Value!;
        Assert.True(dropped.Result.Ok, dropped.Result.Message);
        Assert.Equal(1, dropped.Result.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.Equal(1, dropped.Drop!.Items.Single().Quantity);
        Assert.Null((await api.DropItemAsync(Key, request)).Value!.Drop); // reintento: no tira otro bulto
        Assert.Equal(1, (await api.GetEconomyAsync(Key, me)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));

        var listed = (await api.GetDropsAsync(Key)).Value!.Single(d => d.DropId == dropId);
        Assert.Equal((2.0, 3.0), (listed.X, listed.Z));
        var picked = (await api.PickupAsync(Key, new PickupRequest(other, dropId, Guid.NewGuid().ToString()))).Value!;
        Assert.True(picked.Ok, picked.Message);
        Assert.Contains("Bolsa de café", picked.Message);
        Assert.Equal(1, picked.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));

        Assert.Equal("not_enough_items", (await api.DropItemAsync(Key,
            new DropItemRequest(me, EconomyCatalog.CoffeeBagItem, 9, Guid.NewGuid(), Guid.NewGuid().ToString(), 0, 0, 0))).Value!.Result.ErrorCode);
    }
}

[Trait("Category", "Database")]
public class NpcSaleApiTests
{
    private const string Key = TestBackend.ServerKey;

    [Fact]
    public async Task Se_vende_al_60_por_ciento_con_tope_por_hora()
    {
        var user = TestBackend.UniqueUser("vn");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = user });
        var api = TestBackend.Api(factory);
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var me = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.AdminGrantAsync(Key, new AdminGrantRequest(me, 0, EconomyCatalog.SparePartsItem, 25, null, Guid.NewGuid().ToString()));
        var before = (await api.GetEconomyAsync(Key, me)).Value!.BalanceCents;

        Assert.Equal("not_bought", (await api.SellAsync(Key, new SellItemRequest(me, EconomyCatalog.WorkshopNpc, EconomyCatalog.SparePartsItem, 1, Guid.NewGuid().ToString()))).Value!.ErrorCode);
        var sold = (await api.SellAsync(Key, new SellItemRequest(me, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 25, Guid.NewGuid().ToString()))).Value!;
        Assert.True(sold.Ok, sold.Message);
        Assert.Equal(5, sold.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));         // tope de 20 por hora
        Assert.Equal(before + 20 * 800 * 60 / 100, sold.Snapshot.BalanceCents);            // 60 % de $8
        Assert.Equal("sale_limit", (await api.SellAsync(Key, new SellItemRequest(me, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 1, Guid.NewGuid().ToString()))).Value!.ErrorCode);
    }
}
