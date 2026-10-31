using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Puesto de café: licencia, reposición, ventas por ciclo (también sin el dueño conectado) y retiro.</summary>
[Trait("Category", "Database")]
public class BusinessApiTests
{
    private const string Key = TestBackend.ServerKey;

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    /// <summary>Le da dinero al personaje con una transacción contable válida (desde la emisión), para preparar escenarios.</summary>
    private static async Task GrantMoney(DistritoApiClient api, Guid character, long cents)
    {
        await api.GetEconomyAsync(Key, character); // asegura que exista la cuenta del personaje
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("""
            WITH t AS (INSERT INTO distrito.ledger_transactions (id, idempotency_key, kind, description)
                       VALUES (gen_random_uuid(), 'test-grant:' || gen_random_uuid(), 'test', 'Fondos para prueba') RETURNING id),
                 mint AS (SELECT id FROM distrito.economic_accounts WHERE code = 'system:emision'),
                 acc AS (SELECT id FROM distrito.economic_accounts WHERE character_id = @c)
            INSERT INTO distrito.ledger_entries (transaction_id, account_id, amount_cents)
            SELECT t.id, mint.id, -@amount FROM t, mint UNION ALL SELECT t.id, acc.id, @amount FROM t, acc;
            UPDATE distrito.economic_accounts SET balance_cents = balance_cents - @amount WHERE code = 'system:emision';
            UPDATE distrito.economic_accounts SET balance_cents = balance_cents + @amount WHERE character_id = @c;
            """);
        cmd.Parameters.AddWithValue("c", character);
        cmd.Parameters.AddWithValue("amount", cents);
        await cmd.ExecuteNonQueryAsync();
    }

    private static BusinessActionRequest Req(Guid character) => new(character, Guid.NewGuid().ToString());

    private static Task<ApiResult<EconomyActionResult>> BuyBag(DistritoApiClient api, Guid character) =>
        api.BuyAsync(Key, new BuyItemRequest(character, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Guid.NewGuid().ToString()));

    [Fact]
    public async Task Ciclo_completo_licencia_reponer_vender_y_retirar()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var economy = TestBackend.Economy();
        var id = await NewCharacter(api);

        var license = (await api.BusinessActionAsync(Key, "license", Req(id))).Value!;
        Assert.True(license.Ok, license.Message);
        Assert.NotNull(license.Snapshot.Business);
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.CoffeeStandLicenseCents, license.Snapshot.BalanceCents);

        await GrantMoney(api, id, 1_000); // con $5 no alcanza para 3 bolsas
        for (var i = 0; i < 3; i++)
            Assert.True((await BuyBag(api, id)).Value!.Ok);
        var restocked = (await api.BusinessActionAsync(Key, "restock", Req(id))).Value!;
        Assert.True(restocked.Ok, restocked.Message);
        Assert.Equal(3, restocked.Snapshot.Business!.Stock);
        Assert.Equal(0, restocked.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));

        // Dos ciclos: vende 2 y después 1 (se queda sin stock). El dueño no hace nada: la economía sigue sola.
        var start = economy.CurrentCycle();
        Assert.Equal(2, await economy.RunBusinessCyclesAsync(start + 1, default, id));
        Assert.Equal(0, await economy.RunBusinessCyclesAsync(start + 1, default, id)); // repetir el mismo ciclo no vende de nuevo
        Assert.Equal(1, await economy.RunBusinessCyclesAsync(start + 2, default, id));
        Assert.Equal(0, await economy.RunBusinessCyclesAsync(start + 5, default, id)); // sin stock, nada

        var business = (await api.GetEconomyAsync(Key, id)).Value!.Business!;
        Assert.Equal(0, business.Stock);
        Assert.Equal(3, business.UnitsSold);
        Assert.Equal(3 * EconomyCatalog.CoffeeSalePriceCents, business.RevenueCents);
        Assert.Equal(2 * EconomyCatalog.CoffeeStandRentCents, business.RentCents);
        Assert.Equal(business.RevenueCents - business.RentCents, business.CashCents);
        Assert.Equal(business.RevenueCents - business.RentCents - 3 * EconomyCatalog.CoffeeBagPriceCents, business.ProfitCents);

        var before = (await api.GetEconomyAsync(Key, id)).Value!.BalanceCents;
        var withdrawn = (await api.BusinessActionAsync(Key, "withdraw", Req(id))).Value!;
        Assert.True(withdrawn.Ok, withdrawn.Message);
        Assert.Equal(before + business.CashCents, withdrawn.Snapshot.BalanceCents);
        Assert.Equal(0, withdrawn.Snapshot.Business!.CashCents);
    }

    [Fact]
    public async Task Reglas_licencia_unica_saldo_y_reponer_sin_mercaderia()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);

        Assert.Equal("no_business", (await api.BusinessActionAsync(Key, "restock", Req(id))).Value!.ErrorCode);
        Assert.True((await api.BusinessActionAsync(Key, "license", Req(id))).Value!.Ok);
        Assert.Equal("already_owner", (await api.BusinessActionAsync(Key, "license", Req(id))).Value!.ErrorCode);
        Assert.Equal("not_enough_items", (await api.BusinessActionAsync(Key, "restock", Req(id))).Value!.ErrorCode);
        Assert.Equal("no_cash", (await api.BusinessActionAsync(Key, "withdraw", Req(id))).Value!.ErrorCode);

        // Le quedan $5: alcanza para otra licencia? No, y además ya tiene una. Otro personaje sin plata suficiente:
        var poor = await NewCharacter(api);
        for (var i = 0; i < 6; i++) await BuyBag(api, poor); // gasta $18 de $20
        Assert.Equal("insufficient_funds", (await api.BusinessActionAsync(Key, "license", Req(poor))).Value!.ErrorCode);
    }

    [Fact]
    public async Task Ciclos_concurrentes_no_duplican_ventas_y_el_dinero_se_conserva()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var economy = TestBackend.Economy();
        var id = await NewCharacter(api);
        await api.BusinessActionAsync(Key, "license", Req(id));
        await GrantMoney(api, id, 1_000);
        for (var i = 0; i < 4; i++) Assert.True((await BuyBag(api, id)).Value!.Ok);
        await api.BusinessActionAsync(Key, "restock", Req(id));

        var cycle = economy.CurrentCycle() + 1;
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => economy.RunBusinessCyclesAsync(cycle, default, id)));
        Assert.Equal(EconomyCatalog.CoffeeDemandPerCycle, results.Sum());

        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var total = db.CreateCommand("SELECT sum(balance_cents) FROM distrito.economic_accounts");
        Assert.Equal(0m, (decimal)(await total.ExecuteScalarAsync())!);
        await using var unbalanced = db.CreateCommand(
            "SELECT count(*) FROM (SELECT transaction_id FROM distrito.ledger_entries GROUP BY transaction_id HAVING sum(amount_cents) <> 0) x");
        Assert.Equal(0L, (long)(await unbalanced.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task El_precio_de_la_bolsa_coincide_con_la_base()
    {
        await using var factory = TestBackend.Create();
        _ = factory.CreateClient();
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("SELECT reference_price_cents FROM distrito.item_definitions WHERE code = @c");
        cmd.Parameters.AddWithValue("c", EconomyCatalog.CoffeeBagItem);
        Assert.Equal(EconomyCatalog.CoffeeBagPriceCents, (long)(await cmd.ExecuteScalarAsync())!);
    }
}
