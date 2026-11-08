namespace Distrito.Shared.Life;

/// <summary>Todos los habitantes a la vez: lo que necesita uno para decidir según los demás (amigos, rivales).</summary>
public sealed record LifeWorld(IReadOnlyDictionary<Guid, Resident> People, IReadOnlyDictionary<Guid, ResidentState> States);

/// <summary>
/// Relaciones entre habitantes (vida de los NPC, etapa 6). Cada uno guarda, de los que conoce, un número de −1 a 1:
/// de 0,25 para arriba se conocen, de 0,6 son amigos y de −0,4 para abajo se llevan mal. Cambia cuando coinciden en un
/// mismo lugar (trabajan juntos, comen en el mismo diner, se cruzan en el kiosco): si son compatibles se acercan y si no,
/// se alejan. El cliente que compra seguido en un local le va tomando confianza al que atiende. Determinista y sin plata:
/// es ambientación y sirve para las decisiones (ir a ver a un amigo, evitar a un rival, comprar donde confía).
/// </summary>
public static class LifeSociety
{
    public const double Acquaintance = 0.25;
    public const double Friend = 0.6;
    public const double Rival = -0.4;
    public const int MaxTies = 12;

    public static string Label(double tie) => tie switch
    {
        >= Friend => "amigo",
        >= Acquaintance => "conocido",
        <= Rival => "se llevan mal",
        _ => "",
    };

    public static double Tie(ResidentState s, Guid other) => s.Ties is { } t && t.TryGetValue(other, out var v) ? v : 0;

    /// <summary>
    /// Qué tan bien se llevan dos personas (de 0 a 1, cerca de 0,3 es neutro): los sociables y los del mismo barrio se
    /// acercan; los agresivos y los desconfiados chocan.
    /// </summary>
    public static double Compatibility(Resident a, Resident b)
    {
        var c = 0.45
                + 0.25 * (a.Trait("sociable") + b.Trait("sociable")) / 2
                - 0.45 * Math.Max(a.Trait("agresivo"), b.Trait("agresivo")) * Math.Max(a.Trait("agresivo"), b.Trait("agresivo")) * 2
                - 0.15 * Math.Max(a.Trait("desconfiado"), b.Trait("desconfiado"))
                + (a.District == b.District ? 0.1 : 0)
                + (a.Profession == b.Profession ? 0.05 : 0);
        return Math.Clamp(c, 0, 1);
    }

    /// <summary>
    /// Relaciones iniciales (una sola vez, la primera vez que se cargan sin ninguna): cada uno conoce un poco a dos o
    /// tres vecinos de su barrio y a los compañeros de trabajo.
    /// </summary>
    public static Dictionary<Guid, ResidentState> Seed(IReadOnlyList<Resident> people, IReadOnlyDictionary<Guid, ResidentState> states)
    {
        var result = new Dictionary<Guid, ResidentState>(states);
        if (states.Values.Any(s => s.Ties is { Count: > 0 }))
            return result;
        var ties = people.ToDictionary(p => p.Id, _ => new Dictionary<Guid, double>());
        foreach (var a in people)
        {
            var neighbours = people.Where(b => b.Id != a.Id && b.District == a.District)
                .OrderBy(b => Mix(a.Id, b.Id)).Take(3);
            foreach (var b in neighbours)
                Set(ties, a.Id, b.Id, 0.15 + 0.15 * Compatibility(a, b));
            foreach (var b in people.Where(b => b.Id != a.Id && b.Workplace is not null && b.Workplace == a.Workplace))
                Set(ties, a.Id, b.Id, 0.2);
        }
        foreach (var p in people)
            if (result.TryGetValue(p.Id, out var s))
                result[p.Id] = s with { Ties = ties[p.Id] };
        return result;
    }

    private static void Set(Dictionary<Guid, Dictionary<Guid, double>> ties, Guid a, Guid b, double value)
    {
        ties[a][b] = Math.Round(Math.Max(ties[a].GetValueOrDefault(b), value), 3);
        ties[b][a] = Math.Round(Math.Max(ties[b].GetValueOrDefault(a), value), 3);
    }

    /// <summary>Dónde está a los fines de cruzarse con otros (null = en la calle o durmiendo: no se cruza con nadie).</summary>
    public static string? Spot(Resident r, ResidentState s, DateTimeOffset now)
    {
        if (s.Travelling(now) || s.Activity == LifeActivities.Sleeping)
            return null;
        if (s.Place == LifeSimulation.HomePlace)
            return $"casa:{r.Id:N}";
        if (s.Place.StartsWith(LifeSimulation.VisitPrefix, StringComparison.Ordinal))
            return $"casa:{s.Place[LifeSimulation.VisitPrefix.Length..]}";
        return s.Place;
    }

