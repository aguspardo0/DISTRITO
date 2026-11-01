using Distrito.Shared.Economy;

namespace Distrito.Shared.Crews;

/// <summary>
/// Sede, prestigio y misiones de cuadrilla (aprobado por el equipo, 2026-11-01).
/// El cupo de integrantes (CrewRules.CapacityForLevel) no cambia: el prestigio es un rango aparte.
/// </summary>
public static class CrewBaseRules
{
    public const long PriceCents = 25_000;       // $250 de la caja
    public const long RentCents = 200;           // $2 por ciclo de negocios
    public const double DepotKg = 500;           // depósito propio

    /// <summary>Galpones de Los Muelles que se pueden comprar como sede (centro y tamaño en planta; puerta hacia la calle).</summary>
    public static readonly IReadOnlyList<(float X, float Z, float Width, float Depth)> Sheds =
    [
        (42, -20, 12, 9), (60, -20, 14, 9), (77, -21, 10, 8), (46, 19, 10, 7), (66, 20, 14, 7),
    ];

    /// <summary>Lado de la puerta: los galpones del norte (z &lt; 0) abren hacia +z (la calle); los del sur, hacia −z.</summary>
    public static float DoorSide(int shed) => Sheds[shed].Z < 0 ? 1 : -1;

    /// <summary>Punto justo adentro de la puerta (reaparición, tablero, depósito).</summary>
    public static (float X, float Z) Inside(int shed)
    {
        var s = Sheds[shed];
        return (s.X, s.Z + DoorSide(shed) * (s.Depth / 2 - 1.6f));
    }

    /// <summary>Punto en la vereda, frente a la puerta (para comprarla).</summary>
    public static (float X, float Z) Front(int shed)
    {
        var s = Sheds[shed];
        return (s.X, s.Z + DoorSide(shed) * (s.Depth / 2 + 1.5f));
    }

    public static bool IsInside(int shed, float x, float z)
    {
        var s = Sheds[shed];
        return MathF.Abs(x - s.X) <= s.Width / 2 - 0.2f && MathF.Abs(z - s.Z) <= s.Depth / 2 - 0.2f;
    }

    public static int? ShedAt(float x, float z, float slack = 0)
    {
        for (var i = 0; i < Sheds.Count; i++)
        {
            var s = Sheds[i];
            if (MathF.Abs(x - s.X) <= s.Width / 2 + slack && MathF.Abs(z - s.Z) <= s.Depth / 2 + slack)
                return i;
        }
        return null;
    }

    /// <summary>¿Está en la puerta o adentro de esa sede? (para comprar, usar el tablero y el depósito)</summary>
    public static bool AtBase(int shed, float x, float z) =>
        IsInside(shed, x, z) || Distance(Front(shed), x, z) <= 3.5f;

    private static float Distance((float X, float Z) p, float x, float z) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
}

/// <summary>Un beneficio de rango de prestigio.</summary>
public sealed record PrestigePerk(int Rank, string Text);

public static class PrestigeRules
{
    public const int MaxRank = 10;

    /// <summary>Prestigio acumulado para llegar a cada rango: 100, 300, 600, 1000… (50·r·(r−1)).</summary>
    public static int ForRank(int rank) => rank <= 1 ? 0 : 50 * rank * (rank - 1);

    public static int RankFor(int prestige)
    {
        var rank = 1;
        while (rank < MaxRank && prestige >= ForRank(rank + 1))
            rank++;
        return rank;
    }

    public static readonly IReadOnlyList<PrestigePerk> Perks =
    [
        new(2, "Depósito +100 kg"),
        new(3, "5 % menos en el Almacén Moretti"),
        new(4, "Cuarta misión en el tablero"),
        new(5, "Armería: 10 % menos en el Taller Caruso"),
        new(6, "Mesa VIP en el casino (apuestas hasta $200)"),
        new(7, "Depósito +200 kg más"),
        new(8, "Misiones con +25 % de pago"),
        new(9, "Quinta misión en el tablero"),
        new(10, "Sede de honor: cartel dorado y misiones con +50 % de pago"),
    ];

    public static double ExtraDepotKg(int rank) => (rank >= 2 ? 100 : 0) + (rank >= 7 ? 200 : 0);
    public static int MissionSlots(int rank) => 3 + (rank >= 4 ? 1 : 0) + (rank >= 9 ? 1 : 0);
    public static int MorettiDiscountPercent(int rank) => rank >= 3 ? 5 : 0;
    public static int CarusoDiscountPercent(int rank) => rank >= 5 ? 10 : 0;
    public static bool CasinoVip(int rank) => rank >= 6;
    public static int PayBonusPercent(int rank) => rank >= 10 ? 50 : rank >= 8 ? 25 : 0;
    public static bool Honor(int rank) => rank >= 10;
}

