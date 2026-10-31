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
builder.Services.AddSingleton<DatabaseStatus>();

var app = builder.Build();

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

app.Run();

public partial class Program;
