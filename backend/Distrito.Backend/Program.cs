using Distrito.Backend.Accounts;
using Distrito.Backend.Api;
using Distrito.Backend.Characters;
using Distrito.Backend.Economy;
using Distrito.Backend.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Se resuelve al crear el servicio (no al arrancar) para que tests y entornos puedan sobrescribirla.
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Distrito")
        ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Distrito'.");
    return NpgsqlDataSource.Create(connectionString);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new PasswordHasher(
    sp.GetRequiredService<IConfiguration>().GetValue("Auth:PasswordIterations", PasswordHasher.DefaultIterations)));
builder.Services.AddSingleton<DatabaseStatus>();
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<CharacterStore>();
builder.Services.AddSingleton<EconomyService>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    var path = app.Configuration["Database:MigrationsPath"] ?? Path.Combine(AppContext.BaseDirectory, "migrations");
    await app.Services.GetRequiredService<MigrationRunner>().MigrateAsync(path);
}

// Vivo: el proceso responde. No toca la base.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

// Listo: el proceso puede atender, incluida la base de datos.
app.MapGet("/health/ready", async (DatabaseStatus db, CancellationToken ct) =>
{
    var result = await db.CheckAsync(ct);
    return result.IsReachable
        ? Results.Ok(new { status = "ok", database = "ok", schemaVersion = result.SchemaVersion })
        : Results.Json(new { status = "unavailable", database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapDistritoApi();

app.Run();

public partial class Program;
