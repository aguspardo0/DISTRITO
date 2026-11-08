using System.Text.Json;
using System.Text.Json.Serialization;

namespace Distrito.Shared.Life;

public sealed class LifeDistrict
{
    [JsonPropertyName("codigo")] public string Code { get; set; } = "";
    [JsonPropertyName("nombre")] public string Name { get; set; } = "";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
    [JsonPropertyName("radio")] public double Radius { get; set; } = 10;
}

public sealed class LifePlace
{
    [JsonPropertyName("codigo")] public string Code { get; set; } = "";
    [JsonPropertyName("nombre")] public string Name { get; set; } = "";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
    /// <summary>Lugar cerrado: al llegar, el habitante entra y deja de verse (hasta que sale).</summary>
    [JsonPropertyName("adentro")] public bool Indoor { get; set; }
}

public sealed class LifeProfession
{
    [JsonPropertyName("codigo")] public string Code { get; set; } = "";
    [JsonPropertyName("nombre")] public string Name { get; set; } = "";
    [JsonPropertyName("cantidad")] public int Count { get; set; }
    [JsonPropertyName("lugares")] public List<string> Places { get; set; } = new();
    /// <summary>Turno en horas de Bellmont (desde = hasta: sin horario fijo).</summary>
    [JsonPropertyName("desde")] public int ShiftFrom { get; set; }
    [JsonPropertyName("hasta")] public int ShiftTo { get; set; }
    /// <summary>Cada cuántos de esta profesión, uno trabaja de noche (el turno al revés). 0 = nadie.</summary>
    [JsonPropertyName("turnoNocheCada")] public int NightShiftEvery { get; set; }
    [JsonPropertyName("edadMin")] public int AgeMin { get; set; } = 18;
    [JsonPropertyName("edadMax")] public int AgeMax { get; set; } = 70;
    [JsonPropertyName("plataMin")] public long MoneyMin { get; set; }
    [JsonPropertyName("plataMax")] public long MoneyMax { get; set; }
    /// <summary>Lo que cobra al terminar el turno (o, sin turno, al levantarse: jubilación, changas). Solo estado del habitante.</summary>
    [JsonPropertyName("ingreso")] public long Income { get; set; }
}

/// <summary>Cuánto gasta un habitante en cada cosa (solo su estado: no es plata del libro de cuentas).</summary>
public sealed class LifeCosts
{
    [JsonPropertyName("comida")] public long Meal { get; set; } = 80;
    [JsonPropertyName("compras")] public long Shopping { get; set; } = 150;
    [JsonPropertyName("salida")] public long Outing { get; set; } = 50;
}

public sealed class LifeNames
{
    [JsonPropertyName("hombres")] public List<string> Men { get; set; } = new();
    [JsonPropertyName("mujeres")] public List<string> Women { get; set; } = new();
    [JsonPropertyName("apellidos")] public List<string> Surnames { get; set; } = new();
}

/// <summary>Vida autónoma de los NPC (config/vida.json). Etapa 1: identidad y estado guardado.</summary>
public sealed class LifeConfig
{
    [JsonPropertyName("semilla")] public int Seed { get; set; } = 1952;
    [JsonPropertyName("rasgos")] public List<string> Traits { get; set; } = new();
    [JsonPropertyName("opuestos")] public List<List<string>> Opposites { get; set; } = new();
    [JsonPropertyName("barrios")] public List<LifeDistrict> Districts { get; set; } = new();
    [JsonPropertyName("lugares")] public List<LifePlace> Places { get; set; } = new();
    [JsonPropertyName("profesiones")] public List<LifeProfession> Professions { get; set; } = new();
    [JsonPropertyName("nombres")] public LifeNames Names { get; set; } = new();
    [JsonPropertyName("gastos")] public LifeCosts Costs { get; set; } = new();
    /// <summary>Dónde se come, se compra y se sale (códigos de <see cref="Places"/>).</summary>
    [JsonPropertyName("paraComer")] public List<string> EatPlaces { get; set; } = new();
    [JsonPropertyName("paraComprar")] public List<string> ShopPlaces { get; set; } = new();
    [JsonPropertyName("paraSalir")] public List<string> SocialPlaces { get; set; } = new();

    public LifePlace? Place(string code) => Places.FirstOrDefault(p => p.Code == code);

    public int Population => Professions.Sum(p => p.Count);

    public static LifeConfig Parse(string json) =>
        JsonSerializer.Deserialize<LifeConfig>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
        ?? throw new ArgumentException("config/vida.json vacío");

    /// <summary>Errores claros al editar mal el archivo.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Traits.Count == 0) errors.Add("Falta al menos un rasgo de personalidad.");
        foreach (var pair in Opposites)
            if (pair.Count != 2 || !Traits.Contains(pair[0]) || !Traits.Contains(pair[1]))
                errors.Add($"opuestos: cada par tiene dos rasgos de la lista ({string.Join(", ", pair)}).");
        if (Districts.Count == 0) errors.Add("Falta al menos un barrio donde vivir.");
        if (Districts.Select(d => d.Code).Distinct().Count() != Districts.Count) errors.Add("Hay barrios repetidos.");
        if (Places.Select(p => p.Code).Distinct().Count() != Places.Count) errors.Add("Hay lugares repetidos.");
        foreach (var p in Professions)
        {
            if (p.Count < 0) errors.Add($"profesión {p.Code}: la cantidad no puede ser negativa.");
            foreach (var place in p.Places.Where(code => Places.All(l => l.Code != code)))
                errors.Add($"profesión {p.Code}: el lugar «{place}» no está en la lista de lugares.");
            if (p.ShiftFrom is < 0 or > 23 || p.ShiftTo is < 0 or > 23) errors.Add($"profesión {p.Code}: las horas van de 0 a 23.");
            if (p.AgeMin > p.AgeMax || p.MoneyMin > p.MoneyMax) errors.Add($"profesión {p.Code}: mínimo mayor que máximo.");
        }
        if (Professions.Select(p => p.Code).Distinct().Count() != Professions.Count) errors.Add("Hay profesiones repetidas.");
        if (Names.Men.Count == 0 || Names.Women.Count == 0 || Names.Surnames.Count == 0) errors.Add("Faltan nombres o apellidos.");
        foreach (var (list, name) in new[] { (EatPlaces, "paraComer"), (ShopPlaces, "paraComprar"), (SocialPlaces, "paraSalir") })
        {
            if (list.Count == 0) errors.Add($"{name}: falta al menos un lugar.");
            foreach (var code in list.Where(c => Places.All(l => l.Code != c)))
                errors.Add($"{name}: el lugar «{code}» no está en la lista de lugares.");
        }
        return errors;
    }
}

