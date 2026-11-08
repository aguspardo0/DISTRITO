namespace Distrito.Shared.Life;

/// <summary>
/// La vida de los habitantes (etapas 2 a 4: rutinas, necesidades y decisiones). Lógica pura y determinista: el mismo
/// estado y la misma hora dan la misma decisión. No simula cada segundo: cada habitante tiene una hora en la que termina
/// lo que está haciendo (<see cref="ResidentState.Until"/>) y recién ahí decide qué sigue; mientras tanto solo se revisa
/// cada tanto si algo urgente lo interrumpe (mucha hambre, mucho cansancio). Por eso sirve igual para los lejanos (sin
/// personaje en el mundo) y para ponerse al día después de un reinicio.
/// La plata es solo estado del habitante (le alcanza o no para comer); no toca el libro de cuentas.
/// </summary>
public sealed class LifeSimulation(LifeConfig cfg)
{
    public const string HomePlace = "casa";

    /// <summary>Velocidad al caminar, en metros por segundo real.</summary>
    public const double WalkSpeed = 1.3;

    /// <summary>Cada cuánto (en minutos de Bellmont) se revisa si algo urgente interrumpe lo que hace.</summary>
    public const double CheckMinutes = 15;

    /// <summary>Tope de decisiones por habitante en una sola llamada (para ponerse al día sin colgarse).</summary>
    public const int MaxCatchUp = 300;

    private readonly Dictionary<string, LifeProfession> _professions = cfg.Professions.ToDictionary(p => p.Code);

    public long DecisionsMade { get; private set; }

    /// <summary>Avanza un habitante hasta <paramref name="now"/>: decide todas las veces que haga falta.</summary>
    public ResidentState Advance(Resident r, ResidentState s, DateTimeOffset now)
    {
        for (var i = 0; i < MaxCatchUp; i++)
        {
            if (s.Until <= now)
            {
                var at = s.Until == default || s.Until < now.AddDays(-2) ? now : s.Until; // muy atrasado: arranca de ahora
                s = Finish(r, s, at);
                s = Decide(r, s, at);
                continue;
            }
            // Algo urgente interrumpe lo que hace (no mientras camina ni duerme ni trabaja).
            if (!s.Travelling(now) && Interruptible(s.Activity) && (now - s.NeedsAt).TotalMinutes * BellmontClock.Speed >= CheckMinutes)
            {
                var updated = Needs(r, s, now);
                if (updated.Hunger > 0.9 || updated.Fatigue > 0.95)
                {
                    s = Decide(r, updated, now);
                    continue;
                }
                s = updated;
            }
            break;
        }
        return s;
    }

    private static bool Interruptible(string activity) =>
        activity is LifeActivities.AtHome or LifeActivities.Strolling or LifeActivities.Shopping or LifeActivities.Socializing;

    /// <summary>Lo que pasa al terminar una actividad: se cobra el turno, se paga la comida, etc.</summary>
    private ResidentState Finish(Resident r, ResidentState s, DateTimeOffset at)
    {
        s = Needs(r, s, at);
        var money = s.Money ?? r.PocketMoneyCents;
        switch (s.Activity)
        {
            case LifeActivities.Working or LifeActivities.Patrolling when !r.OnShift(BellmontClock.Hour(at)):
                money += _professions.GetValueOrDefault(r.Profession)?.Income ?? 0;
                break;
            case LifeActivities.Sleeping when !r.HasShift:
                money += _professions.GetValueOrDefault(r.Profession)?.Income ?? 0; // jubilación o changas del día
                break;
        }
        return s with { Money = money };
    }

    /// <summary>Hambre, cansancio y ganas de ver gente según lo que estuvo haciendo desde la última cuenta.</summary>
    public ResidentState Needs(Resident r, ResidentState s, DateTimeOffset at)
    {
        var hours = Math.Max(0, (at - (s.NeedsAt == default ? s.Since : s.NeedsAt)).TotalHours * BellmontClock.Speed);
        if (hours <= 0)
            return s;
        double hunger = s.Hunger, fatigue = s.Fatigue, social = s.Social;
        var walking = s.Travelling(at);
        var doing = walking ? "" : s.Activity;
        hunger += 0.07 * hours;
        social += 0.04 * hours * (0.5 + r.Trait("sociable"));
        switch (doing)
        {
            case LifeActivities.Sleeping: fatigue -= 0.13 * hours; hunger -= 0.04 * hours; break;
            case LifeActivities.Working or LifeActivities.Patrolling: fatigue += 0.06 * hours * (1.3 - 0.6 * r.Trait("trabajador")); social -= 0.02 * hours; break;
            case LifeActivities.Eating: hunger -= 1.2 * hours; social -= 0.05 * hours; break;
            case LifeActivities.Socializing: social -= 0.4 * hours; break;
            case LifeActivities.AtHome: fatigue -= 0.02 * hours; break;
            default: fatigue += 0.04 * hours; break;
        }
        return s with
        {
            Hunger = Math.Round(Math.Clamp(hunger, 0, 1), 3),
            Fatigue = Math.Round(Math.Clamp(fatigue, 0, 1), 3),
            Social = Math.Round(Math.Clamp(social, 0, 1), 3),
            NeedsAt = at,
        };
    }

