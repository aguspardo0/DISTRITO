using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>
/// Ánimo de las familias. Cada prueba usa un reloj propio en el futuro: su ventana de 24 h no tiene datos de otras
/// pruebas, y los datos que inserta (con fechas de esa ventana) no afectan a las demás.
/// </summary>
[Trait("Category", "Database")]
public class FamilyMoodApiTests
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

    [Fact]
    public async Task Diez_contratos_para_los_Caruso_los_ponen_en_cooperacion_y_pagan_mas()
    {
        var now = new DateTimeOffset(2099, 3, 10, 12, 30, 0, TimeSpan.Zero);
        await using var factory = TestBackend.Create(clock: new TestBackend.FixedClock(now));
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);
        Assert.Equal(FamilyRules.Neutral, (await api.GetEconomyAsync(Key, id)).Value!.MoodOf(EconomyCatalog.Caruso));

        // 10 entregas de repuestos (las paga el Taller Caruso) dentro de la ventana de esa hora.
        await Sql("""
            INSERT INTO distrito.contracts (id, template_code, character_id, status, accept_key, completed_at)
            SELECT gen_random_uuid(), 'entrega-repuestos', @c, 'completed', 'test:' || gen_random_uuid(), @at
            FROM generate_series(1, 10)
            """, ("c", id), ("at", now.AddHours(-2)));

        var snapshot = (await api.GetEconomyAsync(Key, id)).Value!;
        Assert.Equal(FamilyRules.Cooperation, snapshot.MoodOf(EconomyCatalog.Caruso));
        Assert.Equal(FamilyRules.Neutral, snapshot.MoodOf(EconomyCatalog.Moretti));

        var template = EconomyCatalog.SparePartsDelivery;
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, template.Code, Req()))).Value!.Snapshot.ActiveContract(template.Code)!;
        await api.BuyAsync(Key, new BuyItemRequest(id, EconomyCatalog.DepositNpc, template.ItemCode, 1, Req()));
        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()))).Value!;
        Assert.True(done.Ok, done.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.SparePartsPriceCents + template.RewardCents * 110 / 100,
            done.Snapshot.BalanceCents);
    }

    [Fact]
    public async Task Perder_reputacion_con_los_Moretti_los_pone_en_rivalidad_y_cobran_mas()
    {
        var now = new DateTimeOffset(2099, 7, 20, 9, 15, 0, TimeSpan.Zero);
        await using var factory = TestBackend.Create(clock: new TestBackend.FixedClock(now));
        var api = TestBackend.Api(factory);
        var id = await NewCharacter(api);

        await Sql("""
            INSERT INTO distrito.reputation_events (character_id, family_code, delta, reason, created_at)
            VALUES (@c, 'moretti', -6, 'prueba', @at), (@c, 'moretti', -4, 'prueba', @at)
            """, ("c", id), ("at", now.AddHours(-3)));
        // Lo de la hora actual todavía no cuenta (la ventana termina al comienzo de la hora).
        await Sql("""
            INSERT INTO distrito.reputation_events (character_id, family_code, delta, reason, created_at)
            VALUES (@c, 'valenti', -50, 'prueba', @at)
            """, ("c", id), ("at", now.AddMinutes(-5)));

        var snapshot = (await api.GetEconomyAsync(Key, id)).Value!;
        Assert.Equal(FamilyRules.Rivalry, snapshot.MoodOf(EconomyCatalog.Moretti));
        Assert.Equal(FamilyRules.Neutral, snapshot.MoodOf(EconomyCatalog.Valenti));

        var bought = (await api.BuyAsync(Key, new BuyItemRequest(id, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, 1, Req()))).Value!;
        Assert.True(bought.Ok, bought.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.CoffeeBagPriceCents * 110 / 100, bought.Snapshot.BalanceCents);
    }
}
