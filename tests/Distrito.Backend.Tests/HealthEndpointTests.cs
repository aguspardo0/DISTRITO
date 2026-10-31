using System.Net;
using System.Text.Json;
using Distrito.Backend.Persistence;

namespace Distrito.Backend.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Live_responde_ok_sin_necesitar_base_de_datos()
    {
        await using var factory = TestBackend.Create(TestBackend.UnreachableDb, migrate: false);
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_devuelve_503_si_la_base_no_responde()
    {
        await using var factory = TestBackend.Create(TestBackend.UnreachableDb, migrate: false);
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>
    /// Requiere PostgreSQL. En CI siempre corre.
    /// Localmente: "docker compose up -d", o excluirlo con --filter "Category!=Database".
    /// </summary>
    [Fact]
    [Trait("Category", "Database")]
    public async Task Ready_informa_la_ultima_migracion_aplicada()
    {
        await using var factory = TestBackend.Create();
        var response = await factory.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Esperaba 200, llegó {(int)response.StatusCode}: {body}");
        var latest = MigrationRunner.Discover(TestBackend.MigrationsPath).Max(m => m.Version);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(latest, json.RootElement.GetProperty("schemaVersion").GetInt32());
    }
}
