using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Inventario, etapa 5: pedidos a proveedores con retiro o envío al depósito.</summary>
[Trait("Category", "Database")]
public class SupplierApiTests
{
    private const string Key = TestBackend.ServerKey;
    private const string Moretti = EconomyCatalog.DepositNpc;
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

    private static SupplierOrderRequest Order(Guid who, int qty, string delivery) =>
        new(who, Moretti, EconomyCatalog.CoffeeBagItem, qty, delivery, Req());

    [Fact]
    public async Task Pedir_cancelar_y_retirar_cuando_esta_listo()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await NewCharacter(api);
        var view = (await api.GetSupplierAsync(Key, me, Moretti)).Value!;
        Assert.Contains(view.Items, i => i.ItemCode == EconomyCatalog.CoffeeBagItem);
        Assert.False(view.HasWarehouse);
        var unit = view.Items.Single(i => i.ItemCode == EconomyCatalog.CoffeeBagItem).UnitPriceCents;

        Assert.Equal("invalid_quantity", (await api.PlaceSupplierOrderAsync(Key, Order(me, 4, SupplierRules.Pickup))).Value!.ErrorCode);
        Assert.Equal("no_warehouse", (await api.PlaceSupplierOrderAsync(Key, Order(me, 5, SupplierRules.Delivery))).Value!.ErrorCode);

        var placed = (await api.PlaceSupplierOrderAsync(Key, Order(me, 5, SupplierRules.Pickup))).Value!;
        Assert.True(placed.Ok, placed.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - 5 * unit, placed.Snapshot.BalanceCents);
        Assert.Equal(0, placed.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem)); // no aparece mágicamente

        var order = (await api.GetSupplierAsync(Key, me, Moretti)).Value!.Orders.Single();
        Assert.False(order.Ready);
        Assert.Equal("not_ready", (await api.PickupSupplierOrderAsync(Key, new SupplierOrderActionRequest(me, order.Id, Req()))).Value!.ErrorCode);
        var cancelled = (await api.CancelSupplierOrderAsync(Key, new SupplierOrderActionRequest(me, order.Id, Req()))).Value!;
        Assert.True(cancelled.Ok, cancelled.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents, cancelled.Snapshot.BalanceCents);

        Assert.True((await api.PlaceSupplierOrderAsync(Key, Order(me, 5, SupplierRules.Pickup))).Value!.Ok);
        var second = (await api.GetSupplierAsync(Key, me, Moretti)).Value!.Orders.First(o => o.Status == "preparando");

        // Una hora después ya está listo: se retira una sola vez y ya no se puede cancelar.
        await using var later = TestBackend.Create(clock: new TestBackend.FixedClock(DateTimeOffset.UtcNow.AddHours(1)));
        var laterApi = TestBackend.Api(later);
        Assert.True((await laterApi.GetSupplierAsync(Key, me, Moretti)).Value!.Orders.Single(o => o.Id == second.Id).Ready);
        Assert.Equal("already_ready", (await laterApi.CancelSupplierOrderAsync(Key, new SupplierOrderActionRequest(me, second.Id, Req()))).Value!.ErrorCode);
        var picked = (await laterApi.PickupSupplierOrderAsync(Key, new SupplierOrderActionRequest(me, second.Id, Req()))).Value!;
        Assert.True(picked.Ok, picked.Message);
        Assert.Equal(5, picked.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.Equal("closed", (await laterApi.PickupSupplierOrderAsync(Key, new SupplierOrderActionRequest(me, second.Id, Req()))).Value!.ErrorCode);
    }

    [Fact]
    public async Task El_envio_llega_al_deposito_de_la_cuadrilla()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Envíos " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        var member = await NewCharacter(api);
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, member));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(member, crewId, true));
        await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(leader, 2_000, Req()));
        Assert.True((await api.OpenWarehouseAsync(Key, new WarehouseOpenRequest(leader, Req()))).Value!.Ok);

        var unit = (await api.GetSupplierAsync(Key, member, Moretti)).Value!.Items.Single(i => i.ItemCode == EconomyCatalog.CoffeeBagItem).UnitPriceCents;
        var placed = (await api.PlaceSupplierOrderAsync(Key, Order(member, 5, SupplierRules.Delivery))).Value!;
        Assert.True(placed.Ok, placed.Message);
        var goods = 5 * unit;
        Assert.Equal(EconomyCatalog.StartingFundsCents - goods - SupplierRules.DeliveryCents(goods), placed.Snapshot.BalanceCents);

        var order = (await api.GetSupplierAsync(Key, member, Moretti)).Value!.Orders.Single();
        var economy = TestBackend.Economy();
        Assert.Equal(0, await economy.DeliverSupplierOrdersAsync(economy.CurrentCycle(), CancellationToken.None, order.Id)); // todavía no salió
        Assert.Equal(1, await economy.DeliverSupplierOrdersAsync(economy.CurrentCycle() + 100, CancellationToken.None, order.Id));

        var stored = (await api.GetWarehouseAsync(Key, leader)).Value!;
        Assert.Equal(5, stored.Items.Single().Quantity);
        Assert.Equal("entregado", (await api.GetSupplierAsync(Key, member, Moretti)).Value!.Orders.Single().Status);
        Assert.Equal(0, (await api.GetEconomyAsync(Key, member)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));
    }
}
