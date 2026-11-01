using System.Security.Cryptography;
using System.Text;

namespace Distrito.Shared.Economy;

/// <summary>
/// Tipos de encargo personal (propuesta aprobada por el equipo, 2026-11-01). Todos son contra NPC y usan mecánicas que ya
/// existen. Los da la gente de cada familia (FamilyPeople); las familias son rivales, así que muchos encargos son contra
/// las otras dos.
/// </summary>
public static class EncargoKinds
{
    public const string Entrega = "entrega";           // conseguir N × mercadería y llevarla a quien encarga
    public const string Contrarreloj = "contrarreloj"; // lo mismo, con tiempo límite y premio si llegás rápido
    public const string Cobro = "cobro";               // cobrarle a un local de una familia rival y traer el sobre
    public const string Paquete = "paquete";           // buscar un paquete en Los Muelles y traerlo
    public const string Asalto = "asalto";             // asaltar algo de una familia rival (camión, cobrador o caja)
    public const string Recado = "recado";             // llevar un sobre sellado de la familia a alguien del Mercado
    public const string Reparto = "reparto";           // entrega grande desde un vehículo propio o de la cuadrilla
    public const string Vigilar = "vigilar";           // vigilar un local rival unos minutos

    public static readonly IReadOnlyList<string> All = [Entrega, Contrarreloj, Cobro, Paquete, Asalto, Recado, Reparto, Vigilar];

    /// <summary>Los de calle los da la persona de la entrada del barrio; los de negocios, la de la plaza.</summary>
    public static readonly IReadOnlyList<string> Street = [Cobro, Paquete, Asalto, Vigilar];
    public static readonly IReadOnlyList<string> Business = [Entrega, Contrarreloj, Recado, Reparto];

    public static int MinLevel(string kind) => kind switch
    {
        Paquete or Contrarreloj => 2,
        Asalto or Reparto => 3, // asaltar pide un arma (pistola, nivel 3); repartir, un vehículo (nivel 3)
        _ => 1,
    };

    public static string Label(string kind) => kind switch
    {
        Entrega => "Entrega",
        Contrarreloj => "Contrarreloj",
        Cobro => "Cobro",
        Paquete => "Paquete",
        Asalto => "Asalto",
        Recado => "Recado",
        Reparto => "Reparto",
        _ => "Vigilancia",
    };

    /// <summary>Paga relativa a la base (más riesgo o más trabajo, más paga).</summary>
    public static double Weight(string kind) => kind switch
    {
        Cobro => 1.1,
        Paquete => 1.2,
        Asalto => 1.35,
        Reparto => 1.4,
        Vigilar => 0.8,
        Recado => 0.9,
        _ => 1.0,
    };

    /// <summary>Los que se cumplen con lo que pasa en el mundo (asaltos, minutos de vigilancia) y después se cobran.</summary>
    public static bool IsEvent(string kind) => kind is Asalto or Vigilar;
}

/// <summary>Estados de un encargo guardado.</summary>
public static class EncargoStatus
{
    public const string Offered = "ofrecido";   // no se guarda: se calcula (ver EncargoGenerator)
    public const string Active = "activo";
    public const string Done = "cumplido";
    public const string Expired = "vencido";
    public const string Abandoned = "abandonado";
}

