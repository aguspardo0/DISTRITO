namespace Distrito.Shared.Economy;

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
    public const string Corner = "la-esquina";

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

    public static string NameOf(string zone) => Zones.FirstOrDefault(z => z.Code == zone).Name ?? zone;

    public static string? ZoneOfNpc(string npcCode) => npcCode switch
    {
        EconomyCatalog.DepositNpc => Warehouse,
        EconomyCatalog.WorkshopNpc => Workshops,
        EconomyCatalog.ValentiOfficeNpc => Office,
        EconomyCatalog.CoffeeCornerNpc or EconomyCatalog.JazzClubNpc => Corner,
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
