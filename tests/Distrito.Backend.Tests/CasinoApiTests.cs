using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Casino: límites de apuesta, la plata va y viene de la caja de Valenti, blackjack con estado.</summary>
[Trait("Category", "Database")]
public class CasinoApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.GetEconomyAsync(Key, id);
        return id;
    }

    [Fact]
    public async Task Ruleta_y_dados_mueven_la_apuesta_y_el_premio()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await NewCharacter(api);
        Assert.Equal("bet_limits", (await api.RouletteAsync(Key, new RouletteRequest(me, 50, "rojo", 0, Req()))).Value!.Message is var m && m.Contains("$1") ? "bet_limits" : m);
        Assert.False((await api.RouletteAsync(Key, new RouletteRequest(me, 10_000, "rojo", 0, Req()))).Value!.Ok); // más de $50 sin VIP

        var spin = (await api.RouletteAsync(Key, new RouletteRequest(me, 100, "negro", 0, Req()))).Value!;
        Assert.True(spin.Ok, spin.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - 100 + spin.PaidCents, spin.BalanceCents);
        Assert.Contains(spin.PaidCents, new long[] { 0, 200 });

        var dice = (await api.DiceAsync(Key, new DiceRequest(me, 100, Req()))).Value!;
        Assert.True(dice.Ok, dice.Message);
        Assert.Equal(spin.BalanceCents - 100 + dice.PaidCents, dice.BalanceCents);
    }

    [Fact]
    public async Task Blackjack_se_juega_hasta_terminar()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await NewCharacter(api);
        var deal = (await api.BlackjackAsync(Key, new BlackjackRequest(me, "repartir", 500, Req()))).Value!;
        Assert.True(deal.Ok, deal.Message);
        Assert.NotNull(deal.Hand);
        var hand = deal.Hand!;
        if (hand.Status == "jugando")
        {
            Assert.True(hand.DealerHidden);
            Assert.Equal("hand_open", (await api.BlackjackAsync(Key, new BlackjackRequest(me, "repartir", 500, Req()))).Value!.Message is { } msg && msg.Contains("terminala") ? "hand_open" : "x");
            var stand = (await api.BlackjackAsync(Key, new BlackjackRequest(me, "plantarse", 0, Req()))).Value!;
            Assert.True(stand.Ok, stand.Message);
            hand = stand.Hand!;
            Assert.NotEqual("jugando", hand.Status);
            Assert.True(CasinoRules.HandValue(hand.Dealer) >= 17 || CasinoRules.HandValue(hand.Player) > 21);
        }
        var balance = (await api.GetEconomyAsync(Key, me)).Value!.BalanceCents;
        var expected = EconomyCatalog.StartingFundsCents - 500 + hand.Status switch { "ganada" => 1000, "blackjack" => 1250, "empate" => 500, _ => 0 };
        Assert.Equal(expected, balance);
    }
}
