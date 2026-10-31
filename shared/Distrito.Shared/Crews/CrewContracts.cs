using System.Text.RegularExpressions;

namespace Distrito.Shared.Crews;

public sealed record CrewMemberView(Guid CharacterId, string Name, string Role);

public sealed record CrewInvitationView(Guid CrewId, string CrewName, string InvitedBy);

/// <summary>Entrada del historial de la cuadrilla (altas, bajas, cambios de mando).</summary>
public sealed record CrewEventView(DateTimeOffset When, string Kind, string? Who, string Detail);

/// <summary>Situación de un personaje respecto de las cuadrillas.</summary>
public sealed record CrewStatus(
    Guid? CrewId,
    string? CrewName,
    bool IsLeader,
    List<CrewMemberView> Members,
    List<CrewInvitationView> Invitations,
    int Level = 1,
    List<CrewEventView>? History = null)
{
    public static CrewStatus None { get; } = new(null, null, false, [], []);
    public bool InCrew => CrewId is not null;
    public int Capacity => CrewRules.CapacityForLevel(Level);

    /// <summary>Con menos de <see cref="CrewRules.MembersToForm"/> integrantes, la cuadrilla está "en formación".</summary>
    public bool IsFormed => Members.Count >= CrewRules.MembersToForm;
}

/// <summary>Resultado de una acción sobre cuadrillas: siempre trae el estado actualizado del que la pidió.</summary>
public sealed record CrewActionResult(bool Ok, string? ErrorCode, string Message, CrewStatus Status);

// --- Pedidos del servidor de juego al backend (API interna) ---

public sealed record CreateCrewRequest(Guid CharacterId, string Name);

public sealed record InviteToCrewRequest(Guid CharacterId, Guid TargetCharacterId);

public sealed record CrewInvitationResponse(Guid CharacterId, Guid CrewId, bool Accept);

public sealed record LeaveCrewRequest(Guid CharacterId);

/// <summary>Reglas provisionales de cuadrillas (ver ADR-004). No son diseño de juego definitivo.</summary>
public static partial class CrewRules
{
    /// <summary>Integrantes necesarios para que la cuadrilla deje de estar "en formación" (documento de diseño).</summary>
    public const int MembersToForm = 3;

    /// <summary>Cupo máximo absoluto (nivel más alto). El cupo real depende del nivel.</summary>
    public const int MaxMembers = 5;

    /// <summary>Días sin entrar al juego tras los cuales el jefe pierde el mando.</summary>
    public const int LeaderInactivityDays = 14;

    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Cupo por nivel (Hoja de ruta v0.2.1): empieza en 3 y se amplía a 4 y luego a 5.
    /// Los costos y requisitos de cada ampliación están pendientes de definición.
    /// </summary>
    public static int CapacityForLevel(int level) => level switch
    {
        <= 1 => 3,
        2 => 4,
        _ => 5,
    };

    /// <summary>Texto legible de un evento del historial.</summary>
    public static string Describe(CrewEventView e) => e.Kind switch
    {
        "founded" => $"{e.Who} fundó la cuadrilla",
        "joined" => $"{e.Who} se unió",
        "left" => $"{e.Who} se fue",
        "leadership_leader_left" => "El jefe se fue: manda el miembro más antiguo",
        "leadership_inactivity" => $"{e.Who} quedó a cargo por inactividad del jefe",
        _ => $"{e.Kind} {e.Who} {e.Detail}".Trim(),
    };

    public static string? ValidateName(string? name) =>
        name is not null && name == string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)) && NamePattern().IsMatch(name)
            ? null
            : "El nombre de la cuadrilla debe tener entre 3 y 24 letras, números o espacios simples.";

    public static string Normalize(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    [GeneratedRegex(@"^[\p{L}\d][\p{L}\d ]{1,22}[\p{L}\d]$")]
    private static partial Regex NamePattern();
}
