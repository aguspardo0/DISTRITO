using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Sede de cuadrilla, tablero de misiones generadas y prestigio.</summary>
[Trait("Category", "Database")]
public class CrewBaseApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> Rich(DistritoApiClient api, string user, long cents = 50_000)
    {
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        Assert.True((await api.AdminGrantAsync(Key, new AdminGrantRequest(id, cents, null, 0, 3, Req()))).Value!.Ok);
        return id;
    }

    /// <summary>Galpón libre (otras pruebas pueden haber comprado alguno).</summary>
    private static async Task<int?> FreeShed(DistritoApiClient api, Guid someone) =>
        Enumerable.Range(0, CrewBaseRules.Sheds.Count).Cast<int?>()
            .FirstOrDefault(s => !(api.GetCrewBoardAsync(Key, someone).Result.Value!.TakenSheds.Contains(s!.Value)));

    [Fact]
    public async Task Sede_tablero_entrega_y_prestigio()
    {
        var (a, b) = (TestBackend.UniqueUser("sa"), TestBackend.UniqueUser("sb"));
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = $"{a},{b}" });
        var api = TestBackend.Api(factory);
        var leader = await Rich(api, a);
        var member = await Rich(api, b);
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Sede " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, member));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(member, crewId, true));

        // Tablero: 3 misiones del turno, iguales para los dos.
        var board = (await api.GetCrewBoardAsync(Key, leader)).Value!;
        Assert.True(board.InCrew);
        Assert.Equal(3, board.Missions.Count);
        Assert.Equal(board.Missions.Select(m => m.Id), (await api.GetCrewBoardAsync(Key, member)).Value!.Missions.Select(m => m.Id));

        // Sede: solo el jefe, con plata de la caja.
        var shed = await FreeShed(api, leader);
        if (shed is { } s)
        {
            Assert.Equal("not_leader", (await api.BuyCrewBaseAsync(Key, new CrewBaseBuyRequest(member, s, Req()))).Value!.ErrorCode);
            Assert.Equal("insufficient_funds", (await api.BuyCrewBaseAsync(Key, new CrewBaseBuyRequest(leader, s, Req()))).Value!.ErrorCode);
            await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(leader, CrewBaseRules.PriceCents, Req()));
            var bought = (await api.BuyCrewBaseAsync(Key, new CrewBaseBuyRequest(leader, s, Req()))).Value!;
            Assert.True(bought.Ok, bought.Message);
            Assert.Equal(s, (await api.GetCrewBoardAsync(Key, leader)).Value!.Shed);
            Assert.Contains((await api.GetCrewBasesAsync(Key)).Value!, x => x.Shed == s);
            var depot = (await api.GetWarehouseAsync(Key, leader)).Value!;
            Assert.True(depot.Exists);
            Assert.Equal(CrewBaseRules.DepotKg, depot.CapacityKg);
        }

        // Una misión de entrega o de evento se cumple y suma prestigio.
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        var mission = board.Missions.First();
        await using (var force = db.CreateCommand("UPDATE distrito.crew_missions SET kind = 'camion', target = 1, npc_code = NULL WHERE id = @id"))
        {
            force.Parameters.AddWithValue("id", mission.Id);
            await force.ExecuteNonQueryAsync();
        }
        var done = (await api.CrewMissionProgressAsync(Key, new CrewMissionProgressRequest(member, MissionKinds.TruckJob, 1, Req()))).Value!;
        Assert.True(done.Ok, done.Message);
        Assert.Contains("cumplida", done.Message);
        var after = (await api.GetCrewBoardAsync(Key, leader)).Value!;
        Assert.Equal(mission.Prestige, after.Prestige);
        Assert.True(after.Missions.Single(m => m.Id == mission.Id).Done);
    }

    [Fact]
    public async Task Entrega_en_el_lugar_y_descuento_por_rango()
    {
        var a = TestBackend.UniqueUser("se");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = a });
        var api = TestBackend.Api(factory);
        var leader = await Rich(api, a);
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Ruta " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        var board = (await api.GetCrewBoardAsync(Key, leader)).Value!;
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        var mission = board.Missions.First();
        await using (var force = db.CreateCommand(
            "UPDATE distrito.crew_missions SET kind = 'entrega', item_code = 'bolsa-cafe', npc_code = 'npc:taller-caruso', target = 3 WHERE id = @id"))
        {
            force.Parameters.AddWithValue("id", mission.Id);
            await force.ExecuteNonQueryAsync();
        }
        Assert.Equal("not_enough_items", (await api.TurnInCrewMissionAsync(Key, new CrewMissionTurnInRequest(leader, mission.Id, Req()))).Value!.ErrorCode);
        await api.AdminGrantAsync(Key, new AdminGrantRequest(leader, 0, "bolsa-cafe", 3, null, Req()));
        var turned = (await api.TurnInCrewMissionAsync(Key, new CrewMissionTurnInRequest(leader, mission.Id, Req()))).Value!;
        Assert.True(turned.Ok, turned.Message);
        Assert.Equal(0, turned.Snapshot.QuantityOf("bolsa-cafe"));

        // Rango 3 de prestigio: 5 % menos en Moretti.
        await using (var fame = db.CreateCommand("UPDATE distrito.crews SET prestige = 300 WHERE id = @id"))
        {
            fame.Parameters.AddWithValue("id", crewId);
            await fame.ExecuteNonQueryAsync();
        }
        var before = (await api.GetEconomyAsync(Key, leader)).Value!.BalanceCents;
        var buy = (await api.BuyAsync(Key, new BuyItemRequest(leader, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 10, Req()))).Value!;
        Assert.True(buy.Ok, buy.Message);
        Assert.True(before - buy.Snapshot.BalanceCents <= 10 * 800 * 110 / 100 * 95 / 100); // con o sin rivalidad, 5 % menos
    }
}
