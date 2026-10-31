namespace Distrito.Shared.Progression;

/// <summary>Un oficio con su práctica acumulada.</summary>
public sealed record SkillView(string Code, string Name, int Practice)
{
    public int Level => ProgressionRules.SkillLevel(Practice);
    public int PracticeForNext => ProgressionRules.PracticeForSkillLevel(Level + 1);
}

/// <summary>Progresión de un personaje: experiencia y oficios.</summary>
public sealed record ProgressView(long Xp, int Logistics, int Negotiation, int Administration)
{
    public static ProgressView Empty { get; } = new(0, 0, 0, 0);

    public int Level => ProgressionRules.LevelForXp(Xp);
    public long XpForNext => ProgressionRules.XpForLevel(Level + 1);
    public int LogisticsLevel => ProgressionRules.SkillLevel(Logistics);
    public int NegotiationLevel => ProgressionRules.SkillLevel(Negotiation);
    public int AdministrationLevel => ProgressionRules.SkillLevel(Administration);

    public IReadOnlyList<SkillView> Skills =>
    [
        new(ProgressionRules.LogisticsCode, "Logística", Logistics),
        new(ProgressionRules.NegotiationCode, "Negociación", Negotiation),
        new(ProgressionRules.AdministrationCode, "Administración", Administration),
    ];
}

/// <summary>
/// Reglas de progresión (propuesta aprobada por el equipo, 2026-10-31).
/// Nivel 1–30: pasar del nivel n al n+1 cuesta 100 × n de experiencia.
/// Oficios 1–10: pasar del nivel n al n+1 cuesta 5 × n prácticas (nivel 2 con 5, nivel 10 con 225 acumuladas).
/// </summary>
public static class ProgressionRules
{
    public const int MaxLevel = 30;
    public const int MaxSkillLevel = 10;

    public const long XpPerContract = 100;
    public const long XpPerSalesCycle = 10;

    public const string LogisticsCode = "logistica";
    public const string NegotiationCode = "negociacion";
    public const string AdministrationCode = "administracion";

    /// <summary>Experiencia acumulada necesaria para llegar a un nivel (nivel 1 = 0).</summary>
    public static long XpForLevel(int level) => level <= 1 ? 0 : 50L * (level - 1) * level;

    public static int LevelForXp(long xp)
    {
        var level = 1;
        while (level < MaxLevel && xp >= XpForLevel(level + 1))
            level++;
        return level;
    }

    public static int PracticeForSkillLevel(int level) => level <= 1 ? 0 : 5 * (level - 1) * level / 2;

    public static int SkillLevel(int practice)
    {
        var level = 1;
        while (level < MaxSkillLevel && practice >= PracticeForSkillLevel(level + 1))
            level++;
        return level;
    }

    // ---------- Ventajas (cambios de economía aprobados) ----------

    /// <summary>Negociación: +1 % del pago de un contrato por nivel por encima de 1. Lo paga quien paga el contrato.</summary>
    public static long WithNegotiationBonus(long cents, int negotiationLevel) =>
        cents * (100 + Math.Max(0, negotiationLevel - 1)) / 100;

    /// <summary>Logística: +10 s de plazo en contratos con plazo por nivel por encima de 1.</summary>
    public static int ExtraDeadlineSeconds(int logisticsLevel) => 10 * Math.Max(0, logisticsLevel - 1);

    /// <summary>Administración: el puesto vende 1 más por ciclo desde el nivel 5 y 2 más desde el 10.</summary>
    public static int ExtraSalesPerCycle(int administrationLevel) => administrationLevel >= 10 ? 2 : administrationLevel >= 5 ? 1 : 0;
}
