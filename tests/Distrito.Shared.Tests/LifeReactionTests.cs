using Distrito.Shared.Life;

namespace Distrito.Shared.Tests;

/// <summary>Vida de los NPC, etapa 7: reacciones a tiros, heridos y asaltos.</summary>
public class LifeReactionTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Morning = Start.AddHours(5); // las 10 en Bellmont

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

    private static Resident Person(string profession, double fearful, double aggressive, double distrustful = 0.5, int index = 0)
    {
        var p = ResidentGenerator.Generate(Config()).Where(r => r.Profession == profession).ElementAt(index);
        return p with
        {
            Traits = new Dictionary<string, double>(p.Traits)
            {
                ["temeroso"] = fearful, ["agresivo"] = aggressive, ["pacifico"] = 1 - aggressive, ["desconfiado"] = distrustful,
            },
        };
    }

    /// <summary>Parado en la calle en (x, z), paseando hasta dentro de una hora.</summary>
    private static ResidentState OnStreet(Resident r, double x, double z, string activity = LifeActivities.Strolling) =>
        ResidentGenerator.InitialState(r, Morning) with
        {
            Activity = activity, Place = "kiosco", X = x, Z = z, FromX = x, FromZ = z, Indoor = false,
            Since = Morning.AddMinutes(-5), ArrivesAt = Morning.AddMinutes(-1), Until = Morning.AddHours(1),
        };

    private static LifeIncident ShotAt(double x, double z) => new(IncidentKinds.Shot, x, z, Morning);

    [Fact]
    public void El_miedoso_sale_corriendo_a_su_casa()
    {
        var sim = new LifeSimulation(Config());
        var r = Person("vecino", fearful: 0.95, aggressive: 0.05);
        var (after, outcome) = sim.React(r, OnStreet(r, 0, 0), ShotAt(4, 0));
        Assert.Equal("huyeron", outcome);
        Assert.Equal(LifeActivities.Fleeing, after.Activity);
        Assert.Equal(LifeSimulation.HomePlace, after.Place);
        Assert.True(after.Indoor);
        Assert.True(after.ScaredUntil > Morning);
        Assert.Contains("un tiro a 4 m", after.Reason);
        // Corre: tarda bastante menos que caminando.
        var walking = Math.Sqrt(r.HomeX * r.HomeX + r.HomeZ * r.HomeZ) / LifeSimulation.WalkSpeed;
        Assert.True((after.ArrivesAt - after.Since).TotalSeconds < walking * 0.6);
        Assert.Equal(OnStreet(r, 0, 0).Decisions + 1, after.Decisions); // se vuelve a mandar a los que lo ven
    }

    [Fact]
    public void El_duro_sigue_con_lo_suyo_y_el_que_confia_avisa_a_la_policia()
    {
        var sim = new LifeSimulation(Config());
        var tough = Person("vecino", fearful: 0.05, aggressive: 0.95, distrustful: 0.9);
        var (keeps, o1) = sim.React(tough, OnStreet(tough, 0, 0), ShotAt(30, 0));
        Assert.Equal("siguieron", o1);
        Assert.Equal(LifeActivities.Strolling, keeps.Activity);

        var calm = Person("vecino", fearful: 0.1, aggressive: 0.1, distrustful: 0.05, index: 1);
        var (warns, o2) = sim.React(calm, OnStreet(calm, 0, 0), ShotAt(40, 0));
        Assert.Equal("avisaron", o2);
        Assert.Contains("avisó a la policía", warns.Reason);
    }

    [Fact]
    public void A_media_distancia_se_meten_en_un_local_cerrado()
    {
        var cfg = Config();
        var sim = new LifeSimulation(cfg);
        var r = Person("vecino", fearful: 0.6, aggressive: 0.3);
        var diner = cfg.Place("diner")!;
        var (after, outcome) = sim.React(r, OnStreet(r, diner.X + 3, diner.Z + 3), ShotAt(diner.X + 20, diner.Z));
        Assert.Equal("escondidos", outcome);
        Assert.Equal(LifeActivities.Hiding, after.Activity);
        Assert.Equal("diner", after.Place);
        // El diner tiene vidriera y puerta de verdad: adentro se lo sigue viendo (los locales sin vidriera lo esconden).
        Assert.True(diner.Visible);
        Assert.False(after.Indoor);
    }

    [Fact]
    public void La_policia_de_turno_va_a_ver_y_no_arresta_a_nadie()
    {
        var sim = new LifeSimulation(Config());
        var cop = ResidentGenerator.Generate(Config()).First(r => r.Profession == "policia" && r.OnShift(10));
        var (after, outcome) = sim.React(cop, OnStreet(cop, 0, 0, LifeActivities.Patrolling), ShotAt(25, 10));
        Assert.Equal("policias", outcome);
        Assert.Equal(LifeActivities.Responding, after.Activity);
        Assert.True(Math.Abs(after.X - 25) < 8 && Math.Abs(after.Z - 10) < 8, $"no fue al lugar: ({after.X}, {after.Z})");
        Assert.Contains("va a ver qué pasó", after.Reason);
        // Avisado desde lejos, también va.
        var called = sim.Respond(cop, OnStreet(cop, -80, 0, LifeActivities.Patrolling), ShotAt(25, 10), "le avisaron");
        Assert.Equal(LifeActivities.Responding, called.Activity);
        Assert.True(called.ArrivesAt > called.Since);
    }

    [Fact]
    public void Los_lejanos_y_los_que_duermen_no_se_enteran_y_los_de_adentro_se_quedan()
    {
        var sim = new LifeSimulation(Config());
        var r = Person("vecino", fearful: 0.9, aggressive: 0.1);
        Assert.Null(sim.React(r, OnStreet(r, 0, 0), ShotAt(200, 0)).Outcome);
        var sleeping = OnStreet(r, 0, 0) with { Activity = LifeActivities.Sleeping, Place = LifeSimulation.HomePlace, Indoor = true };
        Assert.Null(sim.React(r, sleeping, ShotAt(5, 0)).Outcome);

        var inside = OnStreet(r, 0, 0) with { Activity = LifeActivities.AtHome, Place = LifeSimulation.HomePlace, Indoor = true, Until = Morning.AddMinutes(2) };
        var (stays, outcome) = sim.React(r, inside, ShotAt(10, 0));
        Assert.Equal("adentro", outcome);
        Assert.True(stays.Until > inside.Until);
        Assert.Contains("se queda adentro", stays.Reason);
    }

    [Fact]
    public void Despues_del_susto_prefiere_quedarse_en_casa_y_se_le_pasa()
    {
        var sim = new LifeSimulation(Config());
        var r = Person("vecino", fearful: 0.95, aggressive: 0.05);
        var (fled, _) = sim.React(r, OnStreet(r, 0, 0) with { Social = 0.2, Hunger = 0.1, Fatigue = 0.1 }, ShotAt(3, 0));
        var next = sim.Advance(r, fled, fled.Until);
        Assert.Equal(LifeActivities.AtHome, next.Activity);
        Assert.Contains("asustado", next.Reason);
        Assert.True(next.ScaredUntil < fled.Until.AddHours(3));
    }

    [Fact]
    public void Reacciona_igual_cada_vez()
    {
        var cfg = Config();
        var people = ResidentGenerator.Generate(cfg);
        string Run()
        {
            var sim = new LifeSimulation(cfg);
            return string.Join("|", people.Select((p, i) => sim.React(p, OnStreet(p, i % 6 * 5, i / 6 * 5), ShotAt(10, 10))).Select(x => $"{x.Outcome}:{x.State.Activity}:{x.State.Place}"));
        }
        var first = Run();
        Assert.Equal(first, Run());
        Assert.True(first.Split('|').Select(x => x.Split(':')[0]).Distinct().Count() >= 3, $"todos reaccionan parecido: {first}");
    }
}
