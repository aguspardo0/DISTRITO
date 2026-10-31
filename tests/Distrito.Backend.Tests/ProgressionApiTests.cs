using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Distrito.Shared.Progression;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Progresión: experiencia y oficios suben con contratos y ventas; las ventajas aprobadas se aplican.</summary>
[Trait("Category", "Database")]
public class ProgressionApiTests
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

    private static async Task SetProgress(Guid character, int logistics = 0, int negotiation = 0, int administration = 0)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("""
            INSERT INTO distrito.character_progress (character_id, logistics, negotiation, administration) VALUES (@c, @l, @n, @a)
            ON CONFLICT (character_id) DO UPDATE SET logistics = @l, negotiation = @n, administration = @a
            """);
        cmd.Parameters.AddWithValue("c", character);
        cmd.Parameters.AddWithValue("l", logistics);
        cmd.Parameters.AddWithValue("n", negotiation);
        cmd.Parameters.AddWithValue("a", administration);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<EconomyActionResult> DoSpareParts(DistritoApiClient api, Guid id)
    {
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, EconomyCatalog.SparePartsDelivery.Code, Req())))
            .Value!.Snapshot.ActiveContract(EconomyCatalog.SparePartsDelivery.Code)!;
        await api.BuyAsync(Key, new BuyItemRequest(id, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 1, Req()));
        return (await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()))).Value!;
    }

    [Fact]
    public async Task Un_contrato_da_experiencia_negociacion_y_logistica()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);
        Assert.Equal(1, (await api.GetEconomyAsync(Key, id)).Value!.ProgressOrEmpty.Level);

        var done = await DoSpareParts(api, id);
        Assert.True(done.Ok, done.Message);
        var p = done.Snapshot.ProgressOrEmpty;
        Assert.Equal(ProgressionRules.XpPerContract, p.Xp);
        Assert.Equal(2, p.Level);
        Assert.Equal(1, p.Logistics);
        Assert.Equal(1, p.Negotiation);
        Assert.Equal(0, p.Administration);
    }

    [Fact]
    public async Task Negociacion_sube_el_pago_y_lo_paga_la_familia()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);
        await SetProgress(id, negotiation: 15); // nivel 3: +2 %

        var done = await DoSpareParts(api, id);
        Assert.True(done.Ok, done.Message);
        var reward = EconomyCatalog.SparePartsDelivery.RewardCents * 102 / 100;
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.SparePartsPriceCents + reward, done.Snapshot.BalanceCents);

        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var total = db.CreateCommand("SELECT sum(balance_cents) FROM distrito.economic_accounts");
        Assert.Equal(0m, (decimal)(await total.ExecuteScalarAsync())!); // el dinero se conserva
    }

    [Fact]
    public async Task Logistica_alarga_el_plazo_del_pedido_urgente()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);
        await SetProgress(id, logistics: 15); // nivel 3: +20 s

        var accepted = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, EconomyCatalog.UrgentCoffeeOrder.Code, Req()))).Value!;
        Assert.True(accepted.Ok, accepted.Message);
        var left = accepted.Snapshot.ActiveContracts[0].SecondsLeft(DateTimeOffset.UtcNow);
        Assert.InRange(left, EconomyCatalog.UrgentCoffeeOrder.TimeLimitSeconds + 20 - 15, EconomyCatalog.UrgentCoffeeOrder.TimeLimitSeconds + 20);
    }

    [Fact]
    public async Task Administracion_vende_uno_mas_desde_el_nivel_5_y_cada_ciclo_suma_practica()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var economy = TestBackend.Economy();
        var id = await NewCharacter(api);
        await api.BusinessActionAsync(Key, "license", new BusinessActionRequest(id, Req()));
        // Después de la licencia quedan $5: se le dan $10 de prueba desde la emisión (asiento contable válido).
        await using (var db = NpgsqlDataSource.Create(TestBackend.ConnectionString))
        {
            await using var cmd = db.CreateCommand("""
                WITH t AS (INSERT INTO distrito.ledger_transactions (id, idempotency_key, kind, description)
                           VALUES (gen_random_uuid(), 'test-grant:' || gen_random_uuid(), 'test', 'Fondos para prueba') RETURNING id),
                     mint AS (SELECT id FROM distrito.economic_accounts WHERE code = 'system:emision'),
                     acc AS (SELECT id FROM distrito.economic_accounts WHERE character_id = @c)
                INSERT INTO distrito.ledger_entries (transaction_id, account_id, amount_cents)
                SELECT t.id, mint.id, -1000 FROM t, mint UNION ALL SELECT t.id, acc.id, 1000 FROM t, acc;
                UPDATE distrito.economic_accounts SET balance_cents = balance_cents - 1000 WHERE code = 'system:emision';
                UPDATE distrito.economic_accounts SET balance_cents = balance_cents + 1000 WHERE character_id = @c;
                """);
            cmd.Parameters.AddWithValue("c", id);
            await cmd.ExecuteNonQueryAsync();
        }
        for (var i = 0; i < 4; i++)
            Assert.True((await api.BuyAsync(Key, new BuyItemRequest(id, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Req()))).Value!.Ok);
        await api.BusinessActionAsync(Key, "restock", new BusinessActionRequest(id, Req()));
        await SetProgress(id, administration: 50); // nivel 5

        Assert.Equal(EconomyCatalog.CoffeeDemandPerCycle + 1, await economy.RunBusinessCyclesAsync(economy.CurrentCycle() + 1, default, id));

        var p = (await api.GetEconomyAsync(Key, id)).Value!.ProgressOrEmpty;
        Assert.Equal(51, p.Administration);
        Assert.Equal(ProgressionRules.XpPerSalesCycle, p.Xp);
    }
}
