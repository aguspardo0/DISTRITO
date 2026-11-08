using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Pago de los turnos de empleado en la Esquina del café: topes, una sola vez por turno y desde la clientela.</summary>
[Trait("Category", "Database")]
public class CoffeeShiftApiTests
{
    private const string Key = TestBackend.ServerKey;

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    [Fact]
    public async Task Cobra_el_turno_una_sola_vez_y_respeta_los_topes()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await NewCharacter(api);
        var before = (await api.GetEconomyAsync(Key, me)).Value!.BalanceCents;

        var id = Guid.NewGuid().ToString();
        var paid = (await api.CoffeeShiftPayAsync(Key, new CoffeeShiftPayRequest(me, id, 300, 80, 13))).Value!;
        Assert.True(paid.Ok, paid.Message);
        Assert.Contains("$3", paid.Message);
        Assert.Equal(before + 380, paid.Snapshot.BalanceCents);

        // El mismo turno no se cobra dos veces.
        var again = (await api.CoffeeShiftPayAsync(Key, new CoffeeShiftPayRequest(me, id, 300, 80, 13))).Value!;
        Assert.Equal(before + 380, again.Snapshot.BalanceCents);

        // Más de lo que paga el turno, no.
        var greedy = (await api.CoffeeShiftPayAsync(Key, new CoffeeShiftPayRequest(me, Guid.NewGuid().ToString(), 5_000, 80, 13))).Value!;
        Assert.False(greedy.Ok);
        var tips = (await api.CoffeeShiftPayAsync(Key, new CoffeeShiftPayRequest(me, Guid.NewGuid().ToString(), 300, 500, 13))).Value!;
        Assert.False(tips.Ok);
        var nothing = (await api.CoffeeShiftPayAsync(Key, new CoffeeShiftPayRequest(me, Guid.NewGuid().ToString(), 0, 0, 3))).Value!;
        Assert.False(nothing.Ok);
        Assert.Equal(before + 380, (await api.GetEconomyAsync(Key, me)).Value!.BalanceCents);
    }
}
