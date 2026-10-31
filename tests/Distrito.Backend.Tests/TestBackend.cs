using Distrito.Shared.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Distrito.Backend.Tests;

/// <summary>Arranca el backend en memoria contra la base de pruebas.</summary>
public static class TestBackend
{
    public const string ServerKey = "clave-de-prueba";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DISTRITO_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=distrito;Username=distrito;Password=distrito_dev;Timeout=5";

    public const string UnreachableDb = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    public static string MigrationsPath
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "database", "migrations");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            throw new DirectoryNotFoundException("No se encontró database/migrations subiendo desde " + AppContext.BaseDirectory);
        }
    }

    public static WebApplicationFactory<Program> Create(
        string? connectionString = null, bool migrate = true, string? serverKey = ServerKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Distrito", connectionString ?? ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", migrate.ToString());
            builder.UseSetting("Database:MigrationsPath", MigrationsPath);
            builder.UseSetting("Auth:PasswordIterations", "1000"); // rápido en tests; producción usa 600.000
            builder.UseSetting("Internal:ServerKey", serverKey ?? "");
            builder.UseSetting("Economy:BusinessWorkerEnabled", "false"); // los tests corren los ciclos a mano
        });

    /// <summary>Servicio de economía conectado directo a la base de pruebas (para correr ciclos a mano).</summary>
    public static Distrito.Backend.Economy.EconomyService Economy() =>
        new(Npgsql.NpgsqlDataSource.Create(ConnectionString), TimeProvider.System,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Economy:BusinessCycleSeconds"] = "300" })
                .Build());

    public static DistritoApiClient Api(WebApplicationFactory<Program> factory) => new(factory.CreateClient());

    /// <summary>Nombre único por ejecución para no chocar con datos de otras pruebas.</summary>
    public static string UniqueUser(string prefix = "u") => prefix + Guid.NewGuid().ToString("N")[..12];

    public static string UniqueCharacterName()
    {
        // Solo letras: convierte dígitos hexadecimales a letras.
        var raw = Guid.NewGuid().ToString("N")[..10];
        return "Prueba " + new string(raw.Select(c => char.IsDigit(c) ? (char)('a' + (c - '0')) : c).ToArray());
    }
}