/// <summary>Quién es un habitante: no cambia (o cambia poco y queda en el historial).</summary>
public sealed record Resident(
    Guid Id,
    string FirstName,
    string LastName,
    bool Woman,
    int Age,
    string Profession,
    string District,
    double HomeX,
    double HomeZ,
    string? Workplace,
    int ShiftFrom,
    int ShiftTo,
    Dictionary<string, double> Traits,
    long PocketMoneyCents)
{
    public string FullName => $"{FirstName} {LastName}";

    public bool HasShift => ShiftFrom != ShiftTo;

    /// <summary>¿Le toca trabajar a esta hora de Bellmont?</summary>
    public bool OnShift(int hour) => HasShift && (ShiftFrom < ShiftTo ? hour >= ShiftFrom && hour < ShiftTo : hour >= ShiftFrom || hour < ShiftTo);

    public double Trait(string code) => Traits.TryGetValue(code, out var v) ? v : 0.5;
}

/// <summary>
/// Qué está haciendo y dónde (lo que se guarda cada tanto, no cada paso). Si está yendo a algún lado, salió de
/// (FromX, FromZ) en <c>Since</c>, llega a (X, Z) en <c>ArrivesAt</c> y ahí hace <c>Activity</c> hasta <c>Until</c>.
/// Necesidades de 0 (nada) a 1 (urgente).
/// </summary>
public sealed record ResidentState(Guid ResidentId, string Activity, string Place, double X, double Z, DateTimeOffset Since,
    DateTimeOffset ArrivesAt = default, DateTimeOffset Until = default, double FromX = 0, double FromZ = 0,
    double Hunger = 0.2, double Fatigue = 0.2, double Social = 0.3, long? Money = null,
    string? Decision = null, string? Reason = null, long Decisions = 0, DateTimeOffset NeedsAt = default,
    double[]? Path = null, bool Indoor = false)
{
    public bool Travelling(DateTimeOffset now) => now < ArrivesAt;

    /// <summary>Puntos del camino: la salida, las esquinas (<see cref="Path"/>: x, z, x, z…) y el destino.</summary>
    public List<(double X, double Z)> Waypoints()
    {
        var points = new List<(double X, double Z)> { (FromX, FromZ) };
        if (Path is { } p)
            for (var i = 0; i + 1 < p.Length; i += 2)
                points.Add((p[i], p[i + 1]));
        points.Add((X, Z));
        return points;
    }

    /// <summary>Largo del camino en metros.</summary>
    public static double Length(IReadOnlyList<(double X, double Z)> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
            total += Math.Sqrt((points[i].X - points[i - 1].X) * (points[i].X - points[i - 1].X) + (points[i].Z - points[i - 1].Z) * (points[i].Z - points[i - 1].Z));
        return total;
    }

    /// <summary>Dónde está ahora (si va caminando, en algún punto del camino, a velocidad pareja).</summary>
    public (double X, double Z) PositionAt(DateTimeOffset now)
    {
        if (!Travelling(now) || ArrivesAt <= Since)
            return (X, Z);
        var t = Math.Clamp((now - Since).TotalSeconds / (ArrivesAt - Since).TotalSeconds, 0, 1);
        var points = Waypoints();
        var left = Length(points) * t;
        for (var i = 1; i < points.Count; i++)
        {
            var (ax, az) = points[i - 1];
            var (bx, bz) = points[i];
            var seg = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            if (left <= seg && seg > 0)
                return (ax + (bx - ax) * left / seg, az + (bz - az) * left / seg);
            left -= seg;
        }
        return (X, Z);
    }

    /// <summary>¿Se ve en la calle? No si ya llegó a un lugar cerrado (su casa, un local).</summary>
    public bool Visible(DateTimeOffset now) => Travelling(now) || !Indoor;
}

/// <summary>Un hecho importante de su vida (no cada movimiento).</summary>
public sealed record ResidentEvent(Guid ResidentId, DateTimeOffset At, string Kind, string Text);

/// <summary>Un habitante como lo guarda el backend: quién es y qué hacía la última vez.</summary>
public sealed record ResidentRecord(Resident Identity, ResidentState? State);

/// <summary>Lo que el backend le manda al servidor del juego al arrancar.</summary>
public sealed record LifeSnapshot(string ConfigJson, List<ResidentRecord> Residents);

public sealed record LifeStateBatch(List<ResidentState> States, List<ResidentEvent> Events);

public static class LifeActivities
{
    public const string AtHome = "en casa";
    public const string Sleeping = "durmiendo";
    public const string Working = "trabajando";
    public const string Patrolling = "patrullando";
    public const string Eating = "comiendo";
    public const string Shopping = "comprando";
    public const string Socializing = "charlando";
    public const string Strolling = "paseando";
}
