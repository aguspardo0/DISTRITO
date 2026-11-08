using Distrito.Shared.Life;

namespace Distrito.Shared.Tests;

/// <summary>Vida de los NPC, etapa 6: relaciones entre los 30 habitantes de la prueba.</summary>
public class LifeSocietyTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(-3));

    private static LifeConfig Config()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config/vida.json");
            if (File.Exists(candidate))
                return LifeConfig.Parse(File.ReadAllText(candidate));
        }
        throw new FileNotFoundException("config/vida.json");
    }

    private sealed record Run(Dictionary<Guid, Resident> People, Dictionary<Guid, ResidentState> States, List<ResidentEvent> Events, List<string> Reasons);

    /// <summary>Dos días reales (cuatro de Bellmont), como en el servidor: cada minuto deciden y cada 8 se cruzan.</summary>
    private static Run Simulate(int days = 2)
    {
        var cfg = Config();
        var sim = new LifeSimulation(cfg);
        var people = ResidentGenerator.Generate(cfg).ToDictionary(p => p.Id);
        var states = LifeSociety.Seed(people.Values.ToList(), people.Values.ToDictionary(p => p.Id, p => ResidentGenerator.InitialState(p, Start)));
        var world = new LifeWorld(people, states);
        var events = new List<ResidentEvent>();
        var reasons = new List<string>();
        var lastMeet = Start;
        for (var minute = 0; minute <= days * 24 * 60; minute++)
        {
            var now = Start.AddMinutes(minute);
            foreach (var p in people.Values.OrderBy(p => p.Id))
            {
                var before = states[p.Id];
                var after = sim.Advance(p, before, now, world);
                if (after.Decisions != before.Decisions && after.Reason is { } reason)
                    reasons.Add($"{after.Decision}: {reason}");
                states[p.Id] = after;
            }
            if (minute % 8 == 0 && minute > 0)
            {
                var (changed, happened) = LifeSociety.Meet(people, states, now, (now - lastMeet).TotalHours * BellmontClock.Speed);
                lastMeet = now;
                foreach (var (id, s) in changed)
                    states[id] = s;
                events.AddRange(happened);
            }
        }
        return new Run(people, states, events, reasons);
    }

    private static readonly Lazy<Run> Two = new(() => Simulate());

    [Fact]
    public void Al_principio_conocen_a_algunos_vecinos_y_a_los_companeros()
    {
        var cfg = Config();
        var people = ResidentGenerator.Generate(cfg);
        var states = LifeSociety.Seed(people, people.ToDictionary(p => p.Id, p => ResidentGenerator.InitialState(p, Start)));
        Assert.All(people, p => Assert.NotEmpty(states[p.Id].Ties!));
        foreach (var a in people.Where(p => p.Workplace is not null))
            foreach (var b in people.Where(b => b.Id != a.Id && b.Workplace == a.Workplace))
                Assert.True(LifeSociety.Tie(states[a.Id], b.Id) >= 0.2, $"{a.FullName} no conoce a su compañero {b.FullName}");
        Assert.All(states.Values, s => Assert.All(s.Ties!.Values, v => Assert.InRange(v, 0, LifeSociety.Friend - 0.01)));
        // Si ya tienen relaciones (después de un reinicio), no se vuelven a crear.
        var again = LifeSociety.Seed(people, states);
        Assert.All(people, p => Assert.Same(states[p.Id].Ties, again[p.Id].Ties));
    }

    [Fact]
    public void Los_agresivos_chocan_y_los_sociables_se_llevan_bien()
    {
        var cfg = Config();
        var people = ResidentGenerator.Generate(cfg);
        Resident With(Resident r, double sociable, double aggressive) => r with
        {
            Traits = new Dictionary<string, double>(r.Traits) { ["sociable"] = sociable, ["reservado"] = 1 - sociable, ["agresivo"] = aggressive, ["pacifico"] = 1 - aggressive },
        };
        var friendly = LifeSociety.Compatibility(With(people[0], 0.9, 0.1), With(people[1], 0.9, 0.1));
        var hostile = LifeSociety.Compatibility(With(people[0], 0.3, 0.9), With(people[1], 0.5, 0.4));
        Assert.True(friendly > 0.4, $"sociables: {friendly:0.00}");
        Assert.True(hostile < 0.2, $"con un agresivo: {hostile:0.00}");
    }

    [Fact]
    public void Con_el_tiempo_se_hacen_amigos()
    {
        var run = Two.Value;
        var pairs = run.States.SelectMany(kv => (kv.Value.Ties ?? new()).Select(t => (A: kv.Key, B: t.Key, t.Value))).Where(t => t.A.CompareTo(t.B) < 0).ToList();
        var friends = pairs.Count(p => p.Value >= LifeSociety.Friend);
        Assert.True(friends >= 3, $"solo {friends} amistades en cuatro días de Bellmont");
        Assert.Contains(run.Events, e => e.Kind == "amistad");
        // Lo que se cuenta en los hechos coincide con lo que quedó (nadie se hace amigo «de la nada»).
        Assert.All(run.Events.Where(e => e.Kind == "amistad"), e => Assert.Contains("se hicieron amigos", e.Text));
        Assert.All(run.States.Values, s =>
        {
            Assert.True((s.Ties?.Count ?? 0) <= LifeSociety.MaxTies);
            Assert.All(s.Ties!.Values, v => Assert.InRange(v, -1, 1));
        });
    }

    [Fact]
    public void Las_relaciones_cambian_lo_que_deciden()
    {
        var run = Two.Value;
        Assert.Contains(run.Reasons, r => r.Contains("su amigo"));
        Assert.Contains(run.Reasons, r => r.Contains("le tiene confianza"));
    }

    [Fact]
    public void Con_ganas_de_ver_gente_va_a_visitar_al_amigo_que_esta_en_su_casa()
    {
        var cfg = Config();
        var sim = new LifeSimulation(cfg);
        var vecinos = ResidentGenerator.Generate(cfg).Where(p => p.Profession == "vecino").Take(2).ToList();
        var a = vecinos[0] with { Traits = new Dictionary<string, double>(vecinos[0].Traits) { ["sociable"] = 0.8, ["reservado"] = 0.2, ["temeroso"] = 0.2 } };
        var b = vecinos[1];
        var at = Start.AddHours(8); // las 16 en Bellmont
        Assert.Equal(16, BellmontClock.Hour(at));
        var stateA = ResidentGenerator.InitialState(a, at) with { Social = 0.9, Hunger = 0.1, Fatigue = 0.1, Money = 10_000, Ties = new() { [b.Id] = 0.8 } };
        var stateB = ResidentGenerator.InitialState(b, at) with { Until = at.AddHours(2), Ties = new() { [a.Id] = 0.8 } };
        var world = new LifeWorld(new Dictionary<Guid, Resident> { [a.Id] = a, [b.Id] = b }, new Dictionary<Guid, ResidentState> { [a.Id] = stateA, [b.Id] = stateB });

        var after = sim.Advance(a, stateA, at, world);
        Assert.Equal(LifeActivities.Visiting, after.Activity);
        Assert.Equal($"visitar a {b.FullName}", after.Decision);
        Assert.Contains($"su amigo {b.FirstName} está en casa", after.Reason);
        Assert.True(after.Indoor);
        Assert.True(Math.Abs(after.X - b.HomeX) < 2 && Math.Abs(after.Z - b.HomeZ) < 2, "no fue a la casa del amigo");
        // Estando juntos en esa casa, se cruzan.
        Assert.Equal(LifeSociety.Spot(b, stateB, at), LifeSociety.Spot(a, after with { ArrivesAt = at }, at));
    }

    [Fact]
    public void Es_determinista()
    {
        var a = Simulate(1);
        var b = Simulate(1);
        foreach (var id in a.People.Keys)
        {
            Assert.Equal(a.States[id].Decisions, b.States[id].Decisions);
            Assert.Equal(a.States[id].Ties!.OrderBy(kv => kv.Key), b.States[id].Ties!.OrderBy(kv => kv.Key));
        }
    }

    [Fact]
    public void Las_relaciones_se_guardan_y_vuelven_igual()
    {
        var run = Two.Value;
        var any = run.States.Values.First(s => s.Ties is { Count: > 0 });
        var json = System.Text.Json.JsonSerializer.Serialize(any);
        var back = System.Text.Json.JsonSerializer.Deserialize<ResidentState>(json)!;
        Assert.Equal(any.Ties!.OrderBy(kv => kv.Key), back.Ties!.OrderBy(kv => kv.Key));
    }
}
