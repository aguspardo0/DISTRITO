namespace Distrito.Shared.Economy;

/// <summary>Ánimo de una familia NPC, igual para todos los jugadores.</summary>
public sealed record FamilyMoodView(string FamilyCode, string Name, string Mood, int ContractsDone, int ReputationLost)
{
    public string Display => FamilyRules.Display(Mood);
}

/// <summary>
/// Estados de las familias NPC (propuesta aprobada por el equipo, 2026-10-31).
/// Se calculan con lo que hicieron todos los jugadores en las 24 horas anteriores al comienzo de la hora actual,
/// así que cambian como mucho una vez por hora.
///   Cooperación: 10 o más contratos cumplidos para la familia → sus contratos pagan +10 %.
///   Rivalidad: le hicieron perder 10 o más puntos de reputación → sus contratos pagan −10 % y sus precios suben +10 %.
///   Si se dan las dos, gana rivalidad.
/// </summary>
public static class FamilyRules
{
    public const string Neutral = "neutral";
    public const string Cooperation = "cooperacion";
    public const string Rivalry = "rivalidad";

    public const int CooperationContracts = 10;
    public const int RivalryReputationLoss = 10;
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public static string Decide(int contractsDone, int reputationLost) =>
        reputationLost >= RivalryReputationLoss ? Rivalry
        : contractsDone >= CooperationContracts ? Cooperation
        : Neutral;

    /// <summary>Fin de la ventana: el comienzo de la hora actual (UTC).</summary>
    public static DateTimeOffset WindowEnd(DateTimeOffset now) => new(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);

    public static long ContractPay(long cents, string mood) => mood switch
    {
        Cooperation => cents * 110 / 100,
        Rivalry => cents * 90 / 100,
        _ => cents,
    };

    public static long SellPrice(long cents, string mood) => mood == Rivalry ? cents * 110 / 100 : cents;

    public static string Display(string mood) => mood switch
    {
        Cooperation => "cooperación (contratos +10 %)",
        Rivalry => "rivalidad (contratos −10 %, precios +10 %)",
        _ => "neutral",
    };

    /// <summary>Familia a la que pertenece cada NPC.</summary>
    public static string? FamilyOfNpc(string npcCode) => npcCode switch
    {
        EconomyCatalog.DepositNpc => EconomyCatalog.Moretti,
        EconomyCatalog.WorkshopNpc => EconomyCatalog.Caruso,
        EconomyCatalog.ValentiOfficeNpc => EconomyCatalog.Valenti,
        _ => null,
    };
}
