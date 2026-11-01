using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Inventario, etapa 6: comprar vehículos, cargar y descargar con capacidad, uso por cuadrilla y forzar cajas.</summary>
[Trait("Category", "Database")]
public class VehicleApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    /// <summary>Personaje de una cuenta admin (para darle plata y nivel en la prueba).</summary>
    private static async Task<Guid> Rich(DistritoApiClient api, string user, int level = 8, long cents = 100_000)
    {
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        Assert.True((await api.AdminGrantAsync(Key, new AdminGrantRequest(id, cents, null, 0, level, Req()))).Value!.Ok);
        return id;
    }

    [Fact]
    public async Task Comprar_cargar_descargar_y_forzar()
    {
        var (a, b, c) = (TestBackend.UniqueUser("va"), TestBackend.UniqueUser("vb"), TestBackend.UniqueUser("vc"));
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = $"{a},{b},{c}" });
        var api = TestBackend.Api(factory);
        var owner = await Rich(api, a);
        var friend = await Rich(api, b);
        var thief = await Rich(api, c);
        var buy = (await api.BuyVehicleAsync(Key, new VehicleBuyRequest(owner, Vehicles.Pickup.Code, false, 14, 0.2, 0, 0, Req()))).Value!;
        Assert.True(buy.Result.Ok, buy.Result.Message);
        Assert.NotNull(buy.Vehicle);
        Assert.Equal(EconomyCatalog.StartingFundsCents + 100_000 - Vehicles.Pickup.PriceCents, buy.Result.Snapshot.BalanceCents);
        var id = buy.Vehicle!.Id;
        Assert.Contains((await api.GetVehiclesAsync(Key)).Value!, v => v.Id == id);

        // Estaciona y se guarda dónde quedó.
        await api.ParkVehicleAsync(Key, new VehicleParkRequest(id, 50, 0.2, 3, 1.5));
        var parked = (await api.GetVehiclesAsync(Key)).Value!.Single(v => v.Id == id);
        Assert.Equal((50.0, 3.0), (parked.X, parked.Z));

        // Cargar: hasta 400 kg (cajas de repuestos de 12 kg).
        Assert.True((await api.AdminGrantAsync(Key, new AdminGrantRequest(owner, 0, EconomyCatalog.SparePartsItem, 40, null, Req()))).Value!.Ok);
        Assert.Equal("vehicle_full", (await api.MoveVehicleCargoAsync(Key, new VehicleCargoRequest(owner, id, EconomyCatalog.SparePartsItem, 34, true, Req()))).Value!.ErrorCode);
        var load = (await api.MoveVehicleCargoAsync(Key, new VehicleCargoRequest(owner, id, EconomyCatalog.SparePartsItem, 30, true, Req()))).Value!;
        Assert.True(load.Ok, load.Message);
        Assert.Equal(10, load.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));
        var cargo = (await api.GetVehicleCargoAsync(Key, id, owner)).Value!;
        Assert.Equal(360, cargo.WeightKg, 3);

        // Uno de otra cuadrilla no la usa ni ve la carga; el dueño no la "fuerza".
        Assert.False((await api.GetVehicleCargoAsync(Key, id, thief)).Value!.CanUse);
        Assert.Empty((await api.GetVehicleCargoAsync(Key, id, thief)).Value!.Items);
        Assert.Equal("forbidden", (await api.MoveVehicleCargoAsync(Key, new VehicleCargoRequest(thief, id, EconomyCatalog.SparePartsItem, 1, false, Req()))).Value!.ErrorCode);
        Assert.Equal("own_vehicle", (await api.ForceVehicleAsync(Key, new VehicleForceRequest(owner, id, Guid.NewGuid(), 0, 0, 0, 60, Req()))).Value!.Result.ErrorCode);

        // Alguien de su cuadrilla sí la usa.
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(owner, "Ruta " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(owner, friend));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(friend, crewId, true));
        Assert.True((await api.GetVehicleCargoAsync(Key, id, friend)).Value!.CanUse);
        var unload = (await api.MoveVehicleCargoAsync(Key, new VehicleCargoRequest(friend, id, EconomyCatalog.SparePartsItem, 5, false, Req()))).Value!;
        Assert.True(unload.Ok, unload.Message);
        Assert.Equal(5, unload.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));

        // Forzar: toda la carga cae a la calle en un bulto que otro levanta.
        var dropId = Guid.NewGuid();
        var forced = (await api.ForceVehicleAsync(Key, new VehicleForceRequest(thief, id, dropId, 52, 0, 3, 120, Req()))).Value!;
        Assert.True(forced.Result.Ok, forced.Result.Message);
        Assert.Equal(25, forced.Drop!.Items.Single().Quantity);
        Assert.Empty((await api.GetVehicleCargoAsync(Key, id, owner)).Value!.Items);
        var picked = (await api.PickupAsync(Key, new PickupRequest(thief, dropId, Req()))).Value!;
        Assert.Equal(25, picked.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));
        Assert.Equal("empty", (await api.ForceVehicleAsync(Key, new VehicleForceRequest(thief, id, Guid.NewGuid(), 0, 0, 0, 60, Req()))).Value!.Result.ErrorCode);
    }

    [Fact]
    public async Task Nivel_caja_de_cuadrilla_y_disolucion()
    {
        var (a, b) = (TestBackend.UniqueUser("vf"), TestBackend.UniqueUser("vg"));
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = $"{a},{b}" });
        var api = TestBackend.Api(factory);
        var leader = await Rich(api, a, level: 5);
        var low = await Rich(api, b, level: 2);

        Assert.Equal("level_too_low", (await api.BuyVehicleAsync(Key, new VehicleBuyRequest(low, Vehicles.Pickup.Code, false, 0, 0, 0, 0, Req()))).Value!.Result.ErrorCode);
        Assert.Equal("level_too_low", (await api.BuyVehicleAsync(Key, new VehicleBuyRequest(leader, Vehicles.Truck.Code, false, 0, 0, 0, 0, Req()))).Value!.Result.ErrorCode);
        Assert.Equal("not_in_crew", (await api.BuyVehicleAsync(Key, new VehicleBuyRequest(leader, Vehicles.Pickup.Code, true, 0, 0, 0, 0, Req()))).Value!.Result.ErrorCode);

        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Flota " + Guid.NewGuid().ToString("N")[..10]));
        Assert.Equal("insufficient_funds", (await api.BuyVehicleAsync(Key, new VehicleBuyRequest(leader, Vehicles.Pickup.Code, true, 0, 0, 0, 0, Req()))).Value!.Result.ErrorCode);
        await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(leader, Vehicles.Pickup.PriceCents, Req()));
        var bought = (await api.BuyVehicleAsync(Key, new VehicleBuyRequest(leader, Vehicles.Pickup.Code, true, 20, 0.2, 0, 0, Req()))).Value!;
        Assert.True(bought.Result.Ok, bought.Result.Message);
        Assert.NotNull(bought.Vehicle!.OwnerCrewId);
        var id = bought.Vehicle.Id;
        Assert.True((await api.AdminGrantAsync(Key, new AdminGrantRequest(leader, 0, EconomyCatalog.CoffeeBagItem, 4, null, Req()))).Value!.Ok);
        Assert.True((await api.MoveVehicleCargoAsync(Key, new VehicleCargoRequest(leader, id, EconomyCatalog.CoffeeBagItem, 4, true, Req()))).Value!.Ok);

        // Se disuelve: la camioneta y su carga pasan al último que se va.
        Assert.True((await api.LeaveCrewAsync(Key, new LeaveCrewRequest(leader))).Value!.Ok);
        var after = (await api.GetVehiclesAsync(Key)).Value!.Single(v => v.Id == id);
        Assert.Equal(leader, after.OwnerCharacterId);
        Assert.Equal(4, (await api.GetVehicleCargoAsync(Key, id, leader)).Value!.Items.Single().Quantity);
    }
}
