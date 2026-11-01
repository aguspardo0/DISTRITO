using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>PvP: la mercadería del herido queda en el suelo y la levanta otro; la plata nunca se pierde.</summary>
[Trait("Category", "Database")]
public class CombatApiTests
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

    [Fact]
    public async Task Al_quedar_herido_la_mercaderia_queda_tirada_y_otro_la_levanta()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var victim = await NewCharacter(api);
        var thief = await NewCharacter(api);
        for (var i = 0; i < 2; i++)
            await api.BuyAsync(Key, new BuyItemRequest(victim, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Guid.NewGuid().ToString()));
        var balance = (await api.GetEconomyAsync(Key, victim)).Value!.BalanceCents;

        var dropId = Guid.NewGuid();
        var ko = new KnockoutRequest(victim, dropId, Guid.NewGuid().ToString());
        var drop = (await api.KnockoutAsync(Key, ko)).Value!;
        Assert.Equal(2, drop.Items.Single().Quantity);
        Assert.Equal(2, (await api.KnockoutAsync(Key, ko)).Value!.Items.Single().Quantity); // reintento: lo mismo
        var after = (await api.GetEconomyAsync(Key, victim)).Value!;
        Assert.Equal(0, after.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.Equal(balance, after.BalanceCents); // la plata no se pierde

        var picked = (await api.PickupAsync(Key, new PickupRequest(thief, dropId, Guid.NewGuid().ToString()))).Value!;
        Assert.True(picked.Ok, picked.Message);
        Assert.Equal(2, picked.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.Equal("drop_empty", (await api.PickupAsync(Key, new PickupRequest(victim, dropId, Guid.NewGuid().ToString()))).Value!.ErrorCode);

        // Sin mercadería encima no queda nada tirado.
        Assert.Empty((await api.KnockoutAsync(Key, new KnockoutRequest(victim, Guid.NewGuid(), Guid.NewGuid().ToString()))).Value!.Items);
    }

    [Fact]
    public async Task El_bulto_sobrevive_al_reinicio_y_vence_segun_la_zona()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var victim = await NewCharacter(api);
        var thief = await NewCharacter(api);
        await api.BuyAsync(Key, new BuyItemRequest(victim, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Guid.NewGuid().ToString()));

        // Zona donde no se pierde mercadería: no queda nada tirado y el café sigue encima.
        Assert.Empty((await api.KnockoutAsync(Key, new KnockoutRequest(victim, Guid.NewGuid(), Guid.NewGuid().ToString(), IncludeGoods: false))).Value!.Items);
        Assert.Equal(1, (await api.GetEconomyAsync(Key, victim)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));

        var dropId = Guid.NewGuid();
        await api.KnockoutAsync(Key, new KnockoutRequest(victim, dropId, Guid.NewGuid().ToString(), true, 4, 0, -7, Seconds: 60));
        var listed = (await api.GetDropsAsync(Key)).Value!.Single(d => d.DropId == dropId);
        Assert.Equal((4.0, -7.0), (listed.X, listed.Z));
        Assert.Equal(1, listed.Items.Single().Quantity);

        // Una hora después ya venció: no aparece y nadie lo levanta.
        await using var later = TestBackend.Create(clock: new TestBackend.FixedClock(DateTimeOffset.UtcNow.AddHours(1)));
        var laterApi = TestBackend.Api(later);
        Assert.DoesNotContain((await laterApi.GetDropsAsync(Key)).Value!, d => d.DropId == dropId);
        Assert.Equal("drop_expired", (await laterApi.PickupAsync(Key, new PickupRequest(thief, dropId, Guid.NewGuid().ToString()))).Value!.ErrorCode);

        // Mientras no venza, se levanta una sola vez y deja de listarse.
        Assert.True((await api.PickupAsync(Key, new PickupRequest(thief, dropId, Guid.NewGuid().ToString()))).Value!.Ok);
        Assert.DoesNotContain((await api.GetDropsAsync(Key)).Value!, d => d.DropId == dropId);
    }

    [Fact]
    public async Task El_estado_de_combate_lista_zonas_en_disputa()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var state = await api.GetCombatStateAsync(Key);
        Assert.True(state.IsSuccess);
        Assert.NotNull(state.Value!.DisputedZones);
    }
}
