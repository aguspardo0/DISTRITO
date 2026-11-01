using Distrito.Shared.Economy;

namespace Distrito.Shared.Crews;

/// <summary>Una familia de jugadores: su Don y sus subfacciones (las cuadrillas que la forman).</summary>
public sealed record PlayerFamilyView(Guid Id, string Name, string DonName, List<string> Subfactions, DateTimeOffset FoundedAt);

/// <summary>Lo que ve un jugador: cuántas hay (de 5), si se pueden fundar y qué le falta a su cuadrilla.</summary>
public sealed record PlayerFamiliesView(bool FoundingEnabled, int Max, List<PlayerFamilyView> Families, string? MyFamily, List<string> Missing)
{
    public bool Full => Families.Count >= Max;
    public bool CanFound => FoundingEnabled && !Full && MyFamily is null && Missing.Count == 0;
}

public sealed record FoundFamilyRequest(Guid CharacterId, string Name, string RequestId);

/// <summary>
/// Familias de jugadores y subfacciones (Etapa E). Decisión del equipo (2026-11-01): como máximo 5 en el servidor, para
/// que sean pocas y valoradas, y por ahora no se pueden fundar (config/familias.json). Lo que no se definió se tomó de lo
/// recomendado: una cuadrilla funda la familia y las otras se suman como subfacciones (cada una conserva su caja, sede y
/// misiones; su jefe es Caporegime); fundar pide prestigio rango 5, el jefe nivel 10 y $1.000 de la caja; rangos del
/// diseño (Don, Consigliere, Sottocapo, Caporegime, Miembro, Asociado). La lealtad a una familia NPC y los permisos
/// configurables quedan para cuando se habilite la fundación.
/// </summary>
public static class PlayerFamilyRules
{
    public const int HardMax = 5;
    public const int CrewRank = 5;
    public const int DonLevel = 10;
    public const long CostCents = 100_000;

    public static readonly IReadOnlyList<string> Ranks = ["Don", "Consigliere", "Sottocapo", "Caporegime", "Miembro", "Asociado"];

    /// <summary>Lo que le falta a una cuadrilla para fundar (vacío si cumple todo).</summary>
    public static List<string> Missing(bool inCrew, bool isLeader, int crewRank, int leaderLevel, long boxCents)
    {
        var missing = new List<string>();
        if (!inCrew) { missing.Add("estar en una cuadrilla"); return missing; }
        if (!isLeader) missing.Add("ser el jefe de la cuadrilla");
        if (crewRank < CrewRank) missing.Add($"prestigio de rango {CrewRank} (tienen rango {crewRank})");
        if (leaderLevel < DonLevel) missing.Add($"nivel {DonLevel} del jefe (nivel {leaderLevel})");
        if (boxCents < CostCents) missing.Add($"{EconomyCatalog.FormatMoney(CostCents)} en la caja (hay {EconomyCatalog.FormatMoney(boxCents)})");
        return missing;
    }

    public static string? NameProblem(string name)
    {
        var n = name.Trim();
        if (n.Length is < 3 or > 30) return "El nombre de la familia va de 3 a 30 letras.";
        return n.All(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '\'' or '-') ? null : "El nombre solo puede tener letras, números, espacios, apóstrofos y guiones.";
    }
}
