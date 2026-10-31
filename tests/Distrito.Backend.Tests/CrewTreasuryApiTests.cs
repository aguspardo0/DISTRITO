using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Caja común de la cuadrilla: depósitos, retiros según permisos, configuración del jefe y aporte de contratos.</summary>
[Trait("Category", "Database")]
public class CrewTreasuryApiTests
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

    private static async Task<(Guid Leader, Guid Second, Guid Member)> NewCrew(DistritoApiClient api)
    {
        var leader = await NewCharacter(api);
        var crewId = (await api.CreateCrewAsync(Key, new CreateCrewRequest(leader, "Banda " + Guid.NewGuid().ToString("N")[..10]))).Value!.Status.CrewId!.Value;
        var second = await NewCharacter(api);
        var member = await NewCharacter(api);
        foreach (var who in new[] { second, member })
        {
            await api.InviteToCrewAsync(Key, new InviteToCrewRequest(leader, who));
            Assert.True((await api.RespondCrewInvitationAsync(Key, new CrewInvitationResponse(who, crewId, true))).Value!.Ok);
        }
        Assert.True((await api.SetCrewRoleAsync(Key, new SetCrewRoleRequest(leader, second, CrewRoles.Second))).Value!.Ok);
        return (leader, second, member);
    }

    [Fact]
    public async Task Depositar_retirar_con_permisos_y_configuracion_del_jefe()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var (leader, second, member) = await NewCrew(api);

        var deposit = (await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(member, 500, Req()))).Value!;
        Assert.True(deposit.Ok, deposit.Message);
        Assert.Equal(500, deposit.Status.TreasuryCents);
        Assert.Equal(EconomyCatalog.StartingFundsCents - 500, (await api.GetEconomyAsync(Key, member)).Value!.BalanceCents);
        Assert.Equal("deposit", deposit.Status.History![0].Kind);

        // Reintento con el mismo pedido: no se duplica.
        var again = new CrewMoneyRequest(second, 300, Req());
        await api.CrewMoneyAsync(Key, true, again);
        Assert.Equal(800, (await api.CrewMoneyAsync(Key, true, again)).Value!.Status.TreasuryCents);

        Assert.Equal("insufficient_funds", (await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(member, 999_999, Req()))).Value!.ErrorCode);
        Assert.Equal("not_allowed", (await api.CrewMoneyAsync(Key, false, new CrewMoneyRequest(member, 100, Req()))).Value!.ErrorCode);
        Assert.Equal("not_allowed", (await api.CrewMoneyAsync(Key, false, new CrewMoneyRequest(second, 100, Req()))).Value!.ErrorCode);

        // Solo el jefe configura; el aporte va de 0 a 20 %.
        var open = new CrewPermissions(SecondCanWithdraw: true, SecondCanInvite: true, MemberCanInvite: true, TithePercent: 10);
        Assert.Equal("not_leader", (await api.SetCrewSettingsAsync(Key, new CrewSettingsRequest(second, open))).Value!.ErrorCode);
        Assert.Equal("invalid_settings", (await api.SetCrewSettingsAsync(Key, new CrewSettingsRequest(leader, open with { TithePercent = 25 }))).Value!.ErrorCode);
        var set = (await api.SetCrewSettingsAsync(Key, new CrewSettingsRequest(leader, open))).Value!;
        Assert.True(set.Ok, set.Message);
        Assert.Equal(open, set.Status.Rules);

        var bySecond = (await api.CrewMoneyAsync(Key, false, new CrewMoneyRequest(second, 100, Req()))).Value!;
        Assert.True(bySecond.Ok, bySecond.Message);
        Assert.True(bySecond.Status.CanWithdraw);
        var all = (await api.CrewMoneyAsync(Key, false, new CrewMoneyRequest(leader, 0, Req()))).Value!; // "todo"
        Assert.True(all.Ok, all.Message);
        Assert.Equal(0, all.Status.TreasuryCents);
        Assert.Equal(EconomyCatalog.StartingFundsCents + 700, (await api.GetEconomyAsync(Key, leader)).Value!.BalanceCents);
        Assert.Equal("no_money", (await api.CrewMoneyAsync(Key, false, new CrewMoneyRequest(leader, 0, Req()))).Value!.ErrorCode);
    }

    [Fact]
    public async Task Los_miembros_invitan_solo_si_el_jefe_lo_permite()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var (leader, _, member) = await NewCrew(api);
        await api.ExpandCrewAsync(Key, new ExpandCrewRequest(leader)); // da igual si no alcanza: solo probamos el permiso
        var outsider = await NewCharacter(api);

        Assert.Equal("not_allowed", (await api.InviteToCrewAsync(Key, new InviteToCrewRequest(member, outsider))).Value!.ErrorCode);
        await api.SetCrewSettingsAsync(Key, new CrewSettingsRequest(leader, CrewPermissions.Default with { MemberCanInvite = true }));
        var invite = (await api.InviteToCrewAsync(Key, new InviteToCrewRequest(member, outsider))).Value!;
        Assert.NotEqual("not_allowed", invite.ErrorCode); // puede fallar por cupo lleno (3/3), pero no por permiso
    }

    [Fact]
    public async Task El_aporte_automatico_lleva_una_parte_del_contrato_a_la_caja()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var (leader, _, _) = await NewCrew(api);
        await api.SetCrewSettingsAsync(Key, new CrewSettingsRequest(leader, CrewPermissions.Default with { TithePercent = 10 }));

        var template = EconomyCatalog.SparePartsDelivery;
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(leader, template.Code, Req()))).Value!.Snapshot.ActiveContract(template.Code)!;
        await api.BuyAsync(Key, new BuyItemRequest(leader, EconomyCatalog.DepositNpc, template.ItemCode, 1, Req()));
        var done = (await api.DeliverAsync(Key, new DeliverContractRequest(leader, contract.Id, Req()))).Value!;
        Assert.True(done.Ok, done.Message);

        var tithe = template.RewardCents / 10;
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.SparePartsPriceCents + template.RewardCents - tithe, done.Snapshot.BalanceCents);
        var status = (await api.GetCrewStatusAsync(Key, leader)).Value!;
        Assert.Equal(tithe, status.TreasuryCents);
        Assert.Equal("tithe", status.History![0].Kind);

        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var total = db.CreateCommand("SELECT sum(balance_cents) FROM distrito.economic_accounts");
        Assert.Equal(0m, (decimal)(await total.ExecuteScalarAsync())!);
    }
}
