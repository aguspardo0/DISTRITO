using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Eventos de distrito leídos de un archivo propio de la prueba, con reloj fijo durante el evento.</summary>
[Trait("Category", "Database")]
public class DistrictEventsApiTests
{
    private const string Key = TestBackend.ServerKey;

    [Fact]
    public async Task Durante_el_evento_cada_contrato_da_la_influencia_del_evento_con_su_tope()
    {
        // Evento de prueba: todos los días 10:00–11:00 (Argentina) en Los Talleres, +12 por contrato, tope 20.
        var file = Path.Combine(Path.GetTempPath(), $"eventos-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, """
            { "eventos": [ { "codigo": "prueba", "nombre": "Evento de prueba", "zona": "los-talleres", "dias": ["todos"],
                             "inicio": "10:00", "minutos": 60, "puntosPorContrato": 12, "topePorCuadrilla": 20 } ] }
            """);
        var now = new DateTimeOffset(2096, 8, 3, 13, 20, 0, TimeSpan.Zero); // 10:20 en Argentina
        await using var factory = TestBackend.Create(clock: new TestBackend.FixedClock(now),
            settings: new Dictionary<string, string> { ["Events:File"] = file });
        var api = TestBackend.Api(factory);

        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.CreateCrewAsync(Key, new CreateCrewRequest(id, "Banda " + Guid.NewGuid().ToString("N")[..10]));

        var snapshot = (await api.GetEconomyAsync(Key, id)).Value!;
        var e = Assert.Single(snapshot.Events!, x => x.IsActive(now));
        Assert.Equal("Evento de prueba", e.Name);

        var t = EconomyCatalog.SparePartsDelivery;
        for (var i = 0; i < 2; i++)
        {
            var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, t.Code, Guid.NewGuid().ToString()))).Value!.Snapshot.ActiveContract(t.Code)!;
            await api.BuyAsync(Key, new BuyItemRequest(id, EconomyCatalog.DepositNpc, t.ItemCode, 1, Guid.NewGuid().ToString()));
            Assert.True((await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Guid.NewGuid().ToString()))).Value!.Ok);
        }
        // 12 + 8 (tope 20), en vez de 3 + 3.
        var points = (await api.GetEconomyAsync(Key, id)).Value!.Territory!.Zone(TerritoryRules.Workshops)!.MyCrewPoints;
        Assert.Equal(20, points);
        File.Delete(file);
    }
}
