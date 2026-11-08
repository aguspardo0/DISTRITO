using System.Net.Http.Json;
using Distrito.Backend.Economy;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Modo sombra: observa los locales reales después del ciclo, lo anota una sola vez y no toca nada del juego.</summary>
[Trait("Category", "Database")]
public class MarketShadowApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<(long Cash, int Stock)> BusinessOf(Guid owner)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("""
            SELECT a.balance_cents,
                   COALESCE((SELECT sum(s.quantity) FROM distrito.item_stacks s WHERE s.owner_account_id = b.account_id AND s.location = b.location), 0)::int
              FROM distrito.businesses b JOIN distrito.economic_accounts a ON a.id = b.account_id
             WHERE b.owner_character_id = @o
            """);
        cmd.Parameters.AddWithValue("o", owner);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt32(1));
    }

    [Fact]
    public async Task Anota_el_ciclo_una_vez_con_los_locales_reales_y_sin_tocar_el_juego()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var user = TestBackend.UniqueUser("sh");
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var me = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;

        var economy = TestBackend.Economy();
        Assert.True((await api.BusinessActionAsync(Key, "license", new BusinessActionRequest(me, Req()))).Value!.Ok);
        for (var i = 0; i < 5; i++)
            Assert.True((await api.BuyAsync(Key, new BuyItemRequest(me, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Req()))).Value!.Ok);
        Assert.True((await api.BusinessActionAsync(Key, "restock", new BusinessActionRequest(me, Req()))).Value!.Ok);
        var cycle = economy.CurrentCycle() + 1;
        Assert.Equal(3, await economy.RunBusinessCyclesAsync(cycle, default, me));

        var shadow = new MarketShadowService(NpgsqlDataSource.Create(TestBackend.ConnectionString), economy,
            new ConfigurationBuilder().Build(), NullLogger<MarketShadowService>.Instance);
        var before = await BusinessOf(me);
        Assert.True(await shadow.ObserveAsync(cycle, default));
        Assert.False(await shadow.ObserveAsync(cycle, default)); // una sola vez por ciclo
        Assert.Equal(before, await BusinessOf(me)); // no movió plata ni mercadería

        var report = await (await Get(factory, "internal/market/shadow?days=3")).Content.ReadFromJsonAsync<ShadowReport>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(report);
        Assert.True(report!.Enabled);
        Assert.NotNull(report.Last);
        var businessId = (await BusinessId(me)).ToString();
        var mine = (await LastCycleRow(cycle))!.Shops.Single(s => s.Id == businessId);
        Assert.Equal(3, mine.RealSold);
        Assert.Equal(5, mine.Stock); // lo que tenía al empezar el ciclo
        Assert.NotEmpty(report.Summary);
    }

    private static async Task<Guid> BusinessId(Guid owner)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("SELECT id FROM distrito.businesses WHERE owner_character_id = @o");
        cmd.Parameters.AddWithValue("o", owner);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<HttpResponseMessage> Get(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string path)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiHeaders.ServerKey, Key);
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<Distrito.Shared.Market.ShadowCycle?> LastCycleRow(long cycle)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("SELECT data FROM distrito.market_shadow_cycles WHERE cycle = @c");
        cmd.Parameters.AddWithValue("c", cycle);
        return System.Text.Json.JsonSerializer.Deserialize<Distrito.Shared.Market.ShadowCycle>((string)(await cmd.ExecuteScalarAsync())!,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}