/// <summary>
/// Un encargo, ofrecido o tomado. Stage: 0 = la primera parte (conseguir, cobrar, buscar, asaltar, vigilar…),
/// 1 = volver con quien lo encargó. Target es la cantidad a llevar o el progreso necesario.
/// </summary>
public sealed record EncargoView(
    Guid Id, string Kind, string GiverNpc, string Family, string Title, string Detail,
    string? ItemCode, int Quantity, string? TargetNpc, int Progress, int Target, int Stage,
    long RewardCents, int Xp, int Reputation, string Status, bool Daily,
    DateTimeOffset ExpiresAt, DateTimeOffset? Deadline = null, DateTimeOffset? FastUntil = null, bool Story = false)
{
    public bool IsActive => Status == EncargoStatus.Active;
    public bool IsOffer => Status == EncargoStatus.Offered;

    /// <summary>A quién hay que ir ahora (o dónde pasa lo que hay que hacer). Null si no es un lugar fijo.</summary>
    public string? NextNpc => !IsActive ? GiverNpc : Stage >= 1 ? GiverNpc : Kind switch
    {
        EncargoKinds.Entrega or EncargoKinds.Contrarreloj or EncargoKinds.Reparto => GiverNpc,
        EncargoKinds.Cobro or EncargoKinds.Paquete or EncargoKinds.Recado or EncargoKinds.Vigilar => TargetNpc,
        _ => EncargoRules.IsPlaceTarget(TargetNpc) ? TargetNpc : null,
    };

    /// <summary>Qué hacer ahora, en una línea (para el seguimiento en pantalla y la libreta).</summary>
    public string StepText(Func<string, string> nameOf)
    {
        if (!IsActive)
            return $"Hablá con {nameOf(GiverNpc)} para tomarlo.";
        if (Stage >= 1)
            return $"Volvé con {nameOf(GiverNpc)} para cobrar.";
        return Kind switch
        {
            EncargoKinds.Entrega or EncargoKinds.Contrarreloj =>
                $"Llevale {Quantity} × {EncargoRules.ItemName(ItemCode)} a {nameOf(GiverNpc)} (se compran en el Almacén Moretti).",
            EncargoKinds.Reparto => $"Cargá {Quantity} × {EncargoRules.ItemName(ItemCode)} en un vehículo y estacionalo junto a {nameOf(GiverNpc)}.",
            EncargoKinds.Cobro => $"Andá a {nameOf(TargetNpc!)} y cobrá la deuda.",
            EncargoKinds.Paquete => $"Buscá el paquete con {nameOf(TargetNpc!)}, en Los Muelles.",
            EncargoKinds.Recado => $"Llevale el sobre a {nameOf(TargetNpc!)}.",
            EncargoKinds.Vigilar => $"Quedate cerca de {nameOf(TargetNpc!)}: {Progress / 2.0:0.#} de {Target / 2.0:0.#} minutos.",
            _ => $"{EncargoRules.RobberyName(TargetNpc, nameOf)} ({Progress}/{Target}).",
        };
    }
}

/// <summary>Lo que muestra la libreta (L): encargos tomados, los que se ofrecen y el del día.</summary>
public sealed record EncargoBoardView(List<EncargoView> Active, List<EncargoView> Offers, EncargoView? Daily, DateTimeOffset NextRefresh, int Level);

/// <summary>Una acción con alguien de la familia o en un lugar: aceptar o hacer el paso que toca.</summary>
public sealed record EncargoActionRequest(Guid CharacterId, Guid EncargoId, string Action, string NpcCode, string RequestId,
    IReadOnlyList<Guid>? VehiclesNear = null);

public sealed record EncargoAbandonRequest(Guid CharacterId, Guid EncargoId, string RequestId);

/// <summary>Algo que pasó en el mundo (un asalto, medio minuto de vigilancia) y puede contar para un encargo.</summary>
public sealed record EncargoProgressRequest(Guid CharacterId, string Kind, int Amount, string RequestId, string? Target = null);

/// <summary>Números y reglas de los encargos (aprobados por el equipo, 2026-11-01).</summary>
public static class EncargoRules
{
    public static readonly TimeSpan Period = TimeSpan.FromHours(2);
    public const int Offers = 4;
    public const int MaxActive = 2;
    public const long MaxRewardCents = 4_500;  // tope de un encargo común: $45
    public const double DailyMultiplier = 2.5; // el del día paga ×2,5 (y da ×2,5 de experiencia)
    public const int FastBonusPercent = 25;    // contrarreloj: +25 % si se entrega en la primera mitad del tiempo
    public const int RivalPenalty = 2;         // el que paga un cobro pierde un poco de simpatía por vos
    public static readonly TimeSpan ActiveFor = TimeSpan.FromMinutes(45);
    public const float VigilRadius = 10f;      // metros alrededor del local a vigilar
    public const float VehicleRadius = 12f;    // el vehículo del reparto tiene que estar así de cerca

    // Objetos de encargo (los crea y los saca el encargo; no se venden).
    public const string CollectionEnvelope = "sobre-cobro";
    public const string SealedEnvelope = "sobre-sellado";
    public const string SealedPackage = "paquete-sellado";

    // Objetivos de asalto (además de los locales, que son el código del NPC).
    public const string Truck = "camion";
    public const string Collector = "cobrador";

    /// <summary>El marinero de Los Muelles que entrega los paquetes.</summary>
    public const string DocksContact = "npc:muelle-marinero";

    public static long SlotOf(DateTimeOffset now) => now.ToUnixTimeSeconds() / (long)Period.TotalSeconds;
    public static DateTimeOffset SlotEnd(long slot) => DateTimeOffset.FromUnixTimeSeconds((slot + 1) * (long)Period.TotalSeconds);

