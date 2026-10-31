using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>
/// Territorio. Cada prueba usa un reloj fijo en una temporada lejana propia, así no la afectan datos de otras pruebas.
/// </summary>
[Trait("Category", "Database")]
public class TerritoryApiTests
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

    private static async Task<(Guid Member, Guid Crew, string CrewName)> NewCrewMember(DistritoApiClient api)
    {
        var member = await NewCharacter(api);
        var name = "Banda " + Guid.NewGuid().ToString("N")[..10];
        var crew = (await api.CreateCrewAsync(Key, new CreateCrewRequest(member, name))).Value!.Status.CrewId!.Value;
        return (member, crew, name);
    }

    private static async Task AddInfluence(Guid crew, string zone, int points, DateTimeOffset at)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("""
            INSERT INTO distrito.zone_influence (season, zone, crew_id, points, reason, created_at) VALUES (@s, @z, @c, @p, 'prueba', @at)
            """);
        cmd.Parameters.AddWithValue("s", TerritoryRules.SeasonAt(at));
        cmd.Parameters.AddWithValue("z", zone);
        cmd.Parameters.AddWithValue("c", crew);
        cmd.Parameters.AddWithValue("p", points);
        cmd.Parameters.AddWithValue("at", at);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<EconomyActionResult> DoSpareParts(DistritoApiClient api, Guid id)
    {
        var t = EconomyCatalog.SparePartsDelivery;
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, t.Code, Req()))).Value!.Snapshot.ActiveContract(t.Code)!;
        await api.BuyAsync(Key, new BuyItemRequest(id, EconomyCatalog.DepositNpc, t.ItemCode, 1, Req()));
        return (await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()))).Value!;
    }

    [Fact]
    public async Task Controlar_la_zona_da_mas_pago_y_cada_contrato_suma_influencia_con_tope_diario()
    {
        var now = new DateTimeOffset(2098, 5, 14, 15, 0, 0, TimeSpan.Zero);
        await using var factory = TestBackend.Create(clock: new TestBackend.FixedClock(now));
        var api = TestBackend.Api(factory);
        var (member, crew, crewName) = await NewCrewMember(api);

        // Ayer: 20 de influencia en Los Talleres (controla). Hoy ya sumó 29: este contrato solo suma 1.
        await AddInfluence(crew, TerritoryRules.Workshops, 20, now.AddDays(-1));
        await AddInfluence(crew, TerritoryRules.Workshops, 29, now.AddMinutes(-10));

        var before = (await api.GetEconomyAsync(Key, member)).Value!.Territory!.Zone(TerritoryRules.Workshops)!;
        Assert.Equal(crewName, before.ControllerCrew);
        Assert.Equal(49, before.MyCrewPoints);

        var done = await DoSpareParts(api, member);
        Assert.True(done.Ok, done.Message);
        var t = EconomyCatalog.SparePartsDelivery;
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.SparePartsPriceCents + t.RewardCents * 105 / 100, done.Snapshot.BalanceCents);
        Assert.Equal(50, done.Snapshot.Territory!.Zone(TerritoryRules.Workshops)!.MyCrewPoints);
    }

    [Fact]
    public async Task Sin_llegar_a_20_nadie_controla_y_al_cerrar_la_temporada_el_ganador_queda_en_el_historial()
    {
        var seasonStart = TerritoryRules.SeasonStart(TerritoryRules.SeasonAt(new DateTimeOffset(2097, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var during = seasonStart.AddDays(3);
        await using var factory = TestBackend.Create(clock: new TestBackend.FixedClock(during));
        var api = TestBackend.Api(factory);
        var (a, crewA, nameA) = await NewCrewMember(api);
        var (_, crewB, _) = await NewCrewMember(api);

        await AddInfluence(crewA, TerritoryRules.Corner, 19, during.AddHours(-1));
        Assert.Null((await api.GetEconomyAsync(Key, a)).Value!.Territory!.Zone(TerritoryRules.Corner)!.ControllerCrew);
        await AddInfluence(crewA, TerritoryRules.Corner, 6, during.AddHours(-1));
        await AddInfluence(crewB, TerritoryRules.Corner, 22, during.AddHours(-1));
        Assert.Equal(nameA, (await api.GetEconomyAsync(Key, a)).Value!.Territory!.Zone(TerritoryRules.Corner)!.ControllerCrew);

        // Ya en la temporada siguiente: se cierra la anterior una sola vez.
        var next = TerritoryRules.SeasonEnd(TerritoryRules.SeasonAt(during)).AddHours(1);
        var economy = new Distrito.Backend.Economy.EconomyService(NpgsqlDataSource.Create(TestBackend.ConnectionString),
            new TestBackend.FixedClock(next), new ConfigurationBuilder().Build());
        Assert.True(await economy.CloseSeasonsAsync(default) >= 1);
        Assert.Equal(0, await economy.CloseSeasonsAsync(default));

        var history = (await api.GetCrewStatusAsync(Key, a)).Value!.History!;
        Assert.Contains(history, e => e.Kind == "season_won" && e.Detail.Contains("La Esquina"));
    }
}
