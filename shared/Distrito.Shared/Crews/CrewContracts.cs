using System.Text.RegularExpressions;

namespace Distrito.Shared.Crews;

public sealed record CrewMemberView(Guid CharacterId, string Name, string Role);

public sealed record CrewInvitationView(Guid CrewId, string CrewName, string InvitedBy);

/// <summary>Situación de un personaje respecto de las cuadrillas.</summary>
public sealed record CrewStatus(
    Guid? CrewId,
    string? CrewName,
    bool IsLeader,
    List<CrewMemberView> Members,
    List<CrewInvitationView> Invitations)
{
    public static CrewStatus None { get; } = new(null, null, false, [], []);
    public bool InCrew => CrewId is not null;
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
    public const int MaxMembers = 8;
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(10);

    public static string? ValidateName(string? name) =>
        name is not null && name == string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)) && NamePattern().IsMatch(name)
            ? null
            : "El nombre de la cuadrilla debe tener entre 3 y 24 letras, números o espacios simples.";

    public static string Normalize(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    [GeneratedRegex(@"^[\p{L}\d][\p{L}\d ]{1,22}[\p{L}\d]$")]
    private static partial Regex NamePattern();
}