    /// <summary>Día del encargo diario (hora de Argentina).</summary>
    public static int DayOf(DateTimeOffset now) => (int)(TerritoryRules.DayStart(now) - TerritoryRules.Epoch).TotalDays;
    public static DateTimeOffset DayEnd(DateTimeOffset now) => TerritoryRules.DayStart(now).AddDays(1);

    public static bool IsPlaceTarget(string? target) => target is not null && target.StartsWith("npc:");

    public static string ItemName(string? item) => item switch
    {
        EconomyCatalog.CoffeeBagItem => "bolsas de café",
        EconomyCatalog.DrinksCrateItem => "cajones de bebidas",
        EconomyCatalog.SparePartsItem => "cajas de repuestos",
        CollectionEnvelope => "sobre de la cobranza",
        SealedEnvelope => "sobre sellado",
        SealedPackage => "paquete sellado",
        _ => item ?? "",
    };

    public static string RobberyName(string? target, Func<string, string> nameOf) => target switch
    {
        Truck => "Asaltá el camión de reparto de los Moretti cuando salga",
        Collector => "Asaltá al cobrador de los Valenti en su recorrida",
        { } npc => $"Vaciá la caja de {nameOf(npc)}",
        _ => "Asalto",
    };

    /// <summary>Familia dueña de cada objetivo de asalto (para no encargar asaltos a la propia familia).</summary>
    public static string VictimFamily(string target) => target switch
    {
        Truck => EconomyCatalog.Moretti,
        Collector => EconomyCatalog.Valenti,
        _ => FamilyRules.FamilyOfNpc(target) ?? "",
    };

    /// <summary>
    /// Paga de un encargo común: $8 + $1,50 por nivel + algo de azar, por el peso del tipo, con tope de $45.
    /// Experiencia 60–150 y reputación +2 a +5 con la familia que encarga.
    /// </summary>
    public static (long Cents, int Xp, int Reputation) Reward(int level, string kind, int luck)
    {
        var weight = EncargoKinds.Weight(kind);
        var dollars = (8 + 1.5 * Math.Clamp(level, 1, 30) + Math.Clamp(luck, 0, 4)) * weight;
        var cents = Math.Min(MaxRewardCents, (long)Math.Round(dollars) * 100);
        var xp = (int)Math.Clamp(Math.Round((60 + 4 * level) * weight), 60, 150);
        var rep = (int)Math.Clamp(Math.Round(2 + 1.5 * (weight - 0.8) * 2 + level / 10.0), 2, 5);
        return (cents, xp, rep);
    }

    /// <summary>Id estable de un encargo ofrecido: así no hace falta guardar las ofertas (se guardan al tomarlas).</summary>
    public static Guid StableId(Guid characterId, string scope, long index)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"encargo:{characterId}:{scope}:{index}"));
        return new Guid(bytes);
    }
}

/// <summary>
/// Genera los encargos de cada jugador: 4 por turno de 2 horas (de familias y tipos variados) y uno del día. Son
/// deterministas (misma semilla por personaje y turno), así que se pueden recalcular en cualquier momento y el id coincide.
/// </summary>
public static class EncargoGenerator
{
    public sealed record Draft(Guid Id, string Kind, string GiverNpc, string Family, string Title, string Detail,
        string? ItemCode, int Quantity, string? TargetNpc, int Target, int TimeLimitSeconds, long RewardCents, int Xp, int Reputation, bool Daily);

    private static readonly string[] Goods = [EconomyCatalog.CoffeeBagItem, EconomyCatalog.DrinksCrateItem, EconomyCatalog.SparePartsItem];

    /// <summary>Locales del Mercado de cada familia (para cobrar, vigilar o asaltar a los rivales).</summary>
    private static readonly Dictionary<string, string[]> Businesses = new()
    {
        [EconomyCatalog.Moretti] = [EconomyCatalog.DepositNpc],
        [EconomyCatalog.Caruso] = [EconomyCatalog.WorkshopNpc],
        [EconomyCatalog.Valenti] = [EconomyCatalog.ValentiOfficeNpc, CasinoRules.Npc],
    };

    /// <summary>Gente del Mercado que recibe recados (neutrales o de los rivales).</summary>
    private static readonly string[] RecadoTargets = [EconomyCatalog.CoffeeCornerNpc, EconomyCatalog.JazzClubNpc, CasinoRules.Npc, EconomyCatalog.WorkshopNpc, EconomyCatalog.DepositNpc];

