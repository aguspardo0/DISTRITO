using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Contratos 2 y 3: pedido urgente con plazo (y su falla) y cobranza en cuadrilla con reparto del pago.</summary>
[Trait("Category", "Database")]
public class ContractsApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static readonly ContractTemplate Urgent = EconomyCatalog.UrgentCoffeeOrder;
    private static readonly ContractTemplate Collection = EconomyCatalog.CarusoCollection;

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.GetEconomyAsync(Key, id); // fondos iniciales
        return id;
    }

    private static string Req() => Guid.NewGuid().ToString();

    private static async Task Sql(string sql, params (string Name, object Value)[] args)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand(sql);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task SetReputation(Guid character, string family, int value) => Sql("""
        INSERT INTO distrito.character_reputation (character_id, family_code, value) VALUES (@c, @f, @v)
        ON CONFLICT (character_id, family_code) DO UPDATE SET value = @v
        """, ("c", character), ("f", family), ("v", value));

    private static async Task BuyBags(DistritoApiClient api, Guid character, int n)
    {
        for (var i = 0; i < n; i++)
            Assert.True((await api.BuyAsync(Key, new BuyItemRequest(character, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Req()))).Value!.Ok);
    }

    [Fact]
    public async Task Pedido_urgente_a_tiempo_paga_Moretti_y_suma_reputacion()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);

        var accepted = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Urgent.Code, Req()))).Value!;
        Assert.True(accepted.Ok, accepted.Message);
        var contract = Assert.Single(accepted.Snapshot.ActiveContracts);
        Assert.NotNull(contract.ExpiresAt);
        Assert.InRange(contract.SecondsLeft(DateTimeOffset.UtcNow), Urgent.TimeLimitSeconds - 30, Urgent.TimeLimitSeconds);

        await BuyBags(api, id, 1);
        Assert.Equal("not_enough_items", (await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()))).Value!.ErrorCode);
        await BuyBags(api, id, 1);

        var delivered = (await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()))).Value!;
        Assert.True(delivered.Ok, delivered.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - 2 * EconomyCatalog.CoffeeBagPriceCents + Urgent.RewardCents, delivered.Snapshot.BalanceCents);
        Assert.Equal(0, delivered.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.Equal(4, delivered.Snapshot.ReputationWith(EconomyCatalog.Moretti));
        Assert.Empty(delivered.Snapshot.ActiveContracts);
    }

    [Fact]
    public async Task Pedido_urgente_vencido_falla_y_resta_reputacion_con_los_Moretti()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);
        await SetReputation(id, EconomyCatalog.Moretti, 10);

        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Urgent.Code, Req()))).Value!.Snapshot.ActiveContracts[0];
        await BuyBags(api, id, 2);
        await Sql("UPDATE distrito.contracts SET expires_at = now() - interval '1 second' WHERE id = @id", ("id", contract.Id));

        var late = (await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()))).Value!;
        Assert.Equal("contract_expired", late.ErrorCode);
        Assert.Empty(late.Snapshot.ActiveContracts);
        Assert.Equal(7, late.Snapshot.ReputationWith(EconomyCatalog.Moretti));
        Assert.Equal(2, late.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem)); // las bolsas siguen siendo suyas

        // La falla se aplica una sola vez, y después se puede volver a aceptar.
        Assert.Equal(7, (await api.GetEconomyAsync(Key, id)).Value!.ReputationWith(EconomyCatalog.Moretti));
        Assert.True((await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Urgent.Code, Req()))).Value!.Ok);
    }

    [Fact]
    public async Task Cobranza_exige_reputacion_cuadrilla_y_un_companero_presente_y_reparte_el_pago()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var leader = await NewCharacter(api);
        var mate = await NewCharacter(api);
        var stranger = await NewCharacter(api);

        Assert.Equal("not_enough_reputation",
            (await api.AcceptContractAsync(Key, new AcceptContractRequest(leader, Collection.Code, Req()))).Value!.ErrorCode);
        await SetReputation(leader, EconomyCatalog.Caruso, 5);
        Assert.Equal("no_crew",
            (await api.AcceptContractAsync(Key, new AcceptContractRequest(leader, Collection.Code, Req()))).Value!.ErrorCode);

        await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Banda " + Guid.NewGuid().ToString("N")[..10]));
        var crewId = (await api.GetCrewStatusAsync(Key, leader)).Value!.CrewId!.Value;
        await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, mate));
        Assert.True((await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(mate, crewId, Accept: true))).Value!.Ok);

        var accepted = (await api.AcceptContractAsync(Key, new AcceptContractRequest(leader, Collection.Code, Req()))).Value!;
        Assert.True(accepted.Ok, accepted.Message);
        var contract = accepted.Snapshot.ActiveContracts.Single(c => c.TemplateCode == Collection.Code);
        Assert.Null(contract.ExpiresAt);

        // Solo, o con alguien que no es de la cuadrilla: no se puede.
        Assert.Equal("need_crew_mate", (await api.DeliverAsync(Key, new DeliverContractRequest(leader, contract.Id, Req()))).Value!.ErrorCode);
        Assert.Equal("need_crew_mate",
            (await api.DeliverAsync(Key, new DeliverContractRequest(leader, contract.Id, Req(), [stranger]))).Value!.ErrorCode);

        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(leader, contract.Id, Req(), [mate, stranger]))).Value!;
        Assert.True(done.Ok, done.Message);
        var share = Collection.RewardCents / 2;
        Assert.Equal(EconomyCatalog.StartingFundsCents + share, done.Snapshot.BalanceCents);
        Assert.Equal(6, done.Snapshot.ReputationWith(EconomyCatalog.Valenti));
        Assert.Equal(3, done.Snapshot.ReputationWith(EconomyCatalog.Caruso));

        var mateEconomy = (await api.GetEconomyAsync(Key, mate)).Value!;
        Assert.Equal(EconomyCatalog.StartingFundsCents + share, mateEconomy.BalanceCents);
        Assert.Equal(6, mateEconomy.ReputationWith(EconomyCatalog.Valenti));
        Assert.Equal(EconomyCatalog.StartingFundsCents, (await api.GetEconomyAsync(Key, stranger)).Value!.BalanceCents);

        // El dinero se conserva: todo asiento suma cero.
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var unbalanced = db.CreateCommand(
            "SELECT count(*) FROM (SELECT transaction_id FROM distrito.ledger_entries GROUP BY transaction_id HAVING sum(amount_cents) <> 0) x");
        Assert.Equal(0L, (long)(await unbalanced.ExecuteScalarAsync())!);
    }
}