/// <summary>
/// Tipos de misión. Las «de entrega» se cumplen en un lugar (el servidor de juego valida la presencia); las «de evento»
/// suman progreso cuando pasa algo en el mundo (asaltos, guardias, vigilancia).
/// </summary>
public static class MissionKinds
{
    public const string Delivery = "entrega";       // llevar N × objeto a un NPC
    public const string Gather = "juntar";          // tener N × objeto en el depósito y entregarlo desde la sede
    public const string Collection = "cobranza";    // ir con K de la cuadrilla a cobrar a un NPC
    public const string TruckJob = "camion";        // asaltar el camión de reparto (por encargo de otra familia)
    public const string Contraband = "contrabando"; // bajar guardias del contrabando
    public const string Shop = "caja";              // asaltar la caja de un local
    public const string Guard = "vigilar";          // estar en la sede con 2 de la cuadrilla (minutos)

    public static readonly IReadOnlyList<string> All = [Delivery, Gather, Collection, TruckJob, Contraband, Shop, Guard];

    /// <summary>Se cumplen con un botón en el lugar (no por eventos del mundo).</summary>
    public static bool IsTurnIn(string kind) => kind is Delivery or Gather or Collection;
}

/// <summary>Una misión del tablero.</summary>
public sealed record CrewMissionView(
    Guid Id, string Kind, string Title, string Detail, string? ItemCode, string? NpcCode, int Target, int Progress,
    long RewardCents, int Prestige, string Status, DateTimeOffset ExpiresAt, string PayerFamily)
{
    public bool Done => Status == "cumplida";
}

/// <summary>Lo que muestra el panel: sede, prestigio y tablero.</summary>
public sealed record CrewBoardView(
    bool InCrew, string? CrewName, int Prestige, int Rank, int NextRankAt, int? Shed, long BaseDebtCents,
    List<CrewMissionView> Missions, List<int> TakenSheds, bool IsLeader, DateTimeOffset NextRefresh,
    CrewOperationView? Operation = null,
    PlayerFamiliesView? Families = null);

/// <summary>Una etapa de la operación del día, para mostrar (la actual trae su misión para cumplirla).</summary>
public sealed record CrewOperationStageView(string Title, string Detail, bool Done, bool Current);

/// <summary>
/// La operación del día de la cuadrilla: tres etapas en orden, paga al final. Status: «disponible» (se puede empezar),
/// «en curso», «cumplida» o «vencida» (se terminó el día).
/// </summary>
public sealed record CrewOperationView(string Code, string Title, string Intro, string Family, long RewardCents, int Prestige,
    string Status, List<CrewOperationStageView> Stages, CrewMissionView? Current, DateTimeOffset EndsAt, int MembersNeeded);

public sealed record CrewOperationStartRequest(Guid CharacterId, string RequestId);

public sealed record CrewBaseBuyRequest(Guid CharacterId, int Shed, string RequestId);
public sealed record CrewMissionTurnInRequest(Guid CharacterId, Guid MissionId, string RequestId, IReadOnlyList<Guid>? Companions = null);
public sealed record CrewMissionProgressRequest(Guid CharacterId, string Kind, int Amount, string RequestId, string? NpcCode = null);

/// <summary>Sedes ocupadas (para que el servidor de juego sepa zonas seguras, carteles y reapariciones).</summary>
public sealed record CrewBaseView(Guid CrewId, string CrewName, int Shed, int Rank);

/// <summary>
/// Generador de misiones: arma cada misión al azar a partir de los tipos, con una semilla fija por cuadrilla y turno
/// (así el tablero es el mismo para todos sus miembros y se puede rehacer igual). Más difíciles y mejor pagas con el rango.
/// </summary>
public static class MissionGenerator
{
    public static readonly TimeSpan Period = TimeSpan.FromHours(3);

    public static long SlotOf(DateTimeOffset now) => now.ToUnixTimeSeconds() / (long)Period.TotalSeconds;
    public static DateTimeOffset SlotEnd(long slot) => DateTimeOffset.FromUnixTimeSeconds((slot + 1) * (long)Period.TotalSeconds);

    private static readonly (string Npc, string Name, string Family, string To)[] Places =
    [
        (EconomyCatalog.DepositNpc, "el Almacén Moretti", EconomyCatalog.Moretti, "al Almacén Moretti"),
        (EconomyCatalog.WorkshopNpc, "el Taller Caruso", EconomyCatalog.Caruso, "al Taller Caruso"),
        (EconomyCatalog.ValentiOfficeNpc, "la Oficina Valenti", EconomyCatalog.Valenti, "a la Oficina Valenti"),
    ];

