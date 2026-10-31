using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Distrito.Backend.Tests;

public class HealthEndpointTests
{
    private const string UnreachableDb =
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Distrito", connectionString));

    [Fact]
    public async Task Live_responde_ok_sin_necesitar_base_de_datos()
    {
        await using var factory = CreateFactory(UnreachableDb);
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_devuelve_503_si_la_base_no_responde()
    {
        await using var factory = CreateFactory(UnreachableDb);
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>
    /// Requiere PostgreSQL con database/init aplicado. En CI siempre corre.
    /// Localmente: "docker compose up -d", o excluirlo con --filter "Category!=Database".
    /// </summary>
    [Fact]
    [Trait("Category", "Database")]
    public async Task Ready_devuelve_ok_y_version_de_esquema_con_base_disponible()
    {
        var connectionString = Environment.GetEnvironmentVariable("DISTRITO_TEST_DB")
            ?? "Host=localhost;Port=5432;Database=distrito;Username=distrito;Password=distrito_dev;Timeout=5";

        await using var factory = CreateFactory(connectionString);
        var response = await factory.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Esperaba 200, llegó {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
    }
}
