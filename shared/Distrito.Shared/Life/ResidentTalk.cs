namespace Distrito.Shared.Life;

/// <summary>Algo que pasó y de lo que se habla en el barrio: quiénes lo vieron (y sus amigos, de oídas).</summary>
public sealed record LifeRumor(string Kind, double X, double Z, DateTimeOffset At, string PlaceName, IReadOnlySet<Guid> Witnesses);

/// <summary>
/// Hablar con un habitante (vida de los NPC, etapa 8). Te dice quién es y qué está haciendo, y según le preguntes te
/// cuenta lo que vio u oyó (los rumores corren entre amigos), a quién conoce y dónde compra. Determinista, con el tono
/// de cada uno: el desconfiado no suelta nada, el policía habla de los avisos. Es ambientación: no da ni cobra nada.
/// </summary>
public static class ResidentTalk
{
    public const string News = "noticias";
    public const string People = "gente";
    public const string Shops = "compras";

    /// <summary>Hasta cuándo se sigue hablando de algo (horas reales).</summary>
    public const double RumorHours = 6;

    public static IReadOnlyList<(string Id, string Label)> Questions { get; } =
    [
        (News, "¿Pasó algo raro por acá?"),
        (People, "¿A quién conoce en el barrio?"),
        (Shops, "¿Dónde conviene comprar?"),
    ];

    private static string Pick(Resident r, long salt, params string[] options) =>
        options[(int)((uint)unchecked(r.Id.GetHashCode() * 31 + (int)salt) % (uint)options.Length)];

    public static string Greeting(Resident r, ResidentState s, LifeConfig cfg, DateTimeOffset now)
    {
        var hour = BellmontClock.Hour(now);
        var hello = r.Trait("desconfiado") > 0.7 ? "¿Sí? ¿Qué necesita?"
            : hour < 12 ? Pick(r, s.Decisions, "Buen día.", "Buenos días, ¿cómo le va?")
            : hour < 20 ? Pick(r, s.Decisions, "Buenas tardes.", "Buenas, ¿qué tal?")
            : Pick(r, s.Decisions, "Buenas noches.", "Buenas noches, ¿anda perdido?");
        var profession = cfg.Professions.FirstOrDefault(p => p.Code == r.Profession)?.Name.ToLowerInvariant() ?? r.Profession;
        profession = (profession, r.Woman) switch
        {
            ("vecino", true) => "vecina",
            ("trabajador", true) => "trabajadora",
            ("policía", _) => "agente de policía",
            _ => profession,
        };
        var district = cfg.District(r.District)?.Name ?? r.District;
        var of = district.StartsWith("Barrio", StringComparison.Ordinal) ? $"del {district}" : $"de {district}";
        var doing = s.Activity switch
        {
            LifeActivities.Fleeing => "¡Ahora no, que hay tiros! ¡Métase en algún lado!",
            LifeActivities.Hiding => "Shh… estoy esperando a que se calme la cosa afuera.",
            LifeActivities.Responding => "Circule, que estoy atendiendo un aviso.",
            LifeActivities.Working => Pick(r, s.Decisions, "Estoy trabajando, así que rapidito.", "Acá, laburando como siempre."),
            LifeActivities.Patrolling => "Haciendo la ronda. Todo en orden, por ahora.",
            LifeActivities.Shopping => "Salí a hacer unas compras.",
            LifeActivities.Eating => "Vine a comer algo.",
            LifeActivities.Socializing => "Salí a charlar un rato con la gente.",
            LifeActivities.Strolling => "Dando una vuelta, nada más.",
            LifeActivities.Visiting => "Vine a visitar a un amigo.",
            _ when s.Bag => "Vuelvo de hacer las compras, mire cómo vengo cargado.",
            _ => s.Decision is { Length: > 0 } d ? $"Ahora voy a {d}." : "Nada, acá ando.",
        };
        var scared = s.ScaredUntil > now ? " Todavía tengo el corazón en la boca por lo de hace un rato." : "";
        return $"{hello} Soy {r.FullName}, {profession} {of}. {doing}{scared}";
    }

    public static string Answer(string question, Resident r, ResidentState s, LifeWorld world, IReadOnlyList<LifeRumor> rumors,
        LifeConfig cfg, DateTimeOffset now) => question switch
    {
        News => NewsFor(r, s, world, rumors, now),
        People => PeopleFor(r, s, world),
        Shops => ShopsFor(r, s, world, cfg),
        _ => "¿Cómo dice?",
    };

