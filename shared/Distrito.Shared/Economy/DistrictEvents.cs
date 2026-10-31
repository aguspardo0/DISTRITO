using System.Globalization;
using System.Text.Json;

namespace Distrito.Shared.Economy;

/// <summary>Un evento de distrito tal como está escrito en config/eventos.json.</summary>
public sealed record DistrictEvent(
    string Codigo, string Nombre, string? Descripcion, string Zona, List<string> Dias,
    string Inicio, int Minutos, int PuntosPorContrato, int TopePorCuadrilla);

/// <summary>Una vez concreta de un evento (fecha y hora). OccurrenceId identifica esa vez (para su tope).</summary>
public sealed record EventView(string Code, string Name, string? Description, string Zone, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, int PointsPerContract, int CapPerCrew)
{
    public string OccurrenceId => $"{Code}:{StartsAt.ToOffset(TerritoryRules.ArgentinaOffset):yyyy-MM-dd}";
    public bool IsActive(DateTimeOffset now) => now >= StartsAt && now < EndsAt;
    public string ZoneName => TerritoryRules.NameOf(Zone);
}

/// <summary>
/// Eventos de distrito (Etapa E, propuesta aprobada por el equipo, 2026-10-31): programados, editables en
/// config/eventos.json. Durante un evento, cada contrato entregado en su zona da más influencia, con tope propio por
/// cuadrilla y evento (no cuenta para el tope diario).
/// </summary>
public static class DistrictEvents
{
    public static readonly TimeSpan AnnounceBefore = TimeSpan.FromHours(1);

    private static readonly string[] DayNames = ["domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado"];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private sealed record File(List<DistrictEvent>? Eventos);

    /// <summary>Lee y valida el archivo. Si hay errores, los devuelve (y la lista queda vacía).</summary>
    public static (List<DistrictEvent> Events, List<string> Errors) Parse(string json)
    {
        var errors = new List<string>();
        List<DistrictEvent> events;
        try
        {
            events = JsonSerializer.Deserialize<File>(json, Json)?.Eventos ?? [];
        }
        catch (JsonException ex)
        {
            return ([], [$"El archivo no es JSON válido: {ex.Message}"]);
        }

        var codes = new HashSet<string>();
        foreach (var e in events)
        {
            var who = string.IsNullOrWhiteSpace(e.Codigo) ? "(sin código)" : e.Codigo;
            if (string.IsNullOrWhiteSpace(e.Codigo) || e.Codigo.Contains(' ') || e.Codigo.Contains(':'))
                errors.Add($"{who}: el código no puede estar vacío ni tener espacios o ':'.");
            else if (!codes.Add(e.Codigo))
                errors.Add($"{who}: código repetido.");
            if (string.IsNullOrWhiteSpace(e.Nombre))
                errors.Add($"{who}: falta el nombre.");
            if (TerritoryRules.Zones.All(z => z.Code != e.Zona))
                errors.Add($"{who}: zona '{e.Zona}' no existe (usá {string.Join(", ", TerritoryRules.Zones.Select(z => z.Code))}).");
            if (e.Dias is null || e.Dias.Count == 0 || e.Dias.Any(d => d != "todos" && !DayNames.Contains(d)))
                errors.Add($"{who}: días inválidos (usá lunes, martes, miercoles, jueves, viernes, sabado, domingo o todos).");
            if (!TimeOnly.TryParseExact(e.Inicio, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                errors.Add($"{who}: la hora de inicio tiene que ser HH:MM (por ejemplo 21:00).");
            if (e.Minutos is < 5 or > 24 * 60)
                errors.Add($"{who}: los minutos van de 5 a 1440.");
            if (e.PuntosPorContrato is < 1 or > 100)
                errors.Add($"{who}: puntosPorContrato va de 1 a 100.");
            if (e.TopePorCuadrilla < e.PuntosPorContrato || e.TopePorCuadrilla > 500)
                errors.Add($"{who}: topePorCuadrilla tiene que ser al menos puntosPorContrato y como mucho 500.");
        }
        return errors.Count == 0 ? (events, errors) : ([], errors);
    }

    /// <summary>Las veces de cada evento que terminan después de <paramref name="from"/> y empiezan antes de <paramref name="until"/>.</summary>
    public static List<EventView> Occurrences(IEnumerable<DistrictEvent> events, DateTimeOffset from, DateTimeOffset until)
    {
        var list = new List<EventView>();
        var firstDay = from.ToOffset(TerritoryRules.ArgentinaOffset).Date.AddDays(-1);
        var lastDay = until.ToOffset(TerritoryRules.ArgentinaOffset).Date;
        foreach (var e in events)
        {
            var start = TimeOnly.ParseExact(e.Inicio, "HH:mm", CultureInfo.InvariantCulture);
            for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
            {
                if (!e.Dias.Contains("todos") && !e.Dias.Contains(DayNames[(int)day.DayOfWeek]))
                    continue;
                var startsAt = new DateTimeOffset(day.Add(start.ToTimeSpan()), TerritoryRules.ArgentinaOffset).ToUniversalTime();
                var endsAt = startsAt.AddMinutes(e.Minutos);
                if (endsAt > from && startsAt < until)
                    list.Add(new EventView(e.Codigo, e.Nombre, e.Descripcion, e.Zona, startsAt, endsAt, e.PuntosPorContrato, e.TopePorCuadrilla));
            }
        }
        return list.OrderBy(o => o.StartsAt).ToList();
    }

    /// <summary>Texto para el anuncio de arriba de la pantalla (null si no hay nada que anunciar).</summary>
    public static string? Banner(IEnumerable<EventView> events, DateTimeOffset now)
    {
        foreach (var e in events.OrderBy(e => e.StartsAt))
        {
            if (e.IsActive(now))
                return $"EVENTO: {e.Name} — termina en {Math.Ceiling((e.EndsAt - now).TotalMinutes)} min. Contratos en {e.ZoneName}: +{e.PointsPerContract} de influencia.";
            if (e.StartsAt > now && e.StartsAt - now <= AnnounceBefore)
                return $"Próximo evento: {e.Name} en {Math.Ceiling((e.StartsAt - now).TotalMinutes)} min ({e.ZoneName}).";
        }
        return null;
    }
}
