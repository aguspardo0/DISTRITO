using Distrito.Shared.Life;

namespace Distrito.Backend.Tests;

/// <summary>Vida de los NPC, etapa 1: el backend crea los 30 habitantes una sola vez y guarda su estado y su historial.</summary>
[Trait("Category", "Database")]
public class LifeApiTests
{
    private const string Key = TestBackend.ServerKey;

    [Fact]
    public async Task Crea_los_habitantes_una_vez_y_guarda_estado_e_historial()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);

        var first = (await api.GetResidentsAsync(Key)).Value!;
        var again = (await api.GetResidentsAsync(Key)).Value!;
        Assert.Equal(LifeConfig.Parse(first.ConfigJson).Population, first.Residents.Count);
        Assert.Equal(first.Residents.Select(r => r.Identity.Id).Order(), again.Residents.Select(r => r.Identity.Id).Order());
        Assert.Contains("profesiones", first.ConfigJson);
        Assert.All(first.Residents, r => Assert.NotNull(r.State));

        var someone = first.Residents.First(r => r.Identity.Workplace is not null).Identity;
        var now = DateTimeOffset.UtcNow;
        var state = new ResidentState(someone.Id, LifeActivities.Working, someone.Workplace!, 1.5, -2.5, now);
        Assert.True((await api.SaveResidentsAsync(Key, new LifeStateBatch([state],
            [new ResidentEvent(someone.Id, now, "prueba", "Abrió su puesto más temprano.")]))).IsSuccess);

        var saved = (await api.GetResidentsAsync(Key)).Value!.Residents.Single(r => r.Identity.Id == someone.Id).State!;
        Assert.Equal(LifeActivities.Working, saved.Activity);
        Assert.Equal(1.5, saved.X);

        var history = (await api.GetResidentHistoryAsync(Key, someone.Id)).Value!;
        Assert.Contains(history, e => e.Kind == "prueba");
        Assert.Contains(history, e => e.Kind == "llego" && e.Text.Contains(someone.FirstName));
    }

    [Fact]
    public async Task Sin_clave_de_servidor_no_se_ven_los_habitantes()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        Assert.False((await api.GetResidentsAsync("otra-clave")).IsSuccess);
    }
}
