namespace Distrito.Shared.Life;

/// <summary>
/// La vida de los habitantes (etapas 2 a 4: rutinas, necesidades y decisiones). Lógica pura y determinista: el mismo
/// estado y la misma hora dan la misma decisión. No simula cada segundo: cada habitante tiene una hora en la que termina
/// lo que está haciendo (<see cref="ResidentState.Until"/>) y recién ahí decide qué sigue; mientras tanto solo se revisa
/// cada tanto si algo urgente lo interrumpe (mucha hambre, mucho cansancio). Por eso sirve igual para los lejanos (sin
/// personaje en el mundo) y para ponerse al día después de un reinicio.
/// La plata es solo estado del habitante (le alcanza o no para comer); no toca el libro de cuentas.
/// </summary>
public sealed class LifeSimulation(LifeConfig cfg, LifeSimulation.Router? route = null)
{
    /// <summary>
    /// Camino por la ciudad (etapa 5): las esquinas de (desde) a (hasta), con el destino real al final (puede quedar
    /// en la puerta si el punto pedido está adentro de un edificio). Sin camino, va en línea recta.
    /// </summary>
    public delegate IReadOnlyList<(double X, double Z)> Router(double fromX, double fromZ, double toX, double toZ);

    public const string HomePlace = "casa";

    /// <summary>De visita en la casa de otro: «visita:» + el id del dueño de casa.</summary>
    public const string VisitPrefix = "visita:";

    /// <summary>Velocidad al caminar, en metros por segundo real.</summary>
    public const double WalkSpeed = 1.3;

    /// <summary>Cada cuánto (en minutos de Bellmont) se revisa si algo urgente interrumpe lo que hace.</summary>
    public const double CheckMinutes = 15;

    /// <summary>Tope de decisiones por habitante en una sola llamada (para ponerse al día sin colgarse).</summary>
    public const int MaxCatchUp = 300;

    private readonly Dictionary<string, LifeProfession> _professions = cfg.Professions.ToDictionary(p => p.Code);

    public long DecisionsMade { get; private set; }

    private bool _catchingUp;
    private LifeWorld? _world;
    private DateTimeOffset _at;

