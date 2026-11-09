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
