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
        EconomyCatalog.DepositNpc or EconomyCatalog.MorettiBossNpc => EconomyCatalog.Moretti,
        EconomyCatalog.WorkshopNpc or EconomyCatalog.CarusoBossNpc => EconomyCatalog.Caruso,
        EconomyCatalog.ValentiOfficeNpc or CasinoRules.Npc or EconomyCatalog.ValentiBossNpc => EconomyCatalog.Valenti,
        _ => FamilyPeople.Find(npcCode)?.Family,
    };
}

/// <summary>Una persona de confianza de una familia: da los encargos y es el camino para llegar al capo.</summary>
public sealed record FamilyContact(string Code, string Name, string Family, string Role, string Greeting);

/// <summary>
/// Hablar con el capo es exclusivo (pedido del equipo, 2026-11-01): primero se trabaja con su gente, que está en el
/// mapa, en el barrio de la familia. El capo recibe solo a quien tiene reputación suficiente con la familia.
/// </summary>
public static class FamilyPeople
{
    public const int BossReputation = 50;

    public static readonly IReadOnlyList<FamilyContact> Contacts =
    [
        new("npc:moretti-tano", "Tano Ricci", EconomyCatalog.Moretti, "calle",
            "Tano Ricci se acomoda la gorra. «Acá nadie entra sin que yo lo sepa. ¿Querés trabajar? Empezá por abajo, como todos.»"),
        new("npc:moretti-sofia", "Sofía Moretti", EconomyCatalog.Moretti, "negocios",
            "Sofía, la sobrina de Don Moretti, lleva las cuentas de la familia. «Mi tío confía en la gente que cumple. Yo anoto quién cumple.»"),
        new("npc:valenti-lupo", "Lupo", EconomyCatalog.Valenti, "calle",
            "Lupo, el chofer de los Valenti, no saca la vista de la calle. «El Palazzo no es para curiosos. Si servís, te vas a enterar.»"),
        new("npc:valenti-vittorio", "Vittorio, el contador", EconomyCatalog.Valenti, "negocios",
            "Vittorio ajusta los anteojos. «Todo en esta familia tiene un número. El suyo, por ahora, es bajo.»"),
        new("npc:caruso-beppe", "Beppe, el capataz", EconomyCatalog.Caruso, "calle",
            "Beppe se limpia la grasa de las manos. «Acá se gana el lugar sudando. Los de traje que vayan a otro barrio.»"),
        new("npc:caruso-rosa", "Rosa, la de la cantina", EconomyCatalog.Caruso, "negocios",
            "Rosa sirve un café sin preguntar. «Todo lo que pasa en el barrio pasa primero por mi cantina. Y lo que le interesa a Don Caruso, también.»"),
    ];

    public static FamilyContact? Find(string npcCode) => Contacts.FirstOrDefault(c => c.Code == npcCode);

    public static string BossOf(string family) => family switch
    {
        EconomyCatalog.Moretti => EconomyCatalog.MorettiBossNpc,
        EconomyCatalog.Valenti => EconomyCatalog.ValentiBossNpc,
        _ => EconomyCatalog.CarusoBossNpc,
    };

    public static bool CanMeetBoss(int reputation) => reputation >= BossReputation;
}
