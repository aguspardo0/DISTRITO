using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Coaliciones: invitar/aceptar, tope de 3, salir con espera, disolución, territorio al 75 % y cobranzas con aliados.</summary>
[Trait("Category", "Database")]
public class CoalitionApiTests
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

    private static string UniqueName(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..10];

    private static async Task<(Guid Leader, Guid Crew, string Name)> NewCrew(DistritoApiClient api)
    {
        var leader = await NewCharacter(api);
        var name = UniqueName("Banda");
        var crew = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, name))).Value!.Status.CrewId!.Value;
        return (leader, crew, name);
    }

    private static async Task Join(DistritoApiClient api, (Guid Leader, Guid Crew, string Name) from, (Guid Leader, Guid Crew, string Name) to, string coalition)
    {
        var invite = (await api.InviteToCoalitionAsync(Key, new CoalitionInviteRequest(from.Leader, to.Name, coalition))).Value!;
        Assert.True(invite.Ok, invite.Message);
        var accept = (await api.RespondCoalitionAsync(Key, new CoalitionResponseRequest(to.Leader, from.Crew, true))).Value!;
        Assert.True(accept.Ok, accept.Message);
    }

    private static async Task Sql(string sql, params (string, object)[] args)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand(sql);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Formar_tope_de_tres_salir_con_espera_y_disolucion()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var a = await NewCrew(api);
        var b = await NewCrew(api);
        var c = await NewCrew(api);
        var d = await NewCrew(api);
        var coalition = UniqueName("Liga");

        Assert.Equal("invalid_name", (await api.InviteToCoalitionAsync(Key, new CoalitionInviteRequest(a.Leader, b.Name, "x"))).Value!.ErrorCode);
        Assert.Equal("crew_not_found", (await api.InviteToCoalitionAsync(Key, new CoalitionInviteRequest(a.Leader, "No Existe Nadie", coalition))).Value!.ErrorCode);
        await Join(api, a, b, coalition);
        await Join(api, b, c, "");
        var status = (await api.GetCrewStatusAsync(Key, c.Leader)).Value!;
        Assert.Equal(coalition, status.Coalition!.Name);
        Assert.Equal(3, status.Coalition.Crews.Count);
        Assert.Equal("coalition_full", (await api.InviteToCoalitionAsync(Key, new CoalitionInviteRequest(a.Leader, d.Name, ""))).Value!.ErrorCode);

        // C sale: queda con espera de 24 h. Después sale B y la coalición (solo A) se disuelve.
        Assert.True((await api.LeaveCoalitionAsync(Key, new CoalitionLeaveRequest(c.Leader))).Value!.Ok);
        Assert.NotNull((await api.GetCrewStatusAsync(Key, c.Leader)).Value!.Coalition!.CooldownUntil);
        Assert.Equal("coalition_cooldown", (await api.InviteToCoalitionAsync(Key, new CoalitionInviteRequest(c.Leader, d.Name, UniqueName("Otra")))).Value!.ErrorCode);
        Assert.True((await api.LeaveCoalitionAsync(Key, new CoalitionLeaveRequest(b.Leader))).Value!.Ok);
        var aStatus = (await api.GetCrewStatusAsync(Key, a.Leader)).Value!;
        Assert.Null(aStatus.Coalition!.Name);
        Assert.Contains(aStatus.History!, e => e.Kind == "coalition_left" && e.Detail.Contains("disolvió"));
    }

    [Fact]
    public async Task En_territorio_la_coalicion_suma_al_75_por_ciento_y_en_cobranzas_sirven_los_aliados()
    {
        var now = new DateTimeOffset(2095, 6, 6, 18, 0, 0, TimeSpan.Zero);
        await using var factory = TestBackend.Create(clock: new TestBackend.FixedClock(now));
        var api = TestBackend.Api(factory);
        var a = await NewCrew(api);
        var b = await NewCrew(api);
        var coalition = UniqueName("Liga");
        await Join(api, a, b, coalition);

        var season = TerritoryRules.SeasonAt(now);
        await Sql("""
            INSERT INTO distrito.zone_influence (season, zone, crew_id, points, reason, created_at)
            VALUES (@s, 'la-oficina', @a, 15, 'prueba', @at), (@s, 'la-oficina', @b, 12, 'prueba', @at)
            """, ("s", season), ("a", a.Crew), ("b", b.Crew), ("at", now.AddHours(-2)));
        var zone = (await api.GetEconomyAsync(Key, a.Leader)).Value!.Territory!.Zone(TerritoryRules.Office)!;
        Assert.Equal($"Coalición {coalition}", zone.ControllerCrew);
        Assert.Equal(20, zone.ControllerPoints); // (15 + 12) × 75 %

        // Cobranza: el jefe de A la hace con el jefe de B (aliado) como compañero.
        await Sql("""
            INSERT INTO distrito.character_reputation (character_id, family_code, value) VALUES (@c, 'caruso', 5)
            ON CONFLICT (character_id, family_code) DO UPDATE SET value = 5
            """, ("c", a.Leader));
        var t = EconomyCatalog.CarusoCollection;
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(a.Leader, t.Code, Guid.NewGuid().ToString()))).Value!.Snapshot.ActiveContract(t.Code)!;
        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(a.Leader, contract.Id, Guid.NewGuid().ToString(), [b.Leader]))).Value!;
        Assert.True(done.Ok, done.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents + t.RewardCents / 2, done.Snapshot.BalanceCents);
    }
}
