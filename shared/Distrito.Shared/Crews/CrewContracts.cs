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
    List<CrewEventView>? History = null,
    int Contracts = 0,
    int Reputation = 0,
    long TreasuryCents = 0,
    CrewPermissions? Permissions = null)
{
    public CrewPermissions Rules => Permissions ?? CrewPermissions.Default;
    public bool CanWithdraw => MyRole is { } r && Rules.CanWithdraw(r);

    public static CrewStatus None { get; } = new(null, null, false, [], []);

    /// <summary>Rol del que consulta (según la lista de miembros), o null si no está en la cuadrilla.</summary>
    public string? MyRole { get; init; }

    public bool CanInvite => MyRole is { } r && Rules.CanInvite(r);
    public bool InCrew => CrewId is not null;
    public int Capacity => CrewRules.CapacityForLevel(Level);

    /// <summary>Lo que pide la próxima ampliación (null si ya está en el cupo máximo).</summary>
    public CrewExpansion? NextExpansion => CrewRules.ExpansionFor(Level);

    /// <summary>Contratos y reputación alcanzan para ampliar (solo el jefe puede hacerlo).</summary>
    public bool CanExpand => NextExpansion is { } next && Contracts >= next.Contracts && Reputation >= next.Reputation;

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

/// <summary>
/// Permisos configurables por el jefe y aporte automático a la caja (propuesta aprobada, 2026-10-31).
/// El jefe siempre puede todo.
/// </summary>
public sealed record CrewPermissions(bool SecondCanWithdraw, bool SecondCanInvite, bool MemberCanInvite, int TithePercent)
{
    public const int MaxTithePercent = 20;
    public static CrewPermissions Default { get; } = new(false, true, false, 0);

    public bool CanInvite(string role) => role == CrewRoles.Leader || (role == CrewRoles.Second && SecondCanInvite) || (role == CrewRoles.Member && MemberCanInvite);
    public bool CanWithdraw(string role) => role == CrewRoles.Leader || (role == CrewRoles.Second && SecondCanWithdraw);

    /// <summary>Parte de un cobro de contrato que va sola a la caja.</summary>
    public long TitheOf(long cents) => cents * Math.Clamp(TithePercent, 0, MaxTithePercent) / 100;
}

/// <summary>Depositar en la caja o retirar de ella. AmountCents 0 = todo. RequestId hace el pedido idempotente.</summary>
public sealed record CrewMoneyRequest(Guid CharacterId, long AmountCents, string RequestId);

/// <summary>El jefe cambia permisos y el aporte automático.</summary>
public sealed record CrewSettingsRequest(Guid CharacterId, CrewPermissions Permissions);

/// <summary>El jefe amplía el cupo de la cuadrilla cuando se cumplen los requisitos.</summary>
public sealed record ExpandCrewRequest(Guid CharacterId);

/// <summary>Requisitos para pasar al nivel siguiente: contratos cumplidos y reputación sumados entre los integrantes.</summary>
public sealed record CrewExpansion(int ToLevel, int Capacity, int Contracts, int Reputation);

/// <summary>El jefe expulsa a un integrante.</summary>
public sealed record KickFromCrewRequest(Guid CharacterId, Guid TargetCharacterId);

/// <summary>El jefe cambia el rol de un integrante ("second" o "member").</summary>
public sealed record SetCrewRoleRequest(Guid CharacterId, Guid TargetCharacterId, string Role);

/// <summary>Roles de cuadrilla (decisión del equipo): jefe, segundo y miembro.</summary>
public static class CrewRoles
{
    public const string Leader = "leader";
    public const string Second = "second";
    public const string Member = "member";

    public static bool CanInvite(string role) => role is Leader or Second;
    public static bool CanManage(string role) => role == Leader; // expulsar y nombrar/quitar segundo

    public static string Display(string role) => role switch
    {
        Leader => "Jefe",
        Second => "Segundo",
        _ => "Miembro",
    };
}

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
    /// Requisitos de cada ampliación: <see cref="ExpansionFor"/>.
    /// </summary>
    public static int CapacityForLevel(int level) => level switch
    {
        <= 1 => 3,
        2 => 4,
        _ => 5,
    };

    /// <summary>
    /// Ampliación (decisión del equipo: por reputación y contratos cumplidos, sin dinero).
    /// Se cuentan los contratos cumplidos y la reputación total (todas las familias) de los integrantes actuales.
    /// Valores iniciales, para ajustar con pruebas: cupo 4 con 5 contratos y 30 de reputación; cupo 5 con 15 y 100.
    /// </summary>
    public static CrewExpansion? ExpansionFor(int currentLevel) => currentLevel switch
    {
        <= 1 => new CrewExpansion(2, CapacityForLevel(2), Contracts: 5, Reputation: 30),
        2 => new CrewExpansion(3, CapacityForLevel(3), Contracts: 15, Reputation: 100),
        _ => null,
    };

    /// <summary>Texto legible de un evento del historial.</summary>
    public static string Describe(CrewEventView e) => e.Kind switch
    {
        "founded" => $"{e.Who} fundó la cuadrilla",
        "joined" => $"{e.Who} se unió",
        "left" => $"{e.Who} se fue",
        "leadership_leader_left" => "El jefe se fue: manda el miembro más antiguo",
        "leadership_inactivity" => $"{e.Who} quedó a cargo por inactividad del jefe",
        "role_second" => $"{e.Who} fue nombrado segundo",
        "role_member" => $"{e.Who} dejó de ser segundo",
        "kicked" => $"{e.Who} fue expulsado",
        "expanded" => $"La cuadrilla creció: cupo {e.Detail}",
        "deposit" => $"{e.Who} puso {e.Detail} en la caja",
        "withdraw" => $"{e.Who} sacó {e.Detail} de la caja",
        "tithe" => $"{e.Who} aportó {e.Detail} de un contrato",
        "settings" => $"{e.Who} cambió los permisos: {e.Detail}",
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
