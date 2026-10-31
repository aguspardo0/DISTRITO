using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Votaciones de la cuadrilla y acuerdos con las familias.</summary>
[Trait("Category", "Database")]
public class VotesAndPactsApiTests
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

    private static async Task<(Guid Leader, List<Guid> Others)> NewCrew(DistritoApiClient api, int others)
    {
        var leader = await NewCharacter(api);
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Banda " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        var list = new List<Guid>();
        for (var i = 0; i < others; i++)
        {
            var m = await NewCharacter(api);
            list.Add(m);
            await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, m));
            await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(m, crewId, true));
        }
        return (leader, list);
    }

    private static async Task Sql(string sql, params (string, object)[] args)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand(sql);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Votar_cambiar_de_jefe_con_mayoria_y_pago_de_la_caja()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var (leader, others) = await NewCrew(api, 2);
        var (a, b) = (others[0], others[1]);

        // A propone que B sea jefe (A vota sí al abrir). Con 3 integrantes hacen falta 2 síes.
        var opened = (await api.OpenCrewVoteAsync(Key, new CrewVoteOpenRequest(a, CrewVotes.Leader, b, 0))).Value!;
        Assert.True(opened.Ok, opened.Message);
        Assert.Equal(1, opened.Status.Vote!.Yes);
        Assert.Equal("vote_open", (await api.OpenCrewVoteAsync(Key, new CrewVoteOpenRequest(leader, CrewVotes.Leader, a, 0))).Value!.ErrorCode);

        var decided = (await api.CastCrewVoteAsync(Key, new CrewVoteCastRequest(b, opened.Status.Vote.Id, true))).Value!;
        Assert.True(decided.Ok, decided.Message);
        var status = (await api.GetCrewStatusAsync(Key, leader)).Value!;
        Assert.Equal(CrewRoles.Leader, status.Members.Single(m => m.CharacterId == b).Role);
        Assert.Equal(CrewRoles.Member, status.Members.Single(m => m.CharacterId == leader).Role);
        Assert.Equal("passed", status.Vote!.Status);

        // Pago: la caja tiene $10; votan darle $5 al exjefe. Dos "no" la hacen fracasar.
        await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(a, 1_000, Guid.NewGuid().ToString()));
        var pay = (await api.OpenCrewVoteAsync(Key, new CrewVoteOpenRequest(leader, CrewVotes.Payout, leader, 500))).Value!;
        Assert.True(pay.Ok, pay.Message);
        await api.CastCrewVoteAsync(Key, new CrewVoteCastRequest(a, pay.Status.Vote!.Id, false));
        var failed = (await api.CastCrewVoteAsync(Key, new CrewVoteCastRequest(b, pay.Status.Vote.Id, false))).Value!;
        Assert.Equal("failed", failed.Status.Vote!.Status);
        Assert.Equal(1_000, failed.Status.TreasuryCents);

        var pay2 = (await api.OpenCrewVoteAsync(Key, new CrewVoteOpenRequest(leader, CrewVotes.Payout, leader, 500))).Value!;
        await api.CastCrewVoteAsync(Key, new CrewVoteCastRequest(a, pay2.Status.Vote!.Id, true));
        Assert.Equal(500, (await api.GetCrewStatusAsync(Key, leader)).Value!.TreasuryCents);
        Assert.Equal(EconomyCatalog.StartingFundsCents + 500, (await api.GetEconomyAsync(Key, leader)).Value!.BalanceCents);
    }

    [Fact]
    public async Task Acuerdo_con_una_familia_pide_reputacion_y_caja_es_unico_y_da_bonus()
    {
        var now = new DateTimeOffset(2094, 2, 2, 18, 0, 0, TimeSpan.Zero);
        await using var factory = TestBackend.Create(clock: new TestBackend.FixedClock(now));
        var api = TestBackend.Api(factory);
        var (leader, others) = await NewCrew(api, 1);
        var (rival, _) = await NewCrew(api, 0);
        PactRequest Pact(Guid who) => new(who, EconomyCatalog.Caruso, Guid.NewGuid().ToString());

        Assert.Equal("not_leader", (await api.SignPactAsync(Key, Pact(others[0]))).Value!.ErrorCode);
        Assert.Equal("not_enough_reputation", (await api.SignPactAsync(Key, Pact(leader))).Value!.ErrorCode);
        await Sql("""
            INSERT INTO distrito.character_reputation (character_id, family_code, value) VALUES (@a, 'caruso', 30), (@b, 'caruso', 20)
            ON CONFLICT (character_id, family_code) DO UPDATE SET value = EXCLUDED.value
            """, ("a", leader), ("b", others[0]));
        Assert.Equal("insufficient_funds", (await api.SignPactAsync(Key, Pact(leader))).Value!.ErrorCode);

        // $50 para la caja: fondos de prueba al jefe desde la emisión (asiento válido) y depósito.
        await Sql("""
            WITH t AS (INSERT INTO distrito.ledger_transactions (id, idempotency_key, kind, description)
                       VALUES (gen_random_uuid(), 'test-grant:' || gen_random_uuid(), 'test', 'Fondos para prueba') RETURNING id),
                 mint AS (SELECT id FROM distrito.economic_accounts WHERE code = 'system:emision'),
                 acc AS (SELECT id FROM distrito.economic_accounts WHERE character_id = @c)
            INSERT INTO distrito.ledger_entries (transaction_id, account_id, amount_cents)
            SELECT t.id, mint.id, -5000 FROM t, mint UNION ALL SELECT t.id, acc.id, 5000 FROM t, acc;
            UPDATE distrito.economic_accounts SET balance_cents = balance_cents - 5000 WHERE code = 'system:emision';
            UPDATE distrito.economic_accounts SET balance_cents = balance_cents + 5000 WHERE character_id = @c;
            """, ("c", leader));
        Assert.True((await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(leader, 5_000, Guid.NewGuid().ToString()))).Value!.Ok);
        var signed = (await api.SignPactAsync(Key, Pact(leader))).Value!;
        Assert.True(signed.Ok, signed.Message);
        Assert.Contains(signed.Snapshot.Pacts!, p => p.FamilyCode == EconomyCatalog.Caruso && p.Mine);
        Assert.Equal(PactRules.DailyInfluence, signed.Snapshot.Territory!.Zone(TerritoryRules.Workshops)!.MyCrewPoints);

        // Otra cuadrilla ya no puede firmar con los Caruso esta temporada.
        await Sql("""
            INSERT INTO distrito.character_reputation (character_id, family_code, value) VALUES (@a, 'caruso', 60)
            ON CONFLICT (character_id, family_code) DO UPDATE SET value = 60
            """, ("a", rival));
        Assert.Equal("pact_taken", (await api.SignPactAsync(Key, Pact(rival))).Value!.ErrorCode);

        // Bonus: los repuestos los paga el Taller Caruso → +5 % para la cuadrilla con acuerdo.
        var member = others[0];
        var before = (await api.GetEconomyAsync(Key, member)).Value!.BalanceCents;
        var t = EconomyCatalog.SparePartsDelivery;
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(member, t.Code, Guid.NewGuid().ToString()))).Value!.Snapshot.ActiveContract(t.Code)!;
        await api.BuyAsync(Key, new BuyItemRequest(member, EconomyCatalog.DepositNpc, t.ItemCode, 1, Guid.NewGuid().ToString()));
        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(member, contract.Id, Guid.NewGuid().ToString()))).Value!;
        Assert.True(done.Ok, done.Message);
        Assert.Equal(before - EconomyCatalog.SparePartsPriceCents + t.RewardCents * 105 / 100, done.Snapshot.BalanceCents);
    }
}