    private static readonly (string Item, string Name)[] Goods =
    [
        (EconomyCatalog.CoffeeBagItem, "bolsas de café"),
        (EconomyCatalog.DrinksCrateItem, "cajones de bebidas"),
        (EconomyCatalog.SparePartsItem, "cajas de repuestos"),
    ];

    public sealed record Draft(string Kind, string Title, string Detail, string? ItemCode, string? NpcCode, int Target,
        long RewardCents, int Prestige, string PayerFamily);

    public static List<Draft> Generate(Guid crewId, long slot, int rank, int salt = 0)
    {
        var seed = unchecked((crewId.GetHashCode() * 397 ^ (int)slot) + salt * 7919); // estable entre procesos (HashCode.Combine no lo es)
        var rng = new Random(seed);
        var count = PrestigeRules.MissionSlots(rank);
        var kinds = MissionKinds.All.OrderBy(_ => rng.Next()).Take(count).ToList();
        return kinds.Select(k => Make(k, rng, rank)).ToList();
    }

    private static Draft Make(string kind, Random rng, int rank)
    {
        var level = Math.Clamp(rank, 1, PrestigeRules.MaxRank);
        // Paga y prestigio crecen con el rango y la dificultad: $20–80 y 10–40 (+25 % de pago desde rango 8).
        long Pay(double difficulty) =>
            (long)Math.Round(Math.Clamp(20 + 6 * (level - 1) * difficulty + rng.Next(0, 10), 20, 80)) * 100 * (100 + PrestigeRules.PayBonusPercent(level)) / 100;
        int Fame(double difficulty) => (int)Math.Clamp(10 + 3 * (level - 1) * difficulty + rng.Next(0, 5), 10, 40);
        var place = Places[rng.Next(Places.Length)];
        var goods = Goods[rng.Next(Goods.Length)];
        switch (kind)
        {
            case MissionKinds.Delivery:
            {
                var n = 2 + level + rng.Next(0, 3);
                return new(kind, $"Entrega: {n} {goods.Name} {place.To}", $"Conseguilas y llevalas {place.To}; ahí las entregás desde el tablero (J).",
                    goods.Item, place.Npc, n, Pay(1), Fame(1), place.Family);
            }
            case MissionKinds.Gather:
            {
                var n = 4 + 2 * level + rng.Next(0, 4);
                return new(kind, $"Acopio: juntar {n} {goods.Name} en el depósito", "Tenelas guardadas en el depósito de la cuadrilla y entregalas desde la sede. Paga Valenti.",
                    goods.Item, null, n, Pay(1.2), Fame(1.1), EconomyCatalog.Valenti);
            }
            case MissionKinds.Collection:
            {
                var mates = level >= 5 ? 2 : 1;
                return new(kind, $"Cobranza en {place.Name} (con {mates} de la cuadrilla)", $"Vayan juntos a {place.Name} y cobren desde el tablero. Paga Valenti.",
                    null, place.Npc, mates, Pay(0.9), Fame(1), EconomyCatalog.Valenti);
            }
            case MissionKinds.TruckJob:
                return new(kind, "Encargo de los Caruso: asaltar el camión de reparto de Moretti", "Cuando salga el camión, que alguien de la cuadrilla lo asalte. Ojo: Moretti se enoja.",
                    null, null, 1, Pay(1.4), Fame(1.4), EconomyCatalog.Caruso);
            case MissionKinds.Contraband:
            {
                var n = level >= 6 ? 2 : 1;
                return new(kind, $"Limpiar el contrabando: bajar {n} guardia{(n > 1 ? "s" : "")} en Los Muelles", "Cuando llegue el contrabando, enfrenten a los guardias.",
                    null, null, n, Pay(1.5), Fame(1.5), EconomyCatalog.Valenti);
            }
            case MissionKinds.Shop:
            {
                var victim = place.Npc == EconomyCatalog.ValentiOfficeNpc ? Places[0] : place;
                var payer = victim.Family == EconomyCatalog.Moretti ? EconomyCatalog.Caruso : EconomyCatalog.Moretti;
                return new(kind, $"Encargo: asaltar la caja de {victim.Name}", "Que alguien de la cuadrilla le apunte al encargado hasta vaciarle la caja.",
                    null, victim.Npc, 1, Pay(1.2), Fame(1.2), payer);
            }
            default:
            {
                var minutes = 3 + level / 2;
                return new(MissionKinds.Guard, $"Vigilar la sede {minutes} minutos (con 2 de la cuadrilla)", "Estén adentro de la sede al menos dos al mismo tiempo.",
                    null, null, minutes * 2, Pay(0.8), Fame(0.9), EconomyCatalog.Valenti); // progreso en medios minutos
            }
        }
    }
}