    /// <summary>
    /// Los que coinciden en un lugar durante <paramref name="hours"/> horas de Bellmont se acercan o se alejan.
    /// Devuelve los estados nuevos (solo los que cambiaron) y los hechos importantes (se hicieron amigos, se pelearon).
    /// </summary>
    public static (Dictionary<Guid, ResidentState> Changed, List<ResidentEvent> Events) Meet(
        IReadOnlyDictionary<Guid, Resident> people, IReadOnlyDictionary<Guid, ResidentState> states, DateTimeOffset now, double hours)
    {
        var changed = new Dictionary<Guid, ResidentState>();
        var events = new List<ResidentEvent>();
        if (hours <= 0)
            return (changed, events);
        hours = Math.Min(hours, 2);
        var groups = states
            .Select(kv => (Id: kv.Key, Spot: people.TryGetValue(kv.Key, out var r) ? Spot(r, kv.Value, now) : null))
            .Where(x => x.Spot is not null)
            .GroupBy(x => x.Spot!)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        var ties = new Dictionary<Guid, Dictionary<Guid, double>>();
        Dictionary<Guid, double> TiesOf(Guid id) =>
            ties.TryGetValue(id, out var t) ? t : (ties[id] = new Dictionary<Guid, double>(states[id].Ties ?? new()));

        foreach (var group in groups)
        {
            var ids = group.Select(x => x.Id).OrderBy(id => id).ToList();
            if (ids.Count < 2 || ids.Count > 40)
                continue;
            for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
            {
                Resident a = people[ids[i]], b = people[ids[j]];
                ResidentState sa = states[a.Id], sb = states[b.Id];
                var before = Tie(sa, b.Id);
                // Al principio se cruzan y nada más: tienen que coincidir varias veces para conocerse.
                var delta = 0.2 * (Compatibility(a, b) - 0.3) * hours;
                if (sa.Activity is LifeActivities.Working or LifeActivities.Patrolling && sb.Activity is LifeActivities.Working or LifeActivities.Patrolling)
                    delta += 0.03 * hours; // compañeros de trabajo
                if (sa.Activity == LifeActivities.Socializing && sb.Activity == LifeActivities.Socializing)
                    delta *= 2; // salieron a charlar
                var after = Math.Round(Math.Clamp(before + delta, -1, 1), 3);
                // El cliente le toma confianza al que lo atiende (más rápido que una amistad, salvo los desconfiados).
                double trustA = 0, trustB = 0;
                if (Serves(a, sa, sb)) trustB = 0.25 * hours * (1 - b.Trait("desconfiado"));
                if (Serves(b, sb, sa)) trustA = 0.25 * hours * (1 - a.Trait("desconfiado"));
                var forA = Math.Round(Math.Clamp(after + trustA, -1, 1), 3);
                var forB = Math.Round(Math.Clamp(Tie(sb, a.Id) + (after - before) + trustB, -1, 1), 3);
                TiesOf(a.Id)[b.Id] = forA;
                TiesOf(b.Id)[a.Id] = forB;
                Crossed(events, now, a, b, before, forA);
            }
        }
        foreach (var (id, t) in ties)
        {
            var kept = t.Count <= MaxTies ? t : t.OrderByDescending(kv => Math.Abs(kv.Value)).ThenBy(kv => kv.Key).Take(MaxTies).ToDictionary();
            changed[id] = states[id] with { Ties = kept };
        }
        return (changed, events);
    }

    /// <summary>¿<paramref name="worker"/> atiende al otro? (trabaja en el lugar y el otro come o compra ahí).</summary>
    private static bool Serves(Resident worker, ResidentState w, ResidentState customer) =>
        w.Activity == LifeActivities.Working && worker.Workplace == w.Place &&
        customer.Activity is LifeActivities.Eating or LifeActivities.Shopping;

    private static void Crossed(List<ResidentEvent> events, DateTimeOffset now, Resident a, Resident b, double before, double after)
    {
        if (before < Friend && after >= Friend)
        {
            events.Add(new ResidentEvent(a.Id, now, "amistad", $"{a.FullName} y {b.FullName} se hicieron amigos."));
            events.Add(new ResidentEvent(b.Id, now, "amistad", $"{b.FullName} y {a.FullName} se hicieron amigos."));
        }
        else if (before > Rival && after <= Rival)
        {
            events.Add(new ResidentEvent(a.Id, now, "rivalidad", $"{a.FullName} y {b.FullName} no se soportan."));
            events.Add(new ResidentEvent(b.Id, now, "rivalidad", $"{b.FullName} y {a.FullName} no se soportan."));
        }
    }

    /// <summary>Amigos, conocidos y rivales de alguien, con nombre (para el panel de administración).</summary>
    public static string Describe(ResidentState s, IReadOnlyDictionary<Guid, Resident> people)
    {
        if (s.Ties is not { Count: > 0 } ties)
            return "no conoce a nadie todavía";
        var parts = ties.Where(kv => Label(kv.Value) != "" && people.ContainsKey(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{people[kv.Key].FullName} ({Label(kv.Value)} {kv.Value:0.00})");
        var text = string.Join(", ", parts);
        return text == "" ? "solo se cruzó con algunos" : text;
    }

    private static ulong Mix(Guid a, Guid b)
    {
        var h = 1469598103934665603UL;
        foreach (var x in a.ToByteArray().Concat(b.ToByteArray()))
            h = (h ^ x) * 1099511628211UL;
        return h;
    }
}