    /// <summary>Avanza un habitante hasta <paramref name="now"/>: decide todas las veces que haga falta.</summary>
    /// <param name="world">Los demás habitantes (amigos, rivales, quién atiende cada local). Sin esto decide solo.</param>
    public ResidentState Advance(Resident r, ResidentState s, DateTimeOffset now, LifeWorld? world = null)
    {
        _world = world;
        for (var i = 0; i < MaxCatchUp; i++)
        {
            if (s.Until <= now)
            {
                var at = s.Until == default || s.Until < now.AddDays(-2) ? now : s.Until; // muy atrasado: arranca de ahora
                _catchingUp = at < now.AddMinutes(-10); // poniéndose al día: viajes viejos, sin buscar el camino
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
                    _catchingUp = false;
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
        activity is LifeActivities.AtHome or LifeActivities.Strolling or LifeActivities.Shopping or LifeActivities.Socializing or LifeActivities.Visiting;

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
            case LifeActivities.Socializing or LifeActivities.Visiting: social -= 0.4 * hours; break;
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
        _at = at;
        DecisionsMade++;
        var options = Options(r, s, at).ToList();
        var noise = Noise(r.Id, s.Decisions);
        // Desempate con azar controlado (cada opción recibe un poco distinto, siempre igual para el mismo habitante).
        var best = options.Select((o, i) => (o, score: o.Score + noise[i % noise.Length])).OrderByDescending(x => x.score).First().o;

        var (fromX, fromZ) = s.PositionAt(at);
        var (toX, toZ) = Where(r, best.Place, s.Decisions);
        double[]? path = null;
        if (route is not null && !_catchingUp && (Math.Abs(toX - fromX) > 0.05 || Math.Abs(toZ - fromZ) > 0.05))
        {
            var corners = route(fromX, fromZ, toX, toZ);
            if (corners.Count > 0)
            {
                (toX, toZ) = corners[^1];
                path = corners.Count > 1 ? corners.Take(corners.Count - 1).SelectMany(c => new[] { c.X, c.Z }).ToArray() : null;
            }
        }
        var length = ResidentState.Length(new ResidentState(s.ResidentId, "", "", toX, toZ, at, FromX: fromX, FromZ: fromZ, Path: path).Waypoints());
        var travel = TimeSpan.FromSeconds(length / WalkSpeed);
        var arrives = at + travel;
        var until = best.Until ?? arrives + TimeSpan.FromSeconds(best.Minutes * 60 / BellmontClock.Speed);
        if (until <= arrives)
            until = arrives + TimeSpan.FromMinutes(1);
        // Lo que no es trabajo termina una hora de Bellmont antes del turno: le da tiempo a ir.
        if (r.Workplace is not null && r.HasShift && best.Activity is not (LifeActivities.Working or LifeActivities.Patrolling))
        {
            var beforeShift = BellmontClock.Next(at, r.ShiftFrom) - TimeSpan.FromHours(1.0 / BellmontClock.Speed);
            if (until > beforeShift && beforeShift > arrives)
                until = beforeShift;
        }
        var money = (s.Money ?? r.PocketMoneyCents) - best.Cost;
        return s with
        {
            Activity = best.Activity, Place = best.Place, X = toX, Z = toZ, FromX = fromX, FromZ = fromZ, Path = path,
            Indoor = best.Place == HomePlace || best.Place.StartsWith(VisitPrefix, StringComparison.Ordinal) || cfg.Place(best.Place)?.Indoor == true,
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

        // Trabajo: si le toca (o empieza dentro de una hora), casi siempre va (salvo mucha hambre o agotamiento).
        if (r.Workplace is { } work && (r.OnShift(hour) || r.OnShift((hour + 1) % 24)))
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
        {
            var trusted = Trusted(r, s, cfg.EatPlaces);
            yield return new Option(LifeActivities.Eating, trusted?.Place ?? Nearest(s, cfg.EatPlaces), 2.4 * s.Hunger - 0.1 + (trusted is null ? 0 : 0.1),
                45, $"hambre {s.Hunger:0.00} y le alcanza ({Money(money)}){(trusted is null ? "" : $"; le tiene confianza a {trusted.Value.Name}")}", c.Meal);
        }
        else
            yield return new Option(LifeActivities.AtHome, HomePlace, 2.4 * s.Hunger - 0.3, 60, $"tiene hambre pero no le alcanza ({Money(money)}): come en casa");

        // Salir a ver gente: según qué tan sociable es, de día o a la nochecita.
        if (hour is >= 9 and < 24 && money >= c.Outing)
        {
            var baseScore = 1.6 * s.Social * (0.4 + r.Trait("sociable")) * outdoorsAtNight;
            var why = $"ganas de ver gente {s.Social:0.00}, {(r.Trait("sociable") > 0.6 ? "es sociable" : "le cuesta un poco salir")}";
            var friendOut = FriendAt(r, s, cfg.SocialPlaces);
            if (friendOut is { } f)
                yield return new Option(LifeActivities.Socializing, f.Place, baseScore + 0.5 * f.Tie, 75,
                    $"{why}; su amigo {f.Name} está en {cfg.Place(f.Place)?.Name ?? f.Place}", c.Outing);
            else
            {
                var place = Pick(cfg.SocialPlaces, r.Id, s.Decisions);
                var rival = RivalAt(r, s, place);
                if (rival is not null)
                {
                    var other = cfg.SocialPlaces.FirstOrDefault(p => p != place && RivalAt(r, s, p) is null);
                    if (other is not null) { why += $"; no va a {cfg.Place(place)?.Name ?? place} porque está {rival}"; place = other; rival = null; }
                }
                yield return new Option(LifeActivities.Socializing, place, baseScore - (rival is null ? 0 : 0.6), 75,
                    rival is null ? why : $"{why}, aunque está {rival}", c.Outing);
            }
        }

        // Visitar a un amigo que está en su casa (de día y a la tarde).
        if (hour is >= 10 and < 21 && FriendHome(r, s) is { } host)
            yield return new Option(LifeActivities.Visiting, VisitPrefix + host.Id.ToString("N"), (1.6 * s.Social * (0.4 + r.Trait("sociable")) + 0.4 * host.Tie) * outdoorsAtNight,
                60, $"ganas de ver gente {s.Social:0.00} y su amigo {host.Name} está en casa");

        // Compras: con los negocios abiertos, si tiene plata de sobra.
        if (hour is >= 8 and < 20 && money >= c.Shopping * 3)
        {
            var trusted = Trusted(r, s, cfg.ShopPlaces);
            yield return new Option(LifeActivities.Shopping, trusted?.Place ?? Pick(cfg.ShopPlaces, r.Id, s.Decisions + 7), 0.45 + 0.3 * r.Trait("ambicioso") + (trusted is null ? 0 : 0.15),
                30, $"tiene plata ({Money(money)}) y los negocios están abiertos{(trusted is null ? "" : $"; le compra a {trusted.Value.Name}, que le tiene confianza")}", c.Shopping);
        }

        // Pasear: de día, los que no son miedosos.
        if (!night)
            yield return new Option(LifeActivities.Strolling, Pick(cfg.Places.Select(p => p.Code).ToList(), r.Id, s.Decisions + 3), 0.35 + 0.25 * (1 - fearful),
                40, "tiene el día libre y sale a dar una vuelta");

        // Quedarse en casa: los reservados, o de noche los miedosos.
        yield return new Option(LifeActivities.AtHome, HomePlace, 0.45 + 0.35 * r.Trait("reservado") + (night ? 0.4 * fearful : 0),
            60, night && fearful > 0.6 ? "es de noche y prefiere no salir" : "se queda tranquilo en casa");
    }

    // --- Relaciones (etapa 6) ---

    private IEnumerable<(Resident Other, ResidentState State, double Tie)> Known(ResidentState s, Func<double, bool> which)
    {
        if (_world is null || s.Ties is null)
            yield break;
        foreach (var (id, tie) in s.Ties.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
            if (which(tie) && _world.People.TryGetValue(id, out var other) && _world.States.TryGetValue(id, out var state))
                yield return (other, state, tie);
    }

    /// <summary>Un amigo que está ahora en alguno de estos lugares (comiendo o charlando).</summary>
    private (string Place, string Name, double Tie)? FriendAt(Resident r, ResidentState s, List<string> places)
    {
        foreach (var (other, state, tie) in Known(s, t => t >= LifeSociety.Friend))
            if (places.Contains(state.Place) && state.Activity is LifeActivities.Socializing or LifeActivities.Eating && state.Until > _at)
                return (state.Place, other.FirstName, tie);
        return null;
    }

    /// <summary>Un amigo que está en su casa despierto.</summary>
    private (Guid Id, string Name, double Tie)? FriendHome(Resident r, ResidentState s)
    {
        foreach (var (other, state, tie) in Known(s, t => t >= LifeSociety.Friend))
            if (state.Activity == LifeActivities.AtHome && state.Place == HomePlace && !state.Travelling(_at) && state.Until > _at.AddMinutes(5))
                return (other.Id, other.FirstName, tie);
        return null;
    }

    /// <summary>Alguien con quien se lleva mal y que está en ese lugar («X», para el motivo).</summary>
    private string? RivalAt(Resident r, ResidentState s, string place)
    {
        foreach (var (other, state, _) in Known(s, t => t <= LifeSociety.Rival))
            if (state.Place == place && state.Until > _at)
                return other.FirstName;
        return null;
    }

    /// <summary>Un local (de esta lista) donde atiende ahora alguien en quien confía.</summary>
    private (string Place, string Name)? Trusted(Resident r, ResidentState s, List<string> places)
    {
        foreach (var (other, state, _) in Known(s, t => t >= 0.35))
            if (other.Workplace is { } work && places.Contains(work) && state.Activity == LifeActivities.Working && state.Place == work && state.Until > _at)
                return (work, other.FirstName);
        return null;
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
        if (place.StartsWith(VisitPrefix, StringComparison.Ordinal))
            return Guid.TryParse(place[VisitPrefix.Length..], out var hostId) && _world?.People.TryGetValue(hostId, out var host) == true
                ? (host!.HomeX, host.HomeZ)
                : (r.HomeX, r.HomeZ);
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
        if (place.StartsWith(VisitPrefix, StringComparison.Ordinal))
            return Guid.TryParse(place[VisitPrefix.Length..], out var hostId) && _world?.People.TryGetValue(hostId, out var host) == true
                ? $"visitar a {host!.FullName}" : "visitar a un amigo";
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
