namespace Distrito.Shared.Economy;

/// <summary>Acuerdo de una familia esta temporada. Mine: lo tiene tu cuadrilla o tu coalición.</summary>
public sealed record FamilyPactView(string FamilyCode, string HolderCrew, bool Mine);

public sealed record PactRequest(Guid CharacterId, string FamilyCode, string RequestId);

/// <summary>Acuerdos con las familias (propuesta aprobada, 2026-10-31).</summary>
public static class PactRules
{
    public const int RequiredReputation = 50;
    public const long CostCents = 5_000;
    public const int ContractBonusPercent = 5;
    public const int DailyInfluence = 2;

    public static long WithBonus(long cents) => cents * (100 + ContractBonusPercent) / 100;
}

public sealed record ZoneView(string Code, string Name, string? ControllerCrew, int ControllerPoints, int MyCrewPoints);

/// <summary>Territorio de la temporada actual, visto por un personaje.</summary>
public sealed record TerritoryView(int Season, DateTimeOffset SeasonEndsAt, List<ZoneView> Zones)
{
    public ZoneView? Zone(string code) => Zones.FirstOrDefault(z => z.Code == code);
}

/// <summary>
/// Territorio (Etapa E, propuesta aprobada por el equipo, 2026-10-31). Sin combate.
/// Cuatro zonas; las cuadrillas suman influencia con contratos entregados (+3) y ciclos con ventas de negocios (+1)
/// en la zona, con tope de 30 por día y zona. Controla la que más tiene, con 20 o más: sus integrantes cobran +5 %
/// en los contratos que se entregan ahí. Temporadas de 4 semanas; al terminar, los ganadores quedan en el historial.
/// </summary>
public static class TerritoryRules
{
    public const string Warehouse = "calle-almacen";
    public const string Workshops = "los-talleres";
    public const string Office = "la-oficina";

    /// <summary>Los Muelles: zona libre (no es territorio en disputa; ver config/zonas.json).</summary>
    public const string Docks = "los-muelles";
    public const string Corner = "la-esquina";

    /// <summary>
    /// Barrios de las familias (pedido del equipo, 2026-11-01): cada familia en el suyo, separados y rivales.
    /// No son territorio en disputa entre cuadrillas: son de la familia. Las avenidas cuentan como parte del barrio.
    /// </summary>
    public const string BarrioMoretti = "barrio-moretti";
    public const string BarrioValenti = "barrio-valenti";
    public const string BarrioCaruso = "barrio-caruso";

    public static readonly IReadOnlyList<(string Code, string Name, string Family)> Barrios =
    [
        (BarrioMoretti, "Barrio Moretti", EconomyCatalog.Moretti),
        (BarrioValenti, "Barrio Valenti", EconomyCatalog.Valenti),
        (BarrioCaruso, "Barrio Caruso", EconomyCatalog.Caruso),
    ];

    /// <summary>Zonas válidas para las reglas de combate: las cuatro del Mercado, Los Muelles y los barrios.</summary>
    public static bool IsKnownZone(string code) => code == Docks || Zones.Any(z => z.Code == code) || Barrios.Any(b => b.Code == code);

    public const int ContractPoints = 3;
    public const int SalesCyclePoints = 1;
    public const int DailyCapPerZone = 30;
    public const int ControlThreshold = 20;
    public const int ControlBonusPercent = 5;
    public static readonly TimeSpan SeasonLength = TimeSpan.FromDays(28);

    /// <summary>Comienzo de la temporada 1: lunes 26/10/2026 a las 0 h de Argentina.</summary>
    public static readonly DateTimeOffset Epoch = new(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan ArgentinaOffset = TimeSpan.FromHours(-3);

    public static readonly IReadOnlyList<(string Code, string Name)> Zones =
    [
        (Warehouse, "Calle del Almacén"), (Workshops, "Los Talleres"), (Office, "La Oficina"), (Corner, "La Esquina"),
    ];

    public static string NameOf(string zone) => zone == Docks ? "Los Muelles" : zone == "sede" ? "la sede de la cuadrilla"
        : Barrios.FirstOrDefault(b => b.Code == zone).Name ?? Zones.FirstOrDefault(z => z.Code == zone).Name ?? zone;

    public static string? ZoneOfNpc(string npcCode) => npcCode switch
    {
        EconomyCatalog.DepositNpc => Warehouse,
        EconomyCatalog.WorkshopNpc => Workshops,
        EconomyCatalog.ValentiOfficeNpc => Office,
        EconomyCatalog.CoffeeCornerNpc or EconomyCatalog.JazzClubNpc or CasinoRules.Npc => Corner,
        EconomyCatalog.MorettiBossNpc => BarrioMoretti,
        EconomyCatalog.ValentiBossNpc => BarrioValenti,
        EconomyCatalog.CarusoBossNpc => BarrioCaruso,
        _ when FamilyPeople.Find(npcCode) is { } contact => Barrios.First(b => b.Family == contact.Family).Code,
        _ => null,
    };

    public static int SeasonAt(DateTimeOffset when) => 1 + (int)Math.Floor((when - Epoch) / SeasonLength);
    public static DateTimeOffset SeasonEnd(int season) => Epoch + SeasonLength * season;
    public static DateTimeOffset SeasonStart(int season) => Epoch + SeasonLength * (season - 1);

    /// <summary>Comienzo del día (hora de Argentina) que contiene a <paramref name="when"/>, en UTC.</summary>
    public static DateTimeOffset DayStart(DateTimeOffset when)
    {
        var local = when.ToOffset(ArgentinaOffset);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, ArgentinaOffset).ToUniversalTime();
    }

    /// <summary>Cuántos puntos se pueden sumar hoy sin pasar el tope.</summary>
    public static int Grantable(int requested, int alreadyToday) => Math.Clamp(DailyCapPerZone - alreadyToday, 0, requested);

    /// <summary>Quién controla: la cuadrilla con más puntos, si llega al umbral (en empate, la que llegó primero).</summary>
    public static (Guid Crew, int Points)? Controller(IEnumerable<(Guid Crew, int Points, DateTimeOffset LastGain)> standings) =>
        standings.Where(s => s.Points >= ControlThreshold)
            .OrderByDescending(s => s.Points).ThenBy(s => s.LastGain)
            .Select(s => ((Guid, int)?)(s.Crew, s.Points)).FirstOrDefault();

    public static long WithControlBonus(long cents) => cents * (100 + ControlBonusPercent) / 100;
}
