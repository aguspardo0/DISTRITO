using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Distrito.Shared.Progression;

namespace Distrito.Backend.Tests;

[Trait("Category", "Database")]
public class SkillApiTests
{
    private const string Key = TestBackend.ServerKey;

    [Fact]
    public async Task Aprender_con_puntos_y_aplicar_regateo()
    {
        var user = TestBackend.UniqueUser("hb");
        await using var factory = TestBackend.Create(settings: new Dictionary<string, string> { ["Admin:Accounts"] = user });
        var api = TestBackend.Api(factory);
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var me = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.GetEconomyAsync(Key, me);
        Assert.Equal("cannot_learn", (await api.LearnSkillAsync(Key, new LearnSkillRequest(me, SkillTree.Haggling, Guid.NewGuid().ToString()))).Value!.ErrorCode);

        await api.AdminGrantAsync(Key, new AdminGrantRequest(me, 100_000, null, 0, 3, Guid.NewGuid().ToString())); // nivel 3: 2 puntos
        Assert.True((await api.LearnSkillAsync(Key, new LearnSkillRequest(me, SkillTree.Haggling, Guid.NewGuid().ToString()))).Value!.Ok);
        var second = (await api.LearnSkillAsync(Key, new LearnSkillRequest(me, SkillTree.Haggling, Guid.NewGuid().ToString()))).Value!;
        Assert.True(second.Ok, second.Message);
        Assert.Equal(2, second.Snapshot.Skills![SkillTree.Haggling]);
        Assert.Equal("cannot_learn", (await api.LearnSkillAsync(Key, new LearnSkillRequest(me, SkillTree.Salesman, Guid.NewGuid().ToString()))).Value!.ErrorCode); // sin puntos

        // Regateo 2: 10 % menos en Moretti.
        var before = second.Snapshot.BalanceCents;
        var buy = (await api.BuyAsync(Key, new BuyItemRequest(me, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 10, Guid.NewGuid().ToString()))).Value!;
        Assert.True(buy.Ok, buy.Message);
        Assert.True(before - buy.Snapshot.BalanceCents <= 10 * 800 * 110 / 100 * 90 / 100);
    }
}
