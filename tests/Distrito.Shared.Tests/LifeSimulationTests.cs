using Distrito.Shared.Life;

namespace Distrito.Shared.Tests;

/// <summary>Vida de los NPC, etapas 2 a 4: rutinas, necesidades y decisiones de los 30 habitantes de la prueba.</summary>
public class LifeSimulationTests
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

    /// <summary>Un día real (dos días de Bellmont), avanzando cada minuto real. Devuelve las muestras de cada minuto.</summary>
    private static (List<Resident> People, List<(DateTimeOffset At, Dictionary<Guid, ResidentState> States)> Samples, LifeSimulation Sim) RunDay()
    {
        var cfg = Config();
        var sim = new LifeSimulation(cfg);
        var people = ResidentGenerator.Generate(cfg);
        var states = people.ToDictionary(p => p.Id, p => ResidentGenerator.InitialState(p, Start));
        var samples = new List<(DateTimeOffset, Dictionary<Guid, ResidentState>)>();
        for (var minute = 0; minute <= 24 * 60; minute++)
        {
            var now = Start.AddMinutes(minute);
            foreach (var p in people)
                states[p.Id] = sim.Advance(p, states[p.Id], now);
            samples.Add((now, new Dictionary<Guid, ResidentState>(states)));
        }
        return (people, samples, sim);
    }

    private static readonly Lazy<(List<Resident> People, List<(DateTimeOffset At, Dictionary<Guid, ResidentState> States)> Samples, LifeSimulation Sim)> Day = new(RunDay);

    [Fact]
    public void Los_que_tienen_turno_trabajan_en_su_horario()
    {
        var (people, samples, _) = Day.Value;
        foreach (var p in people.Where(p => p.HasShift))
        {
            // Mitad del turno (lejos del viaje de ida): casi siempre está trabajando o patrullando.
            var mid = samples.Where(x => p.OnShift(BellmontClock.Hour(x.At)) && p.OnShift((BellmontClock.Hour(x.At) + 23) % 24) && p.OnShift((BellmontClock.Hour(x.At) + 1) % 24)).ToList();
            var working = mid.Count(x => x.States[p.Id].Activity is LifeActivities.Working or LifeActivities.Patrolling);
            Assert.True(working >= mid.Count * 0.8, $"{p.FullName} ({p.Profession}) trabajó {working} de {mid.Count} minutos de su turno");
        }
    }

    [Fact]
    public void Todos_comen_y_de_madrugada_la_mayoria_duerme()
    {
        var (people, samples, _) = Day.Value;
        var ate = people.Where(p => samples.Any(x => x.States[p.Id].Activity == LifeActivities.Eating)).ToList();
        Assert.True(ate.Count >= people.Count - 2, $"comieron {ate.Count} de {people.Count}");

        var dayPeople = people.Where(p => !(p.HasShift && p.ShiftFrom > p.ShiftTo)).ToList();
        var lateNight = samples.Where(x => BellmontClock.Hour(x.At) is >= 2 and < 4).ToList();
        var sleeping = lateNight.Average(x => dayPeople.Count(p => x.States[p.Id].Activity == LifeActivities.Sleeping) / (double)dayPeople.Count);
        Assert.True(sleeping > 0.6, $"de madrugada duerme el {sleeping:P0}");
    }

    [Fact]
    public void No_todos_hacen_la_misma_rutina()
    {
        var (people, samples, _) = Day.Value;
        var routines = people.Select(p =>
        {
            var seq = new List<string>();
            foreach (var x in samples)
                if (seq.Count == 0 || seq[^1] != x.States[p.Id].Decision)
                    seq.Add(x.States[p.Id].Decision ?? "");
            return string.Join(" > ", seq.Take(12));
        }).ToList();
        Assert.True(routines.Distinct().Count() >= 25, $"solo {routines.Distinct().Count()} rutinas distintas");
        // Y hay de todo un poco: salen, compran, pasean, se quedan en casa.
        var activities = samples.SelectMany(x => x.States.Values.Select(s => s.Activity)).Distinct().ToList();
        foreach (var a in new[] { LifeActivities.Eating, LifeActivities.Sleeping, LifeActivities.Working, LifeActivities.Patrolling, LifeActivities.Socializing, LifeActivities.AtHome })
            Assert.Contains(a, activities);
    }

    [Fact]
    public void Siempre_hay_policia_patrullando_de_dia_y_de_noche()
    {
        var (people, samples, _) = Day.Value;
        var police = people.Where(p => p.Profession == "policia").Select(p => p.Id).ToList();
        var withoutPatrol = samples.Count(x => !police.Any(id => x.States[id].Activity == LifeActivities.Patrolling));
        Assert.True(withoutPatrol < samples.Count * 0.05, $"{withoutPatrol} minutos sin ningún policía de patrulla");
    }

    [Fact]
    public void Necesidades_y_plata_quedan_en_rango_y_se_mueven_por_la_ciudad()
    {
        var (people, samples, sim) = Day.Value;
        Assert.All(samples[^1].States.Values, s =>
        {
            Assert.InRange(s.Hunger, 0, 1);
            Assert.InRange(s.Fatigue, 0, 1);
            Assert.InRange(s.Social, 0, 1);
            Assert.True(s.Money >= 0);
            Assert.False(string.IsNullOrEmpty(s.Reason));
        });
        // Van de un lado a otro: a mitad del día hay gente caminando por la calle.
        Assert.Contains(samples, x => x.States.Values.Count(s => s.Travelling(x.At)) >= 3);
        Assert.True(sim.DecisionsMade > people.Count * 10);
    }

    [Fact]
    public void Con_mucha_hambre_deja_lo_que_esta_haciendo_para_comer()
    {
        var cfg = Config();
        var sim = new LifeSimulation(cfg);
        var p = ResidentGenerator.Generate(cfg).First(r => r.Profession == "vecino");
        var noon = Start.AddHours(6); // mediodía en Bellmont
        var walking = ResidentGenerator.InitialState(p, noon) with
        {
            Activity = LifeActivities.Strolling, Place = "kiosco", Until = noon.AddHours(2), ArrivesAt = noon, NeedsAt = noon,
            Hunger = 0.95, Money = 5_000,
        };
        var after = sim.Advance(p, walking, noon.AddMinutes(10));
        Assert.Equal(LifeActivities.Eating, after.Activity);
        Assert.Contains("hambre", after.Reason);
    }

    [Fact]
    public void Se_pone_al_dia_despues_de_horas_sin_simular_y_es_determinista()
    {
        var cfg = Config();
        var p = ResidentGenerator.Generate(cfg)[0];
        var a = new LifeSimulation(cfg).Advance(p, ResidentGenerator.InitialState(p, Start), Start.AddHours(6));
        var b = new LifeSimulation(cfg).Advance(p, ResidentGenerator.InitialState(p, Start), Start.AddHours(6));
        Assert.True(a.Until > Start.AddHours(6));
        Assert.True(a.Decisions > 3);
        Assert.Equal(a, b);
    }
}
