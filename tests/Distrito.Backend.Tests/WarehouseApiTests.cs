using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Inventario, etapa 3: depósito de la cuadrilla con capacidad, permisos, historial y alquiler de la caja.</summary>
[Trait("Category", "Database")]
public class WarehouseApiTests
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

    private static async Task<(Guid Leader, Guid Second, Guid Member)> NewCrew(DistritoApiClient api)
    {
        var leader = await NewCharacter(api);
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Banda " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        var second = await NewCharacter(api);
        var member = await NewCharacter(api);
        foreach (var who in new[] { second, member })
        {
            await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, who));
            Assert.True((await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(who, crewId, true))).Value!.Ok);
        }
        Assert.True((await api.SetCrewRoleAsync(Key, new SetCrewRoleRequest(leader, second, CrewRoles.Second))).Value!.Ok);
        return (leader, second, member);
    }

    private static Task<ApiResult<EconomyActionResult>> Move(DistritoApiClient api, Guid who, int qty, bool deposit) =>
        api.MoveWarehouseAsync(Key, new WarehouseMoveRequest(who, EconomyCatalog.CoffeeBagItem, qty, deposit, Req()));

    [Fact]
    public async Task Abrir_guardar_retirar_con_permisos_e_historial()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var (leader, second, member) = await NewCrew(api);
        Assert.Null((await api.GetWarehouseAsync(Key, await NewCharacter(api))).Value); // sin cuadrilla no hay depósito

        var before = (await api.GetWarehouseAsync(Key, member)).Value!;
        Assert.False(before.Exists);
        Assert.Equal(2_000, before.OpenCostCents);
        Assert.Equal(300, before.CapacityKg);

        // Solo el jefe lo abre, y la caja tiene que llegar.
        Assert.Equal("not_leader", (await api.OpenWarehouseAsync(Key, new WarehouseOpenRequest(member, Req()))).Value!.ErrorCode);
        Assert.Equal("insufficient_funds", (await api.OpenWarehouseAsync(Key, new WarehouseOpenRequest(leader, Req()))).Value!.ErrorCode);
        await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(leader, 2_000, Req()));
        var open = (await api.OpenWarehouseAsync(Key, new WarehouseOpenRequest(leader, Req()))).Value!;
        Assert.True(open.Ok, open.Message);
        Assert.Equal("already_open", (await api.OpenWarehouseAsync(Key, new WarehouseOpenRequest(leader, Req()))).Value!.ErrorCode);

        await api.BuyAsync(Key, new BuyItemRequest(member, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 3, Req()));
        var deposit = new WarehouseMoveRequest(member, EconomyCatalog.CoffeeBagItem, 3, true, Req());
        Assert.True((await api.MoveWarehouseAsync(Key, deposit)).Value!.Ok);
        Assert.True((await api.MoveWarehouseAsync(Key, deposit)).Value!.Ok); // reintento: no guarda dos veces
        Assert.Equal(0, (await api.GetEconomyAsync(Key, member)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));
        var stored = (await api.GetWarehouseAsync(Key, leader)).Value!;
        Assert.Equal(3, stored.Items.Single().Quantity);
        Assert.Equal(15, stored.WeightKg, 3);

        // El segundo retira; el historial dice quién hizo qué.
        Assert.True((await Move(api, second, 1, deposit: false)).Value!.Ok);
        var history = (await api.GetWarehouseAsync(Key, leader)).Value!.History;
        Assert.Equal(2, history.Count);
        Assert.False(history[0].Deposit);
        Assert.True(history[1].Deposit);
        Assert.Equal(3, history[1].Quantity);

        // El jefe les saca a los miembros el permiso de retirar y de ver.
        Assert.Equal("not_leader", (await api.SetWarehousePermissionsAsync(Key,
            new WarehousePermissionsRequest(second, new WarehousePermissions(true, true, false, true), Req()))).Value!.ErrorCode);
        Assert.True((await api.SetWarehousePermissionsAsync(Key,
            new WarehousePermissionsRequest(leader, new WarehousePermissions(false, true, false, true), Req()))).Value!.Ok);
        Assert.Equal("forbidden", (await Move(api, member, 1, deposit: false)).Value!.ErrorCode);
        var hidden = (await api.GetWarehouseAsync(Key, member)).Value!;
        Assert.False(hidden.CanView);
        Assert.Empty(hidden.Items);

        // Lo guardado aparece en las existencias del mundo (para dibujarlo en el galpón).
        var world = (await api.GetWorldStockAsync(Key)).Value!;
        Assert.Contains(world.Suppliers, sp => sp.SupplierCode == EconomyCatalog.DepositNpc && sp.Items.Count > 0);
        if (world.Warehouses.FirstOrDefault(x => x.Id == stored.Id) is { } shown) // solo las primeras bahías se dibujan
            Assert.Equal(2, shown.Items.Single().Quantity);

        // No se retira más de lo que hay.
        Assert.Equal("not_enough_items", (await Move(api, leader, 9, deposit: false)).Value!.ErrorCode);
    }

    [Fact]
    public async Task Alquiler_con_deuda_y_disolucion()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var (leader, second, member) = await NewCrew(api);
        await api.BuyAsync(Key, new BuyItemRequest(leader, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 2, Req()));
        await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(second, 2_000, Req()));
        Assert.True((await api.OpenWarehouseAsync(Key, new WarehouseOpenRequest(leader, Req()))).Value!.Ok); // la caja queda en cero

        var economy = TestBackend.Economy();
        Assert.Equal(300, economy.CrewWarehouseRules().CapacityKg);
        var cycle = economy.CurrentCycle();
        var id = (await api.GetWarehouseAsync(Key, leader)).Value!.Id!.Value;

        // Vacío no paga alquiler.
        await economy.RunWarehouseRentAsync(cycle + 1, CancellationToken.None, id);
        Assert.Equal(0, (await api.GetWarehouseAsync(Key, leader)).Value!.DebtCents);

        // Con algo guardado y la caja vacía, el alquiler queda como deuda y no se puede retirar (guardar sí).
        Assert.True((await Move(api, leader, 1, deposit: true)).Value!.Ok);
        await economy.RunWarehouseRentAsync(cycle + 4, CancellationToken.None, id);
        Assert.Equal(300, (await api.GetWarehouseAsync(Key, leader)).Value!.DebtCents);
        Assert.Equal("rent_due", (await Move(api, leader, 1, deposit: false)).Value!.ErrorCode);
        Assert.True((await Move(api, leader, 1, deposit: true)).Value!.Ok);

        // Ponen plata en la caja: el próximo ciclo cobra todo y se puede volver a retirar.
        await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(member, 1_000, Req()));
        await economy.RunWarehouseRentAsync(cycle + 5, CancellationToken.None, id);
        Assert.Equal(0, (await api.GetWarehouseAsync(Key, leader)).Value!.DebtCents);
        Assert.True((await Move(api, leader, 1, deposit: false)).Value!.Ok);

        // Si la cuadrilla se disuelve, lo guardado pasa al último que se va (no se pierde).
        foreach (var who in new[] { leader, second, member })
            Assert.True((await api.LeaveCrewAsync(Key, new LeaveCrewRequest(who))).Value!.Ok);
        Assert.Equal(1, (await api.GetEconomyAsync(Key, member)).Value!.QuantityOf(EconomyCatalog.CoffeeBagItem));
    }
}
