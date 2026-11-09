using Distrito.Server.Core.World;

namespace Distrito.Server.Core.Tests;

/// <summary>Escenas de la ciudad: la configuración, las coreografías y el director.</summary>
public class CityScenesTests
{
    private static SceneConfig Config()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "config/escenas.json")))
            dir = dir.Parent;
        var (cfg, errors) = SceneConfig.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "config/escenas.json")));
        Assert.True(cfg is not null, string.Join(" | ", errors));
        return cfg!;
    }

    [Fact]
    public void La_configuracion_tiene_todas_las_escenas_y_cada_una_su_coreografia()
    {
        var cfg = Config();
        Assert.Equal(SceneScripts.Kinds.OrderBy(k => k), cfg.Escenas.Select(e => e.Codigo).OrderBy(k => k));
        Assert.Contains(SceneConfig.Parse("""{ "maxActivas": 1, "escenas": [ { "codigo": "inventada", "nombre": "x", "horas": [0, 24], "cadaMinutos": 1, "probabilidad": 1, "duracionMinutos": 1, "lugares": [[0, 0]], "frases": [] } ] }""").Errors,
            e => e.Contains("inventada"));
    }

    [Fact]
    public void Las_personas_de_cada_escena_andan_por_lugares_caminables_y_dicen_algo()
    {
        var cfg = Config();
        foreach (var def in cfg.Escenas)
        {
            var places = def.Global ? [new float[] { 0, 0 }] : def.Lugares;
            for (var p = 0; p < places.Length; p++)
            {
                var scene = new ActiveScene(1, def.Codigo, places[p][0], places[p][1], 1234 + p, 0, (long)(def.DuracionMinutos * 60_000));
                var timeline = SceneScripts.Build(def, scene);
                if (def.Codigo == "lluvia")
                {
                    Assert.Equal("lluvia", timeline.Weather);
                    continue;
                }
                if (def.Codigo == "apagon")
                {
                    Assert.True(timeline.Blackout > 0);
                    continue;
                }
                Assert.NotEmpty(timeline.Tracks);
                foreach (var track in timeline.Tracks.Where(t => t.Kind == "persona"))
                {
                    Assert.True(track.Keys.Zip(track.Keys.Skip(1)).All(k => k.Second.T >= k.First.T), $"{def.Codigo}: los tiempos van para atrás");
                    foreach (var k in track.Keys.Where((k, i) => !(i == 0 && k.Act == "")))
                        Assert.True(CityLayout.IsWalkable(k.X, k.Z), $"{def.Codigo} en {p}: {track.Look} pisa afuera de la ciudad en ({k.X:0.0}, {k.Z:0.0})");
                }
                // Alguien se ve en algún momento de la escena.
                Assert.Contains(timeline.Tracks, tr => Enumerable.Range(0, 20).Any(i => SceneScripts.Pose(tr, scene.DurationMs / 1000.0 * i / 20).Visible));
            }
        }
    }

    [Fact]
    public void El_director_respeta_horas_dias_y_maximo()
    {
        var cfg = Config();
        var director = new SceneDirector(cfg, seed: 7);
        var start = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        var seen = new HashSet<string>();
        for (var m = 0; m < 12 * 60; m += 1)
        {
            var now = start.AddMinutes(m);
            var (started, _) = director.Tick(now);
            foreach (var s in started)
            {
                var def = director.Find(s.Code)!;
                Assert.True(def.AtHour(Distrito.Shared.Life.BellmontClock.Hour(now)), $"{s.Code} fuera de hora");
                seen.Add(s.Code);
            }
            Assert.True(director.Active.Count(a => !director.Find(a.Code)!.Global) <= cfg.MaxActivas + 1);
            foreach (var g in director.Active.GroupBy(a => a.Code))
                Assert.True(g.Count() <= Math.Max(1, director.Find(g.Key)!.Maximo));
        }
        // En medio día de Bellmont (6 horas reales) pasan muchas escenas distintas.
        Assert.True(seen.Count >= 8, $"solo pasaron: {string.Join(", ", seen)}");
    }

    [Fact]
    public void La_pose_interpola_y_esconde_al_que_todavia_no_llego()
    {
        var track = new SceneTrack("persona", "vecino", [new(0, 0, 0, ""), new(10, 0, 0, "caminar"), new(20, 10, 0, "quieto", "Hola")]);
        Assert.False(SceneScripts.Pose(track, 5).Visible);
        var mid = SceneScripts.Pose(track, 15);
        Assert.True(mid.Visible);
        Assert.Equal(5, mid.X, 2);
        Assert.Equal(1, mid.Speed, 2);
        Assert.Equal("Hola", SceneScripts.Pose(track, 21).Say);
        Assert.Null(SceneScripts.Pose(track, 25).Say);
        Assert.False(SceneScripts.Pose(track, 30).Visible);
    }
}

