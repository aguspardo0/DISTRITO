using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Operación del día de la cuadrilla: empezar, etapas en orden y paga al final.</summary>
[Trait("Category", "Database")]
public class CrewOperationApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> Character(DistritoApiClient api, string user)
    {
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    [Fact]
    public async Task Operacion_del_dia_en_tres_etapas()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await Character(api, TestBackend.UniqueUser("oa"));
        var member = await Character(api, TestBackend.UniqueUser("ob"));
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Op " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;

        // Sola, la cuadrilla no puede: hacen falta dos.
        var board = (await api.GetCrewBoardAsync(Key, leader)).Value!;
        Assert.NotNull(board.Operation);
        Assert.Equal(CrewOperations.Available, board.Operation!.Status);
        Assert.InRange(board.Operation.RewardCents, 12_000, 25_000);
        Assert.InRange(board.Operation.Prestige, 60, 120);
        Assert.Equal("too_few", (await api.StartCrewOperationAsync(Key, new CrewOperationStartRequest(leader, Req()))).Value!.ErrorCode);

        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, member));
        await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(member, crewId, true));
        var started = (await api.StartCrewOperationAsync(Key, new CrewOperationStartRequest(member, Req()))).Value!;
        Assert.True(started.Ok, started.Message);
        Assert.Equal("already", (await api.StartCrewOperationAsync(Key, new CrewOperationStartRequest(leader, Req()))).Value!.ErrorCode);
        var op = (await api.GetCrewBoardAsync(Key, leader)).Value!.Operation!;
        Assert.Equal(CrewOperations.Running, op.Status);
        Assert.True(op.Stages[0].Current);
        Assert.Equal(MissionKinds.Collection, op.Current!.Kind);

        // Etapa 1: los dos juntos en el lugar.
        Assert.Equal("need_crew_mate", (await api.TurnInCrewMissionAsync(Key, new CrewMissionTurnInRequest(leader, op.Current.Id, Req(), []))).Value!.ErrorCode);
        var first = (await api.TurnInCrewMissionAsync(Key, new CrewMissionTurnInRequest(leader, op.Current.Id, Req(), [member]))).Value!;
        Assert.True(first.Ok, first.Message);
        Assert.Contains("etapa 1 de 3", first.Message);
        op = (await api.GetCrewBoardAsync(Key, member)).Value!.Operation!;
        Assert.True(op.Stages[0].Done);
        Assert.True(op.Stages[1].Current);

        // Etapas 2 y 3 forzadas a eventos del mundo (la lógica de cumplir es la del tablero).
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        async Task ToTruck(Guid id)
        {
            await using var cmd = db.CreateCommand("UPDATE distrito.crew_missions SET kind = 'camion', target = 1, npc_code = NULL, item_code = NULL WHERE id = @id");
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        await ToTruck(op.Current!.Id);
        var second = (await api.CrewMissionProgressAsync(Key, new CrewMissionProgressRequest(member, MissionKinds.TruckJob, 1, Req()))).Value!;
        Assert.Contains("etapa 2 de 3", second.Message);
        op = (await api.GetCrewBoardAsync(Key, member)).Value!.Operation!;
        await ToTruck(op.Current!.Id);
        var prestigeBefore = (await api.GetCrewBoardAsync(Key, leader)).Value!.Prestige;
        var last = (await api.CrewMissionProgressAsync(Key, new CrewMissionProgressRequest(member, MissionKinds.TruckJob, 1, Req()))).Value!;
        Assert.True(last.Ok, last.Message);
        Assert.Contains("¡Operación cumplida!", last.Message);
        var after = (await api.GetCrewBoardAsync(Key, leader)).Value!;
        Assert.Equal(CrewOperations.Done, after.Operation!.Status);
        Assert.Equal(prestigeBefore + op.Prestige, after.Prestige);
    }
}