    private static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var minutes = (int)(now - at).TotalMinutes;
        return minutes < 2 ? "recién" : minutes < 60 ? $"hace {minutes} minutos" : $"hace {minutes / 60} hora{(minutes / 60 == 1 ? "" : "s")}";
    }

    private static string NewsFor(Resident r, ResidentState s, LifeWorld world, IReadOnlyList<LifeRumor> rumors, DateTimeOffset now)
    {
        var fresh = rumors.Where(x => (now - x.At).TotalHours <= RumorHours && x.At <= now).OrderByDescending(x => x.At).ToList();
        var seen = fresh.FirstOrDefault(x => x.Witnesses.Contains(r.Id));
        // De oídas: alguno de sus amigos lo vio.
        var heard = fresh.Select(x => (Rumor: x, Friend: x.Witnesses
                .Where(w => w != r.Id && LifeSociety.Tie(s, w) >= LifeSociety.Friend && world.People.ContainsKey(w))
                .OrderByDescending(w => LifeSociety.Tie(s, w)).Select(w => world.People[w].FirstName).FirstOrDefault()))
            .FirstOrDefault(x => x.Friend is not null && !x.Rumor.Witnesses.Contains(r.Id));

        if (r.Profession == "policia")
            return (seen ?? fresh.FirstOrDefault()) is { } report // a la policía le llegan todos los avisos
                ? $"Hubo {IncidentKinds.Describe(report.Kind)} cerca de {report.PlaceName}, {Ago(report.At, now)}. Estamos en eso; usted no vio nada, ¿no?"
                : "Tranquilo, por ahora. Si ve algo raro, avise en el puesto de la Avenida Norte.";
        if (r.Trait("desconfiado") > 0.7)
            return Pick(r, s.Decisions, "Yo no vi nada. Y usted tampoco.", "En este barrio el que habla de más termina mal. No sé nada.");
        if (seen is not null)
            return $"¡Ni me hable! Yo estaba ahí cerca: {IncidentKinds.Describe(seen.Kind)} por {seen.PlaceName}, {Ago(seen.At, now)}. " +
                   (seen.Kind == IncidentKinds.Robbery ? "Esa gente no respeta nada." : "Me pegué un susto bárbaro.");
        if (heard.Rumor is { } h)
            return $"Me contó {heard.Friend} que hubo {IncidentKinds.Describe(h.Kind)} por {h.PlaceName}, {Ago(h.At, now)}. Yo no lo vi, ¿eh?";
        return Pick(r, s.Decisions, "Que yo sepa, nada. Un día tranquilo.", "Nada que yo sepa. Y mejor así.", "Lo de siempre: mucho trabajo y poca plata.");
    }

    private static string PeopleFor(Resident r, ResidentState s, LifeWorld world)
    {
        if (s.Ties is not { Count: > 0 } ties)
            return "Recién me estoy haciendo conocido por acá.";
        string Name(Guid id) => world.People.TryGetValue(id, out var p) ? p.FullName : "uno de por ahí";
        var friends = ties.Where(t => t.Value >= LifeSociety.Friend).OrderByDescending(t => t.Value).Take(2).Select(t => Name(t.Key)).ToList();
        var known = ties.Where(t => t.Value is >= LifeSociety.Acquaintance and < LifeSociety.Friend).OrderByDescending(t => t.Value).Take(2).Select(t => Name(t.Key)).ToList();
        var rival = ties.Where(t => t.Value <= LifeSociety.Rival).OrderBy(t => t.Value).Select(t => Name(t.Key)).FirstOrDefault();
        var parts = new List<string>();
        if (friends.Count > 0)
            parts.Add($"Mi gran amigo es {string.Join(" y también ", friends)}.");
        if (known.Count > 0)
            parts.Add($"Conozco a {string.Join(" y a ", known)}, gente bien.");
        if (rival is not null)
            parts.Add(r.Trait("agresivo") > 0.6 ? $"Y a {rival}, si lo ve, dígale que no se me cruce." : $"Con {rival} no me hablo, mejor ni pregunte.");
        return parts.Count == 0 ? "Conozco a algunos de vista, nada más." : string.Join(" ", parts);
    }

    private static string ShopsFor(Resident r, ResidentState s, LifeWorld world, LifeConfig cfg)
    {
        var trusted = (s.Ties ?? new())
            .Where(t => t.Value >= 0.35 && world.People.TryGetValue(t.Key, out var p) && p.Workplace is { } w && (cfg.ShopPlaces.Contains(w) || cfg.EatPlaces.Contains(w)))
            .OrderByDescending(t => t.Value).Select(t => world.People[t.Key]).FirstOrDefault();
        if (trusted is not null)
            return $"Yo le compro a {trusted.FullName}, en {cfg.Place(trusted.Workplace!)!.Name}. Es gente de confianza: no le mete la mano en el bolsillo a nadie.";
        var shop = cfg.Place(cfg.ShopPlaces[(int)((uint)r.Id.GetHashCode() % cfg.ShopPlaces.Count)])?.Name ?? "el Mercado";
        return Pick(r, s.Decisions, $"Por ahora compro en {shop}, pero todavía no le tengo confianza a nadie.", $"Voy a {shop}, que me queda cerca. Los precios, un robo.");
    }

    /// <summary>El lugar con nombre más cercano a un punto (para contar dónde pasó algo).</summary>
    public static string NearestPlaceName(LifeConfig cfg, double x, double z)
    {
        var place = cfg.Places.OrderBy(p => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z)).FirstOrDefault();
        var district = cfg.AllDistricts.OrderBy(d => (d.X - x) * (d.X - x) + (d.Z - z) * (d.Z - z)).FirstOrDefault();
        if (place is null)
            return district?.Name ?? "el centro";
        var placeDist = Math.Sqrt((place.X - x) * (place.X - x) + (place.Z - z) * (place.Z - z));
        return placeDist <= 40 || district is null ? place.Name : district.Name;
    }
}