/// <summary>Tránsito: recorridos por la calle y autos que frenan por la gente.</summary>
public class TrafficTests
{
    [Fact]
    public void Los_recorridos_van_por_la_calle_sin_edificios()
    {
        foreach (var route in TrafficRoutes.Routes)
            for (var d = 0f; d < TrafficRoutes.Length(route); d += 2f)
            {
                var (at, _) = TrafficRoutes.At(route, d);
                Assert.True(CityLayout.IsWalkable(at.X, at.Y), $"el tránsito sale de la ciudad en ({at.X:0}, {at.Y:0})");
                Assert.DoesNotContain(MapPresence.Buildings, b => MathF.Abs(at.X - b.X) < b.Width / 2 + 1f && MathF.Abs(at.Y - b.Z) < b.Depth / 2 + 1f);
                Assert.False(CityLayout.InsideWall(at.X, at.Y));
            }
    }

    [Fact]
    public void Un_auto_frena_si_hay_alguien_adelante_y_sigue_cuando_se_corre()
    {
        var sim = new TrafficSim();
        for (var i = 0; i < 40; i++)
            sim.Step(0.1f, []);
        var car = sim.Cars[0];
        Assert.True(car.Speed > 5);
        var person = car.Position + car.Heading * 6;
        for (var i = 0; i < 30; i++)
            sim.Step(0.1f, [person]);
        Assert.Equal(0, car.Speed, 2);
        Assert.True(System.Numerics.Vector2.Distance(car.Position, person) > 1.5f, "lo atropelló");
        for (var i = 0; i < 30; i++)
            sim.Step(0.1f, []);
        Assert.True(car.Speed > 3);
        var closed = new HashSet<int> { 0 };
        sim.Step(0.1f, [], closed);
        Assert.True(sim.Cars.Where(c => c.Route == 0).All(c => c.Parked && c.Speed == 0));
    }

    [Fact]
    public void En_el_tiroteo_y_la_extorsion_los_matones_se_pueden_volver_de_verdad()
    {
        var cfg = Config();
        var shoot = cfg.Escenas.Single(e => e.Codigo == "tiroteo");
        var s = new ActiveScene(1, "tiroteo", shoot.Lugares[0][0], shoot.Lugares[0][1], 42, 0, (long)(shoot.DuracionMinutos * 60_000));
        var fighters = SceneFights.Fighters("tiroteo", SceneScripts.Build(shoot, s));
        Assert.Equal(3, fighters.Count(f => f.Side < 0));
        Assert.Equal(3, fighters.Count(f => f.Side > 0));
        var (left, right) = SceneFights.ShootoutFamilies(s.Seed);
        Assert.Equal("caruso", left);
        Assert.NotEqual(left, right);

        var ext = cfg.Escenas.Single(e => e.Codigo == "extorsion");
        var e = new ActiveScene(2, "extorsion", ext.Lugares[0][0], ext.Lugares[0][1], 7, 0, (long)(ext.DuracionMinutos * 60_000));
        var timeline = SceneScripts.Build(ext, e);
        Assert.Equal(2, SceneFights.Fighters("extorsion", timeline).Count(f => f.Side == 0));
        Assert.Equal("comerciante", timeline.Tracks[0].Look); // el que se queda quieto si lo salvan
        var (from, to) = SceneFights.Window("extorsion", ext.DuracionMinutos * 60);
        Assert.True(from < to && to < ext.DuracionMinutos * 60);

        // La familia que cuida cada lugar es la del local de familia más cercano.
        Assert.Equal("moretti", SceneFights.ZoneFamily(-21.2f, 11));
        Assert.Equal("valenti", SceneFights.ZoneFamily(16, -11));
        // Ayudar a un lado = pegarle al otro.
        Assert.Equal(1, SceneFights.HelpedSide(hitsLeft: 3, hitsRight: 0));
        Assert.Equal(-1, SceneFights.HelpedSide(hitsLeft: 0, hitsRight: 2));
        Assert.Equal(0, SceneFights.HelpedSide(1, 1));
        Assert.Empty(SceneFights.Fighters("persecucion", new SceneTimeline([], [])));
    }

    [Fact]
    public void El_contrabando_sale_solo_con_el_evento_y_se_puede_terminar_antes()
    {
        var cfg = Config();
        var def = cfg.Escenas.Single(e => e.Codigo == "contrabando");
        Assert.Equal(0, def.Probabilidad);
        var tl = SceneScripts.Build(def, new ActiveScene(1, "contrabando", def.Lugares[0][0], def.Lugares[0][1], 3, 0, (long)(def.DuracionMinutos * 60_000)));
        Assert.DoesNotContain(tl.Tracks, t => t.Look == "maton"); // los guardias son los del evento de PvE
        var director = new SceneDirector(cfg);
        var scene = director.Start(def, 0);
        Assert.True(director.End(scene.Id));
        Assert.DoesNotContain(director.Active, a => a.Id == scene.Id);
        Assert.False(director.End(scene.Id));
    }
}