/// <summary>
/// Operaciones de cuadrilla (propuesta aprobada por el equipo, 2026-11-01): una por día y por cuadrilla, tres etapas en
/// orden con lo que ya existe (reunirse en un lugar, juntar en el depósito, un asalto o un golpe), hacen falta al menos
/// 2 integrantes y pagan al final $120–250 a la caja y 60–120 de prestigio (más con el rango). Las encarga una familia,
/// siempre contra otra.
/// </summary>
public static class CrewOperations
{
    public const int MinMembers = 2;
    public const string Available = "disponible";
    public const string Running = "en curso";
    public const string Done = "cumplida";
    public const string Expired = "vencida";

    public sealed record Stage(string Kind, string Title, string Detail, string? Item, string? Npc, int Target);
    public sealed record Template(string Code, string Title, string Intro, string Family, IReadOnlyList<Stage> Stages);

    public static readonly IReadOnlyList<Template> Templates =
    [
        new("golpe-puerto", "El golpe del puerto",
            "Don Valenti quiere el contrabando del puerto, pero no quiere ensuciarse las manos. «Ustedes hacen el trabajo; yo pago bien y no pregunto.»",
            EconomyCatalog.Valenti,
            [
                new(MissionKinds.Collection, "Reconocimiento en Los Muelles", "Vayan dos de la cuadrilla a hablar con Gaetano, el marinero, y marquen el terreno (botón «Cumplir» estando ahí).",
                    null, EncargoRules.DocksContact, 1),
                new(MissionKinds.Gather, "Armarse", "Junten 60 balas de pistola en el depósito de la cuadrilla y entréguenlas desde la sede.", "bala-pistola", null, 60),
                new(MissionKinds.Contraband, "El golpe", "Cuando llegue el contrabando a Los Muelles (13, 19 o 23 h), bajen a los dos guardias.", null, null, 2),
            ]),
        new("ruta-camion", "La ruta del camión",
            "Don Caruso está harto de que los Moretti le cobren de más. «Ese camión pasa todos los días por el Mercado. Que un día no llegue.»",
            EconomyCatalog.Caruso,
            [
                new(MissionKinds.Collection, "Seguir al camión", "Vayan dos de la cuadrilla al Almacén Moretti, de donde sale el camión, y anoten la ruta (botón «Cumplir» estando ahí).",
                    null, EconomyCatalog.DepositNpc, 1),
                new(MissionKinds.Gather, "La coartada", "Junten 8 cajones de bebidas en el depósito: si preguntan, estaban repartiendo para una fiesta.", EconomyCatalog.DrinksCrateItem, null, 8),
                new(MissionKinds.TruckJob, "El asalto", "Asalten el camión de reparto de los Moretti en su recorrida.", null, null, 1),
            ]),
        new("mano-dura", "Mano dura con los Valenti",
            "Don Moretti no olvida una deuda. «Los Valenti se creen dueños de Bellmont con sus libros y su casino. Recuérdenles quién estaba primero.»",
            EconomyCatalog.Moretti,
            [
                new(MissionKinds.Collection, "Mostrar los dientes", "Vayan dos de la cuadrilla a la Oficina Valenti y hagan saber que están (botón «Cumplir» estando ahí).",
                    null, EconomyCatalog.ValentiOfficeNpc, 1),
                new(MissionKinds.Shop, "Fondos para la guerra", "Vacíen la caja de un local del Mercado a punta de pistola.", null, null, 1),
                new(MissionKinds.Collection, "La cobranza grande", "Vuelvan juntos a la Oficina Valenti y cobren lo que le deben a los Moretti (botón «Cumplir» estando ahí).",
                    null, EconomyCatalog.ValentiOfficeNpc, 1),
            ]),
    ];

    /// <summary>Día de la operación (hora de Argentina) y cuándo termina.</summary>
    public static int DayOf(DateTimeOffset now) => EncargoRules.DayOf(now);
    public static DateTimeOffset DayEnd(DateTimeOffset now) => EncargoRules.DayEnd(now);

    /// <summary>La operación que le toca a una cuadrilla hoy (la misma para todos sus integrantes).</summary>
    public static Template Pick(Guid crewId, int day) =>
        Templates[(int)((uint)EncargoRules.StableId(crewId, "operacion", day).GetHashCode() % (uint)Templates.Count)];

    /// <summary>Paga al terminar: $120–250 a la caja y 60–120 de prestigio, más con el rango.</summary>
    public static (long Cents, int Prestige) Reward(int rank, int luck)
    {
        var level = Math.Clamp(rank, 1, PrestigeRules.MaxRank);
        var dollars = Math.Clamp(120 + 14 * (level - 1) + Math.Clamp(luck, 0, 20), 120, 250);
        var prestige = Math.Clamp(60 + 7 * (level - 1) + Math.Clamp(luck, 0, 20) / 2, 60, 120);
        return (dollars * 100L, prestige);
    }
}