    private sealed record Option(string Activity, string Place, double Score, double Minutes, string Reason, long Cost = 0, DateTimeOffset? Until = null);

    /// <summary>Elige qué hacer ahora entre las opciones válidas, con puntaje, y anota el motivo.</summary>
    public ResidentState Decide(Resident r, ResidentState s, DateTimeOffset at)
    {
        s = Needs(r, s, at);
        DecisionsMade++;
        var options = Options(r, s, at).ToList();
        var noise = Noise(r.Id, s.Decisions);
        // Desempate con azar controlado (cada opción recibe un poco distinto, siempre igual para el mismo habitante).
        var best = options.Select((o, i) => (o, score: o.Score + noise[i % noise.Length])).OrderByDescending(x => x.score).First().o;

        var (fromX, fromZ) = s.PositionAt(at);
        var (toX, toZ) = Where(r, best.Place, s.Decisions);
        var travel = TimeSpan.FromSeconds(Math.Sqrt((toX - fromX) * (toX - fromX) + (toZ - fromZ) * (toZ - fromZ)) / WalkSpeed);
        var arrives = at + travel;
        var until = best.Until ?? arrives + TimeSpan.FromSeconds(best.Minutes * 60 / BellmontClock.Speed);
        if (until <= arrives)
            until = arrives + TimeSpan.FromMinutes(1);
        var money = (s.Money ?? r.PocketMoneyCents) - best.Cost;
        return s with
        {
            Activity = best.Activity, Place = best.Place, X = toX, Z = toZ, FromX = fromX, FromZ = fromZ,
            Since = at, ArrivesAt = arrives, Until = until, Money = Math.Max(0, money),
            Decision = Describe(best.Activity, best.Place), Reason = best.Reason, Decisions = s.Decisions + 1,
        };
    }

    private IEnumerable<Option> Options(Resident r, ResidentState s, DateTimeOffset at)
    {
        var hour = BellmontClock.Hour(at);
        var night = BellmontClock.IsNight(at);
        var money = s.Money ?? r.PocketMoneyCents;
        var fearful = r.Trait("temeroso");
        var outdoorsAtNight = night ? 1 - 0.6 * fearful : 1;
        var c = cfg.Costs;
        var reasons = $"hambre {s.Hunger:0.00}, cansancio {s.Fatigue:0.00}, ganas de ver gente {s.Social:0.00}, tiene {Money(money)}";

        // Trabajo: si le toca, casi siempre va (salvo mucha hambre o agotamiento).
        if (r.Workplace is { } work && r.OnShift(hour))
        {
            var activity = r.Profession == "policia" ? LifeActivities.Patrolling : LifeActivities.Working;
            var place = activity == LifeActivities.Patrolling && s.Activity == LifeActivities.Patrolling ? PatrolStop(r, s) : work;
            var shiftEnd = BellmontClock.Next(at, r.ShiftTo);
            // La patrulla va de lugar en lugar: cada parada es una decisión nueva dentro del turno.
            DateTimeOffset? end = activity == LifeActivities.Patrolling ? null : shiftEnd;
            yield return new Option(activity, place, 3 + 0.5 * r.Trait("trabajador") - 0.4 * r.Trait("perezoso"),
                activity == LifeActivities.Patrolling ? 25 : 0, $"le toca el turno ({r.ShiftFrom} a {r.ShiftTo})", Until: end);
        }

        // Dormir: con cansancio, y más de noche (o de día si trabaja de noche).
        var sleepTime = r.HasShift && r.ShiftFrom > r.ShiftTo ? !night : night;
        var wake = WakeHour(r);
        yield return new Option(LifeActivities.Sleeping, HomePlace, 2.2 * s.Fatigue + (sleepTime ? 0.9 : 0) - 0.2,
            0, $"cansancio {s.Fatigue:0.00}{(sleepTime ? ", es su hora de dormir" : "")}",
            Until: sleepTime ? Min(BellmontClock.Next(at, wake), at.AddHours(9.0 / BellmontClock.Speed)) : at.AddHours(1.5 / BellmontClock.Speed));

        // Comer: con hambre y si le alcanza.
        if (money >= c.Meal)
            yield return new Option(LifeActivities.Eating, Nearest(s, cfg.EatPlaces), 2.4 * s.Hunger - 0.1,
                45, $"hambre {s.Hunger:0.00} y le alcanza ({Money(money)})", c.Meal);
        else
            yield return new Option(LifeActivities.AtHome, HomePlace, 2.4 * s.Hunger - 0.3, 60, $"tiene hambre pero no le alcanza ({Money(money)}): come en casa");

        // Salir a ver gente: según qué tan sociable es, de día o a la nochecita.
        if (hour is >= 9 and < 24 && money >= c.Outing)
            yield return new Option(LifeActivities.Socializing, Pick(cfg.SocialPlaces, r.Id, s.Decisions), 1.6 * s.Social * (0.4 + r.Trait("sociable")) * outdoorsAtNight,
                75, $"ganas de ver gente {s.Social:0.00}, {(r.Trait("sociable") > 0.6 ? "es sociable" : "le cuesta un poco salir")}", c.Outing);

        // Compras: con los negocios abiertos, si tiene plata de sobra.
        if (hour is >= 8 and < 20 && money >= c.Shopping * 3)
            yield return new Option(LifeActivities.Shopping, Pick(cfg.ShopPlaces, r.Id, s.Decisions + 7), 0.45 + 0.3 * r.Trait("ambicioso"),
                30, $"tiene plata ({Money(money)}) y los negocios están abiertos", c.Shopping);

        // Pasear: de día, los que no son miedosos.
        if (!night)
            yield return new Option(LifeActivities.Strolling, Pick(cfg.Places.Select(p => p.Code).ToList(), r.Id, s.Decisions + 3), 0.35 + 0.25 * (1 - fearful),
                40, "tiene el día libre y sale a dar una vuelta");

        // Quedarse en casa: los reservados, o de noche los miedosos.
        yield return new Option(LifeActivities.AtHome, HomePlace, 0.45 + 0.35 * r.Trait("reservado") + (night ? 0.4 * fearful : 0),
            60, night && fearful > 0.6 ? "es de noche y prefiere no salir" : "se queda tranquilo en casa");
    }

