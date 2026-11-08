using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Negocios que crecen: mejoras (pagadas al Taller Caruso), precio, fama y libro de cuentas.</summary>
[Trait("Category", "Database")]
public class BusinessGrowthApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> Character(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser("bg");
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    private static async Task Grant(DistritoApiClient api, Guid character, long cents)
    {
        await api.GetEconomyAsync(Key, character);
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

    /// <summary>
    /// ¿Recibió <paramref name="code"/> un movimiento exacto de <paramref name="amount"/> después de <paramref name="afterId"/>?
    /// Se mira el movimiento y no el saldo, porque otras pruebas en paralelo también le pagan o le cobran al taller.
    /// </summary>
    private static async Task<bool> Received(string code, long afterId, long amount)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand(
            "SELECT count(*) FROM distrito.ledger_entries e JOIN distrito.economic_accounts a ON a.id = e.account_id " +
            "WHERE a.code = @c AND e.id > @after AND e.amount_cents = @amount");
        cmd.Parameters.AddWithValue("c", code);
        cmd.Parameters.AddWithValue("after", afterId);
        cmd.Parameters.AddWithValue("amount", amount);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private static async Task<long> LastEntryId()
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("SELECT COALESCE(max(id), 0) FROM distrito.ledger_entries");
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Mejorar_el_local_lo_cobra_el_taller_y_atiende_mas_gente()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var economy = TestBackend.Economy();
        var me = await Character(api);
        var cafe = EconomyCatalog.CoffeeStandLocation;

        Assert.Equal("no_business", (await api.UpgradeBusinessAsync(Key, new BusinessUpgradeRequest(me, cafe, BusinessGrowth.Capacity, Req()))).Value!.ErrorCode);
        Assert.True((await api.BusinessActionAsync(Key, "license", new BusinessActionRequest(me, Req()))).Value!.Ok);
        Assert.Equal("insufficient_funds", (await api.UpgradeBusinessAsync(Key, new BusinessUpgradeRequest(me, cafe, BusinessGrowth.Capacity, Req()))).Value!.ErrorCode);
        Assert.Equal("unknown_branch", (await api.UpgradeBusinessAsync(Key, new BusinessUpgradeRequest(me, cafe, "piscina", Req()))).Value!.ErrorCode);

        var info = (await api.GetMarketInfoAsync(Key, me)).Value!.Ledgers.Single().Growth!;
        var cost = info.Upgrades.Single(u => u.Branch == BusinessGrowth.Capacity).CostCents;
        Assert.Equal(3, info.Capacity);
        Assert.Equal("Puesto", info.Category);

        await Grant(api, me, cost + 2_000);
        var lastEntry = await LastEntryId();
        var before = (await api.GetEconomyAsync(Key, me)).Value!.BalanceCents;
        var upgraded = (await api.UpgradeBusinessAsync(Key, new BusinessUpgradeRequest(me, cafe, BusinessGrowth.Capacity, Req()))).Value!;
        Assert.True(upgraded.Ok, upgraded.Message);
        Assert.Contains("Mostrador grande", upgraded.Message);
        Assert.Equal(1, upgraded.Snapshot.Business!.LevelCapacity);
        Assert.Equal(before - cost, upgraded.Snapshot.BalanceCents);
        Assert.True(await Received(EconomyCatalog.WorkshopNpc, lastEntry, cost), "la mejora la cobra el Taller Caruso");

        var next = (await api.GetMarketInfoAsync(Key, me)).Value!.Ledgers.Single().Growth!;
        Assert.Equal(4, next.Capacity);
        Assert.True(next.Upgrades.Single(u => u.Branch == BusinessGrowth.Capacity).CostCents > cost);

        // Con capacidad 4 (y la ciudad llena de gente en la prueba) atiende a 4 por ciclo.
        for (var i = 0; i < 5; i++)
            Assert.True((await api.BuyAsync(Key, new BuyItemRequest(me, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Req()))).Value!.Ok);
        Assert.True((await api.BusinessActionAsync(Key, "restock", new BusinessActionRequest(me, Req()))).Value!.Ok);
        Assert.Equal(4, await economy.RunBusinessCyclesAsync(economy.CurrentCycle() + 1, default, me));
        var business = (await api.GetEconomyAsync(Key, me)).Value!.Business!;
        Assert.True(business.Fame > 0, "atender suma fama");
        Assert.Equal(4, business.LastCustomers);

        // Las pizarras de la esquina muestran el local con su precio y la gente que hay.
        var corner = (await api.GetMarketBoardsAsync(Key)).Value!.Single(b => b.NpcCode == EconomyCatalog.CoffeeCornerNpc);
        Assert.Contains(corner.Lines, l => l.ItemCode == business.Id.ToString() && l.Name.Contains("(Puesto)"));
        Assert.True(corner.Crowd >= 4);
    }

    [Fact]
    public async Task El_dueno_elige_el_precio_dentro_del_rango()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var economy = TestBackend.Economy();
        var me = await Character(api);
        var cafe = EconomyCatalog.CoffeeStandLocation;
        Assert.True((await api.BusinessActionAsync(Key, "license", new BusinessActionRequest(me, Req()))).Value!.Ok);

        Assert.Equal("price_range", (await api.SetBusinessPriceAsync(Key, new BusinessPriceRequest(me, cafe, 100, Req()))).Value!.ErrorCode);
        Assert.Equal("price_range", (await api.SetBusinessPriceAsync(Key, new BusinessPriceRequest(me, cafe, 100_000, Req()))).Value!.ErrorCode);
        var set = (await api.SetBusinessPriceAsync(Key, new BusinessPriceRequest(me, cafe, 700, Req()))).Value!;
        Assert.True(set.Ok, set.Message);
        Assert.Contains("caro", set.Message);
        Assert.Equal(700, set.Snapshot.Business!.SalePriceCents);

        // Se vende al precio elegido.
        Assert.True((await api.BuyAsync(Key, new BuyItemRequest(me, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Req()))).Value!.Ok);
        Assert.True((await api.BusinessActionAsync(Key, "restock", new BusinessActionRequest(me, Req()))).Value!.Ok);
        Assert.Equal(1, await economy.RunBusinessCyclesAsync(economy.CurrentCycle() + 1, default, me));
        var ledger = (await api.GetMarketInfoAsync(Key, me)).Value!.Ledgers.Single();
        Assert.Equal(700, ledger.RevenueToday);
        Assert.Equal(700, ledger.SalePriceCents);
        Assert.Equal(1_500, ledger.Growth!.MaxPriceCents);
    }
}
