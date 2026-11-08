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

/// <summary>Qué está haciendo y dónde (lo que se guarda cada tanto, no cada paso).</summary>
public sealed record ResidentState(Guid ResidentId, string Activity, string Place, double X, double Z, DateTimeOffset Since);

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
    public const string Working = "trabajando";
}
