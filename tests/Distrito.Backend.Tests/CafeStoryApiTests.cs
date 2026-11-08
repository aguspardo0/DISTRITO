using Distrito.Shared.Api;
using Distrito.Shared.Economy;

namespace Distrito.Backend.Tests;

/// <summary>Misiones del café: Don Aldo se las da solo a quien tiene puesto; cada capítulo lo paga la familia que lo encarga.</summary>
[Trait("Category", "Database")]
public class CafeStoryApiTests
{
    private const string Key = TestBackend.ServerKey;

    private static async Task<Guid> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        return (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
    }

    private static EncargoView? CafeOffer(EconomySnapshot s) =>
        s.Encargos?.FirstOrDefault(e => e.Story && e.GiverNpc == EconomyCatalog.CoffeeCornerNpc && e.IsOffer);

    [Fact]
    public async Task Sin_puesto_no_hay_mision_y_con_puesto_Don_Aldo_ofrece_el_primer_capitulo()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await NewCharacter(api);
        Assert.Null(CafeOffer((await api.GetEconomyAsync(Key, me)).Value!));

        var license = (await api.BusinessActionAsync(Key, "license", new BusinessActionRequest(me, Guid.NewGuid().ToString()))).Value!;
        Assert.True(license.Ok, license.Message);
        var offer = CafeOffer((await api.GetEconomyAsync(Key, me)).Value!);
        Assert.NotNull(offer);
        Assert.Equal(FamilyStories.Cafe.Steps[0].Title, offer!.Title);
        Assert.Equal(EconomyCatalog.Moretti, offer.Family); // el primer capítulo lo pagan los Moretti

        // Se toma con Don Aldo (no con el capo) y no cuenta para el límite de encargos comunes.
        var wrong = (await api.EncargoActionAsync(Key, new EncargoActionRequest(me, offer.Id, "aceptar", FamilyPeople.BossOf(EconomyCatalog.Moretti), Guid.NewGuid().ToString()))).Value!;
        Assert.False(wrong.Ok);
        var taken = (await api.EncargoActionAsync(Key, new EncargoActionRequest(me, offer.Id, "aceptar", EconomyCatalog.CoffeeCornerNpc, Guid.NewGuid().ToString()))).Value!;
        Assert.True(taken.Ok, taken.Message);
        Assert.Contains("Don Aldo", taken.Message);
        Assert.Contains(taken.Snapshot.ActiveEncargos, e => e.Story && e.Title == FamilyStories.Cafe.Steps[0].Title);
    }
}
