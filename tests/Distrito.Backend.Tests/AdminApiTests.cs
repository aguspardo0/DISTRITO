using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Panel de administración: solo cuentas admin; plata, objetos y nivel quedan en el libro.</summary>
[Trait("Category", "Database")]
public class AdminApiTests
{
    private const string Key = TestBackend.ServerKey;

    private static async Task<(Guid Id, string User)> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.GetEconomyAsync(Key, id);
        return (id, user);
    }

    [Fact]
    public async Task Solo_la_cuenta_admin_puede_darse_cosas()
    {
        var adminUser = TestBackend.UniqueUser("adm");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = $"otro, {adminUser.ToUpperInvariant()}" });
        var api = TestBackend.Api(factory);
        await api.RegisterAsync(adminUser, "contraseña-larga-1");
        var token = (await api.LoginAsync(adminUser, "contraseña-larga-1")).Value!.Token;
        var admin = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        var (player, _) = await NewCharacter(api);

        Assert.False((await api.GetAdminAsync(Key, player)).Value!.IsAdmin);
        Assert.Equal("not_admin", (await api.AdminGrantAsync(Key, new AdminGrantRequest(player, 100_000, null, 0, null, Guid.NewGuid().ToString()))).Value!.ErrorCode);

        var view = (await api.GetAdminAsync(Key, admin)).Value!;
        Assert.True(view.IsAdmin);
        Assert.Contains(view.Items, i => i.ItemCode == "thompson");

        var grant = (await api.AdminGrantAsync(Key, new AdminGrantRequest(admin, 100_000, "thompson", 1, 8, Guid.NewGuid().ToString()))).Value!;
        Assert.True(grant.Ok, grant.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents + 100_000, grant.Snapshot.BalanceCents);
        Assert.Equal(1, grant.Snapshot.QuantityOf("thompson"));
        Assert.Equal(8, grant.Snapshot.ProgressOrEmpty.Level);
        Assert.Equal("invalid_request", (await api.AdminGrantAsync(Key, new AdminGrantRequest(admin, 0, null, 0, 99, Guid.NewGuid().ToString()))).Value!.ErrorCode);
    }
}

[Trait("Category", "Database")]
public class AdminToolApiTests
{
    private const string Key = TestBackend.ServerKey;

    [Fact]
    public async Task Prestigio_y_tablero_nuevo_solo_para_admin()
    {
        var user = TestBackend.UniqueUser("at");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = user });
        var api = TestBackend.Api(factory);
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var me = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.CreateCrewAsync(Key, new Distrito.Shared.Crews.CreateCrewRequest(me, "Admin " + Guid.NewGuid().ToString("N")[..10]));

        var before = (await api.GetCrewBoardAsync(Key, me)).Value!;
        var fame = (await api.AdminToolAsync(Key, new AdminToolRequest(me, "prestigio", 1500, Guid.NewGuid().ToString()))).Value!;
        Assert.True(fame.Ok, fame.Message);
        var after = (await api.GetCrewBoardAsync(Key, me)).Value!;
        Assert.Equal(6, after.Rank);

        Assert.True((await api.AdminToolAsync(Key, new AdminToolRequest(me, "tablero", 1, Guid.NewGuid().ToString()))).Value!.Ok);
        var rerolled = (await api.GetCrewBoardAsync(Key, me)).Value!;
        Assert.DoesNotContain(rerolled.Missions, m => before.Missions.Any(b => b.Id == m.Id));
        Assert.True((await api.AdminToolAsync(Key, new AdminToolRequest(me, "ciclos", 1, Guid.NewGuid().ToString()))).Value!.Ok);

        // Alguien que no es admin no puede.
        var other = TestBackend.UniqueUser("no");
        await api.RegisterAsync(other, "contraseña-larga-1");
        var t2 = (await api.LoginAsync(other, "contraseña-larga-1")).Value!.Token;
        var nobody = (await api.CreateCharacterAsync(t2, TestBackend.UniqueCharacterName())).Value!.Id;
        Assert.Equal("not_admin", (await api.AdminToolAsync(Key, new AdminToolRequest(nobody, "prestigio", 100, Guid.NewGuid().ToString()))).Value!.ErrorCode);
    }
}
