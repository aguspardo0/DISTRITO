using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Contratos por nivel: requisitos de nivel, oficio y reputación; cobranza grande con dos compañeros.</summary>
[Trait("Category", "Database")]
public class LevelContractsApiTests
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

    private static string Req() => Guid.NewGuid().ToString();

    private static async Task Sql(string sql, params (string, object)[] args)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand(sql);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task SetProgress(Guid c, long xp, int logistics = 0, int negotiation = 0) => Sql("""
        INSERT INTO distrito.character_progress (character_id, xp, logistics, negotiation) VALUES (@c, @xp, @l, @n)
        ON CONFLICT (character_id) DO UPDATE SET xp = @xp, logistics = @l, negotiation = @n
        """, ("c", c), ("xp", xp), ("l", logistics), ("n", negotiation));

    [Fact]
    public async Task Revisar_las_cuentas_pide_nivel_y_negociacion_y_se_hace_en_el_almacen()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);
        var audit = EconomyCatalog.AuditBooks;

        Assert.Equal("level_too_low", (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, audit.Code, Req()))).Value!.ErrorCode);
        await SetProgress(id, xp: 1000, negotiation: 14);
        Assert.Equal("skill_too_low", (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, audit.Code, Req()))).Value!.ErrorCode);
        await SetProgress(id, xp: 1000, negotiation: 15); // Negociación 3: +2 % de pago

        var accepted = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, audit.Code, Req()))).Value!;
        Assert.True(accepted.Ok, accepted.Message);
        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(id, accepted.Snapshot.ActiveContract(audit.Code)!.Id, Req()))).Value!;
        Assert.True(done.Ok, done.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents + audit.RewardCents * 102 / 100, done.Snapshot.BalanceCents);
        Assert.Equal(5, done.Snapshot.ReputationWith(EconomyCatalog.Valenti));
    }

    [Fact]
    public async Task Reparto_para_la_fiesta_lleva_tres_cajones_al_taller()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);
        var party = EconomyCatalog.PartyDelivery;
        await SetProgress(id, xp: 300, logistics: 5);

        var accepted = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, party.Code, Req()))).Value!;
        Assert.True(accepted.Ok, accepted.Message);
        var contract = accepted.Snapshot.ActiveContract(party.Code)!;
        Assert.InRange(contract.SecondsLeft(DateTimeOffset.UtcNow), party.TimeLimitSeconds + 10 - 15, party.TimeLimitSeconds + 10); // Logística 2: +10 s

        for (var i = 0; i < 3; i++)
            Assert.True((await api.BuyAsync(Key, new BuyItemRequest(id, EconomyCatalog.DepositNpc, party.ItemCode, 1, Req()))).Value!.Ok);
        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()))).Value!;
        Assert.True(done.Ok, done.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - 3 * EconomyCatalog.JazzClub.ItemPriceCents + party.RewardCents, done.Snapshot.BalanceCents);
        Assert.Equal(4, done.Snapshot.ReputationWith(EconomyCatalog.Moretti));
        Assert.Equal(2, done.Snapshot.ReputationWith(EconomyCatalog.Caruso));
    }

    [Fact]
    public async Task Cobranza_grande_necesita_dos_companeros_presentes_y_reparte_entre_tres()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Banda " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        var mates = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var mate = await NewCharacter(api);
            mates.Add(mate);
            await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, mate));
            await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(mate, crewId, true));
        }
        var big = EconomyCatalog.BigCollection;
        await SetProgress(leader, xp: 2800);
        await Sql("""
            INSERT INTO distrito.character_reputation (character_id, family_code, value) VALUES (@c, 'caruso', 15)
            ON CONFLICT (character_id, family_code) DO UPDATE SET value = 15
            """, ("c", leader));

        var accepted = (await api.AcceptContractAsync(Key, new AcceptContractRequest(leader, big.Code, Req()))).Value!;
        Assert.True(accepted.Ok, accepted.Message);
        var contract = accepted.Snapshot.ActiveContract(big.Code)!;

        Assert.Equal("need_crew_mate", (await api.DeliverAsync(Key, new DeliverContractRequest(leader, contract.Id, Req(), [mates[0]]))).Value!.ErrorCode);
        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(leader, contract.Id, Req(), mates))).Value!;
        Assert.True(done.Ok, done.Message);
        var share = big.RewardCents / 3;
        Assert.Equal(EconomyCatalog.StartingFundsCents + share, done.Snapshot.BalanceCents);
        foreach (var mate in mates)
            Assert.Equal(EconomyCatalog.StartingFundsCents + share, (await api.GetEconomyAsync(Key, mate)).Value!.BalanceCents);
    }
}