    private static readonly string[] Families = [EconomyCatalog.Moretti, EconomyCatalog.Valenti, EconomyCatalog.Caruso];

    private static int Seed(Guid characterId, string scope, long index) =>
        BitConverter.ToInt32(EncargoRules.StableId(characterId, scope, index).ToByteArray(), 0);

    public static List<Draft> Generate(Guid characterId, long slot, int level)
    {
        var rng = new Random(Seed(characterId, "turno", slot));
        var unlocked = EncargoKinds.All.Where(k => EncargoKinds.MinLevel(k) <= level).ToList();
        var kinds = unlocked.OrderBy(_ => rng.Next()).Take(EncargoRules.Offers).ToList();
        // Familias variadas: arranca en una al azar y va rotando.
        var start = rng.Next(Families.Length);
        return kinds.Select((kind, i) => Make(EncargoRules.StableId(characterId, $"turno-{slot}", i), kind, Families[(start + i) % Families.Length], level, rng, daily: false)).ToList();
    }

    public static Draft Daily(Guid characterId, int day, int level)
    {
        var rng = new Random(Seed(characterId, "dia", day));
        var options = new[] { EncargoKinds.Cobro, EncargoKinds.Paquete, EncargoKinds.Entrega, EncargoKinds.Asalto, EncargoKinds.Reparto }
            .Where(k => EncargoKinds.MinLevel(k) <= level).ToList();
        var kind = options[rng.Next(options.Count)];
        var family = Families[(day % Families.Length + Families.Length) % Families.Length];
        return Make(EncargoRules.StableId(characterId, $"dia-{day}", 0), kind, family, level, rng, daily: true);
    }

