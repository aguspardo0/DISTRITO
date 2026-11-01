using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Familias de jugadores: máximo 5, cerradas por ahora (config del repo) y la fundación cuando se habilite.</summary>
[Trait("Category", "Database")]
public class PlayerFamilyApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> Character(DistritoApiClient api, string user)
    {
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    [Fact]
    public async Task Por_ahora_no_se_pueden_fundar_y_son_como_mucho_cinco()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await Character(api, TestBackend.UniqueUser("fa"));
        var families = (await api.GetCrewBoardAsync(Key, me)).Value!.Families!;
        Assert.False(families.FoundingEnabled);
        Assert.Equal(PlayerFamilyRules.HardMax, families.Max);
        Assert.False(families.CanFound);
        var refused = (await api.FoundFamilyAsync(Key, new FoundFamilyRequest(me, "Los Nuevos", Req()))).Value!;
        Assert.Equal("not_enabled", refused.ErrorCode);
    }

    [Fact]
    public async Task Cuando_se_habilite_pide_requisitos_y_cobra_la_fundacion()
    {
        var user = TestBackend.UniqueUser("fb");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string>
        {
            ["Families:FoundingEnabled"] = "true",
            ["Admin:Accounts"] = user,
        });
        var api = TestBackend.Api(factory);
        var don = await Character(api, user);
        await api.CreateCrewAsync(Key, new CreateCrewRequest(don, "Fam " + Guid.NewGuid().ToString("N")[..10]));
        var missing = (await api.FoundFamilyAsync(Key, new FoundFamilyRequest(don, "Familia " + Guid.NewGuid().ToString("N")[..8], Req()))).Value!;
        Assert.Equal("requirements", missing.ErrorCode);

        // Rango 5, nivel 10 y $1.000 en la caja.
        Assert.True((await api.AdminToolAsync(Key, new AdminToolRequest(don, "prestigio", PrestigeRules.ForRank(PlayerFamilyRules.CrewRank), Req()))).Value!.Ok);
        Assert.True((await api.AdminGrantAsync(Key, new AdminGrantRequest(don, PlayerFamilyRules.CostCents, null, 0, PlayerFamilyRules.DonLevel, Req()))).Value!.Ok);
        await api.CrewMoneyAsync(Key, true, new CrewMoneyRequest(don, PlayerFamilyRules.CostCents, Req()));
        Assert.Equal("invalid_name", (await api.FoundFamilyAsync(Key, new FoundFamilyRequest(don, "X", Req()))).Value!.ErrorCode);
        var name = "Familia " + Guid.NewGuid().ToString("N")[..8];
        var founded = (await api.FoundFamilyAsync(Key, new FoundFamilyRequest(don, name, Req()))).Value!;
        Assert.True(founded.Ok, founded.Message);
        var view = (await api.GetCrewBoardAsync(Key, don)).Value!.Families!;
        Assert.Equal(name, view.MyFamily);
        Assert.Contains(view.Families, f => f.Name == name);
        Assert.Equal("already", (await api.FoundFamilyAsync(Key, new FoundFamilyRequest(don, name + "2", Req()))).Value!.ErrorCode);
    }
}