    /// <summary>A qué hora se levanta: los perezosos, más tarde.</summary>
    public static int WakeHour(Resident r)
    {
        if (r.HasShift && r.ShiftFrom > r.ShiftTo)
            return (r.ShiftTo + 2 + (int)Math.Round(2 * r.Trait("perezoso"))) % 24; // turno noche: duerme de mañana
        var wake = 6 + (int)Math.Round(2 * r.Trait("perezoso"));
        return r.HasShift ? Math.Min(wake, (r.ShiftFrom + 23) % 24) : wake;
    }

    private string PatrolStop(Resident r, ResidentState s) =>
        Pick(cfg.Places.Select(p => p.Code).Where(c => c != s.Place).ToList(), r.Id, s.Decisions + 11);

    private string Nearest(ResidentState s, List<string> codes) =>
        codes.Select(c => cfg.Place(c)!).OrderBy(p => (p.X - s.X) * (p.X - s.X) + (p.Z - s.Z) * (p.Z - s.Z)).First().Code;

    private (double X, double Z) Where(Resident r, string place, long salt)
    {
        if (place == HomePlace)
            return (r.HomeX, r.HomeZ);
        var p = cfg.Place(place);
        if (p is null)
            return (r.HomeX, r.HomeZ);
        // No todos en el mismo punto: cada uno se para un poco al costado.
        var n = Noise(r.Id, salt);
        return (Math.Round(p.X + (n[0] - 0.15) * 8, 2), Math.Round(p.Z + (n[1] - 0.15) * 8, 2));
    }

    private static string Pick(List<string> codes, Guid id, long salt) =>
        codes[(int)(Hash(id, salt) % (ulong)codes.Count)];

    /// <summary>Azar controlado: números entre 0 y 0,3 que dependen solo del habitante y de cuántas decisiones lleva.</summary>
    private static double[] Noise(Guid id, long salt)
    {
        var h = Hash(id, salt);
        var result = new double[8];
        for (var i = 0; i < result.Length; i++)
        {
            h ^= h << 13; h ^= h >> 7; h ^= h << 17;
            result[i] = (h % 1000) / 1000.0 * 0.3;
        }
        return result;
    }

    private static ulong Hash(Guid id, long salt)
    {
        var bytes = id.ToByteArray();
        var h = 1469598103934665603UL ^ (ulong)salt * 1099511628211UL;
        foreach (var b in bytes)
            h = (h ^ b) * 1099511628211UL;
        return h == 0 ? 1 : h;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private string Describe(string activity, string place)
    {
        var name = place == HomePlace ? "su casa" : cfg.Place(place)?.Name ?? place;
        return activity switch
        {
            LifeActivities.Sleeping => "dormir en su casa",
            LifeActivities.AtHome => "quedarse en su casa",
            LifeActivities.Working => $"trabajar en {name}",
            LifeActivities.Patrolling => $"patrullar por {name}",
            LifeActivities.Eating => $"comer en {name}",
            LifeActivities.Shopping => $"comprar en {name}",
            LifeActivities.Socializing => $"ver gente en {name}",
            LifeActivities.Strolling => $"pasear hasta {name}",
            _ => activity,
        };
    }

    private static string Money(long cents) => $"${cents / 100}.{cents % 100:00}";
}