    private static Draft Make(Guid id, string kind, string family, int level, Random rng, bool daily)
    {
        var role = EncargoKinds.Street.Contains(kind) ? "calle" : "negocios";
        var giver = FamilyPeople.Contacts.First(c => c.Family == family && c.Role == role);
        var rivals = Families.Where(f => f != family).ToArray();
        var rival = rivals[rng.Next(rivals.Length)];
        var rivalName = FamilyName(rival);
        var (cents, xp, rep) = EncargoRules.Reward(level, kind, rng.Next(0, 5));
        var scale = daily ? 2 : 1;
        string title, detail;
        string? item = null, target = null;
        int quantity = 0, goal = 1, limit = 0;
        switch (kind)
        {
            case EncargoKinds.Entrega:
                item = Goods[rng.Next(Goods.Length)];
                quantity = Math.Min(8, 2 + level / 3 + rng.Next(0, 3)) * scale;
                goal = quantity;
                (title, detail) = Pick(rng,
                    ($"{Cap(EncargoRules.ItemName(item))} para la familia",
                     $"La familia necesita {quantity} {EncargoRules.ItemName(item)} y no le quiere comprar a los {rivalName}. Conseguilas en el Almacén y traélas."),
                    ($"Pedido de {giver.Name}",
                     $"«Me quedé corto de {EncargoRules.ItemName(item)}. Traeme {quantity} y te pago bien. Y que no se enteren los {rivalName}.»"));
                break;
            case EncargoKinds.Contrarreloj:
                item = Goods[rng.Next(Goods.Length)];
                quantity = (1 + rng.Next(0, 3)) * scale;
                goal = quantity;
                limit = 240 + 30 * rng.Next(0, 5);
                (title, detail) = Pick(rng,
                    ("Urgente: antes de que se den cuenta",
                     $"Hacen falta {quantity} {EncargoRules.ItemName(item)} ya mismo. Tenés {limit / 60} minutos; si llegás en la mitad, hay premio (+{EncargoRules.FastBonusPercent} %)."),
                    ("Contra el reloj",
                     $"«Los {rivalName} nos quieren ganar de mano. {quantity} {EncargoRules.ItemName(item)}, en menos de {limit / 60} minutos. Rápido y hay extra.»"));
                break;
            case EncargoKinds.Reparto:
                item = Goods[rng.Next(Goods.Length)];
                quantity = (10 + rng.Next(0, 11)) * scale;
                goal = quantity;
                (title, detail) = Pick(rng,
                    ("Reparto grande",
                     $"{quantity} {EncargoRules.ItemName(item)} para el barrio. Esto no se lleva a mano: cargalo en un vehículo (tuyo o de tu cuadrilla) y estacionalo al lado mío."),
                    ("Un camión lleno",
                     $"«Necesito {quantity} {EncargoRules.ItemName(item)} de una vez. Traé la camioneta cargada hasta acá y lo bajamos.»"));
                break;
            case EncargoKinds.Cobro:
                target = Pick(rng, Businesses[rival]);
                (title, detail) = Pick(rng,
                    ($"Los {rivalName} deben plata",
                     $"Hay una deuda vieja con la familia. Andá al local de los {rivalName} en el Mercado, cobrá y traeme el sobre. Si lo perdés, lo pagás vos."),
                    ("Cuentas pendientes",
                     $"«Los {rivalName} se hacen los distraídos con lo que nos deben. Recordáselo con buenos modales… y traé el sobre entero.»"));
                break;
            case EncargoKinds.Paquete:
                target = EncargoRules.DocksContact;
                (title, detail) = Pick(rng,
                    ("Un paquete en el puerto",
                     "Llegó algo para la familia por barco. Buscalo con el marinero de Los Muelles y traelo sin abrir. Ojo: en el puerto no hay protección."),
                    ("Mercadería sin papeles",
                     "«Gaetano, el marinero, tiene un paquete nuestro. Traémelo. Y si alguien te pregunta qué llevás, no sabés nada.»"));
                break;
            case EncargoKinds.Recado:
                target = Pick(rng, RecadoTargets.Where(n => FamilyRules.FamilyOfNpc(n) != family).ToArray());
                (title, detail) = Pick(rng,
                    ("Un mensaje de la familia",
                     "Llevá este sobre sellado sin abrirlo. Lo que dice no es asunto tuyo; que llegue, sí."),
                    ("Palabras que pesan",
                     "«Este sobre tiene que llegar hoy. Si te lo piden en el camino, decí que es una carta de amor.»"));
                break;
            case EncargoKinds.Vigilar:
                target = Pick(rng, Businesses[rival]);
                goal = (4 + rng.Next(0, 5)) * scale; // en medios minutos
                (title, detail) = Pick(rng,
                    ($"Ojos sobre los {rivalName}",
                     $"Queremos saber quién entra y quién sale del local de los {rivalName}. Quedate cerca {goal / 2.0:0.#} minutos y volvé a contarme."),
                    ("Vigilancia",
                     $"«Algo traman los {rivalName}. Pará en la esquina de su local y mirá. {goal / 2.0:0.#} minutos, sin llamar la atención.»"));
                break;
            default: // asalto
            {
                var targets = new[] { EncargoRules.Truck, EncargoRules.Collector, EconomyCatalog.WorkshopNpc, EconomyCatalog.DepositNpc }
                    .Where(t => EncargoRules.VictimFamily(t) != family).ToArray();
                target = Pick(rng, targets);
                var victim = FamilyName(EncargoRules.VictimFamily(target));
                (title, detail) = Pick(rng,
                    ($"Un golpe a los {victim}",
                     $"Que los {victim} sientan el golpe. Amenazá con un arma hasta que te den lo que llevan; el botín es tuyo, la paga la pongo yo."),
                    ("Cobrarse una vieja",
                     $"«Los {victim} se pasaron de la raya. Hacé el trabajo y vení a contarme. Ellos se van a enojar con vos, no conmigo.»"));
                break;
            }
        }
        if (daily)
        {
            cents = (long)Math.Round(cents * EncargoRules.DailyMultiplier);
            xp = (int)Math.Round(xp * EncargoRules.DailyMultiplier);
            rep = Math.Min(10, rep * 2);
            title = $"Encargo del día: {title}";
        }
        return new Draft(id, kind, giver.Code, family, title, detail, item, quantity, target, goal, limit, cents, xp, rep, daily);
    }

    public static string FamilyName(string family) => family switch
    {
        EconomyCatalog.Moretti => "Moretti",
        EconomyCatalog.Valenti => "Valenti",
        EconomyCatalog.Caruso => "Caruso",
        _ => family,
    };

    private static T Pick<T>(Random rng, params T[] options) => options[rng.Next(options.Length)];

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>De borrador a vista (para mostrarlo como oferta).</summary>
    public static EncargoView ToOffer(Draft d, DateTimeOffset expiresAt, bool story = false) =>
        new(d.Id, d.Kind, d.GiverNpc, d.Family, d.Title, d.Detail, d.ItemCode, d.Quantity, d.TargetNpc, 0, d.Target, 0,
            d.RewardCents, d.Xp, d.Reputation, EncargoStatus.Offered, d.Daily, expiresAt, Story: story);
}
