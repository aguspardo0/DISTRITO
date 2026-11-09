using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Distrito.Shared.Life;

namespace Distrito.Server.Core.World;

/// <summary>
/// Escenas de la ciudad (pedido del equipo, 2026-11-09: «más vida, eventos, secuencias»). Cosas que pasan solas según
/// la hora de Bellmont, el día y el lugar: el canillita que grita La Gaceta, un camión que descarga en el Almacén, dos
/// vecinos que discuten, un borracho que sale del bar, la lluvia, la feria, un barco que llega al puerto, una redada,
/// una persecución, un tiroteo entre familias… Se editan sin tocar código en <c>config/escenas.json</c>.
///
/// Cómo funciona: el servidor decide qué escena empieza, dónde y con qué semilla (<see cref="SceneDirector"/>), y avisa
/// a todos. Cada equipo arma la misma coreografía a partir de la semilla (<see cref="SceneScripts"/>): quién camina por
/// dónde, qué dice, qué suena. Así no hay que mandar posiciones por la red y se ve igual en todos lados.
/// Son ambientación: no tocan la economía. Las que tienen tiros avisan a los habitantes (huyen y llaman a la policía).
/// </summary>
public sealed record SceneDef(
    string Codigo, string Nombre, int[] Horas, double CadaMinutos, double Probabilidad, double DuracionMinutos,
    float[][] Lugares, string[] Frases, bool Global = false, int[]? Dias = null, int Maximo = 1)
{
    /// <summary>¿Puede pasar a esta hora de Bellmont? (Horas = [desde, hasta); si desde &gt; hasta, cruza la medianoche.)</summary>
    public bool AtHour(int hour) => Horas.Length < 2 || (Horas[0] <= Horas[1] ? hour >= Horas[0] && hour < Horas[1] : hour >= Horas[0] || hour < Horas[1]);
}

public sealed record SceneConfig(int MaxActivas, List<SceneDef> Escenas)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static (SceneConfig? Config, List<string> Errors) Parse(string json)
    {
        var errors = new List<string>();
        SceneConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<SceneConfig>(json, Options);
        }
        catch (JsonException e)
        {
            return (null, [$"escenas.json: {e.Message}"]);
        }
        if (cfg?.Escenas is null)
            return (null, ["escenas.json: falta la lista «escenas»"]);
        foreach (var s in cfg.Escenas)
        {
            if (!SceneScripts.Kinds.Contains(s.Codigo))
                errors.Add($"{s.Codigo}: no hay coreografía con ese código (hay: {string.Join(", ", SceneScripts.Kinds)})");
            if (s.DuracionMinutos <= 0 || s.CadaMinutos <= 0)
                errors.Add($"{s.Codigo}: la duración y «cadaMinutos» tienen que ser mayores que cero");
            if (s.Probabilidad is < 0 or > 1)
                errors.Add($"{s.Codigo}: la probabilidad va de 0 a 1");
            if (!s.Global && (s.Lugares is null || s.Lugares.Length == 0 || s.Lugares.Any(p => p.Length < 2)))
                errors.Add($"{s.Codigo}: faltan los lugares ([x, z])");
            if (s.Horas is { Length: > 0 } && (s.Horas.Length != 2 || s.Horas.Any(h => h is < 0 or > 24)))
                errors.Add($"{s.Codigo}: «horas» es [desde, hasta] entre 0 y 24");
        }
        return (errors.Count == 0 ? cfg : null, errors);
    }
}

/// <summary>Una escena en curso: qué, dónde, con qué semilla y desde cuándo (milisegundos Unix del servidor).</summary>
public sealed record ActiveScene(long Id, string Code, float X, float Z, int Seed, long StartMs, long DurationMs)
{
    public long EndMs => StartMs + DurationMs;
    public double Seconds(long nowMs) => (nowMs - StartMs) / 1000.0;
}

/// <summary>Elige qué escenas empiezan. Determinista con la semilla del servidor (para las pruebas).</summary>
public sealed class SceneDirector(SceneConfig config, int seed = 1952)
{
    private readonly Random _rng = new(seed);
    private readonly Dictionary<string, long> _nextTry = new();
    private readonly List<ActiveScene> _active = [];
    private long _nextId = 1;

    public IReadOnlyList<ActiveScene> Active => _active;
    public SceneConfig Config => config;

    /// <summary>Una vez por segundo, más o menos: termina las vencidas y prueba suerte con las que tocan.</summary>
    public (List<ActiveScene> Started, List<ActiveScene> Ended) Tick(DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        var ended = _active.Where(a => a.EndMs <= ms).ToList();
        _active.RemoveAll(a => a.EndMs <= ms);
        var started = new List<ActiveScene>();
        var hour = BellmontClock.Hour(now);
        var weekday = (int)(((BellmontClock.Day(now) % 7) + 7) % 7); // 0 = domingo
        foreach (var def in config.Escenas)
        {
            if (_nextTry.TryGetValue(def.Codigo, out var next) && ms < next)
                continue;
            _nextTry[def.Codigo] = ms + (long)(def.CadaMinutos * 60_000);
            if (!def.AtHour(hour) || def.Dias is { Length: > 0 } && !def.Dias.Contains(weekday))
                continue;
            if (_active.Count >= config.MaxActivas && !def.Global)
                continue;
            if (_active.Count(a => a.Code == def.Codigo) >= Math.Max(1, def.Maximo))
                continue;
            if (_rng.NextDouble() > def.Probabilidad)
                continue;
            started.Add(Start(def, ms));
        }
        return (started, ended);
    }

    /// <summary>Empieza una escena ya (la usan el panel de administración y las capturas de prueba).</summary>
    public ActiveScene Start(SceneDef def, long nowMs, int? place = null)
    {
        // No repite un lugar ocupado por otra escena si hay otro libre.
        var free = Enumerable.Range(0, Math.Max(1, def.Lugares?.Length ?? 0))
            .Where(i => def.Global || def.Lugares is null || !_active.Any(a => Vector2.Distance(new(a.X, a.Z), new(def.Lugares[i][0], def.Lugares[i][1])) < 12))
            .ToList();
        var index = place ?? (free.Count > 0 ? free[_rng.Next(free.Count)] : _rng.Next(Math.Max(1, def.Lugares?.Length ?? 1)));
        var at = def.Global || def.Lugares is null || def.Lugares.Length == 0 ? new float[] { 0, 0 } : def.Lugares[Math.Clamp(index, 0, def.Lugares.Length - 1)];
        var scene = new ActiveScene(_nextId++, def.Codigo, at[0], at[1], _rng.Next(1, int.MaxValue), nowMs, (long)(def.DuracionMinutos * 60_000));
        _active.Add(scene);
        return scene;
    }

    public SceneDef? Find(string code) => config.Escenas.FirstOrDefault(s => s.Codigo == code);
}

/// <summary>Un momento de la coreografía de alguien: a qué segundo está dónde, qué hace y qué dice.</summary>
public sealed record SceneKey(double T, float X, float Z, string Act = "", string? Say = null);

/// <summary>
/// Alguien (o algo) de la escena. <see cref="Kind"/>: «persona», «auto», «camioneta», «camion», «policia» (auto con
/// sirena), «funebre», «barco», «pelota», «puesto» (mesa con toldo), «cajon». <see cref="Look"/>: estilo de la persona
/// (vecino, vecina, policia, canillita, matón, obrero, músico…). <see cref="Scale"/>: los chicos son más chicos.
/// </summary>
public sealed record SceneTrack(string Kind, string Look, List<SceneKey> Keys, float Scale = 1f, int Variant = 0);

public sealed record SceneSound(double T, string Sound, float X, float Z);

/// <summary>Toda la coreografía. <see cref="Weather"/>: «lluvia» si llueve. <see cref="Blackout"/>: radio sin luz.</summary>
public sealed record SceneTimeline(List<SceneTrack> Tracks, List<SceneSound> Sounds, string? Weather = null, float Blackout = 0, List<(double T, float X, float Z, string Kind)>? Incidents = null);

/// <summary>Dónde está alguien de la escena en un segundo dado (interpolando entre sus momentos).</summary>
public readonly record struct ScenePose(float X, float Z, float Yaw, float Speed, string Act, string? Say, bool Visible);

/// <summary>Las coreografías: cada código arma su escena a partir del lugar y la semilla. Iguales en todos lados.</summary>
public static class SceneScripts
{
    public static readonly string[] Kinds =
    [
        "canillita", "descarga", "discusion", "borracho", "lustrabotas", "chicos", "musico", "paseo",
        "lluvia", "feria", "apagon", "barco", "redada", "funeral",
        "persecucion", "tiroteo", "extorsion", "contrabando",
    ];

    public static SceneTimeline Build(SceneDef def, ActiveScene s)
    {
        var rng = new Random(s.Seed);
        var d = s.DurationMs / 1000.0;
        var at = new Vector2(s.X, s.Z);
        var lines = def.Frases is { Length: > 0 } ? def.Frases : ["…"];
        string Line(int i) => lines[(i % lines.Length + lines.Length) % lines.Length];
        return s.Code switch
        {
            "canillita" => Canillita(at, d, rng, Line),
            "descarga" => Unload(at, d, rng, Line),
            "discusion" => Argument(at, d, rng, Line),
            "borracho" => Drunk(at, d, rng, Line),
            "lustrabotas" => Shoeshine(at, d, rng, Line),
            "chicos" => Kids(at, d, rng, Line),
            "musico" => Musician(at, d, rng, Line),
            "paseo" => Stroll(at, d, rng, Line),
            "lluvia" => new SceneTimeline([], [new(0, "trueno", 0, 0), new(d * 0.4, "trueno", 0, 0)], Weather: "lluvia"),
            "feria" => Fair(at, d, rng, Line),
            "apagon" => new SceneTimeline([], [new(0, "chispa", s.X, s.Z)], Blackout: 140),
            "barco" => Ship(at, d, rng, Line),
            "redada" => Raid(at, d, rng, Line),
            "funeral" => Funeral(at, d, rng, Line),
            "persecucion" => Chase(at, d, rng, Line),
            "tiroteo" => Shootout(at, d, rng, Line),
            "extorsion" => Extortion(at, d, rng, Line),
            "contrabando" => Smuggling(at, d, rng, Line),
            _ => new SceneTimeline([], []),
        };
    }

    // ---------------- Calle ----------------

    /// <summary>El canillita va y viene por la vereda y cada tanto se para a gritar un titular.</summary>
    private static SceneTimeline Canillita(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var keys = new List<SceneKey>();
        var t = 0.0;
        var x = at.X;
        var dir = rng.Next(2) == 0 ? 1 : -1;
        var i = rng.Next(100);
        while (t < d)
        {
            keys.Add(new(t, x, at.Y, "caminar"));
            var nx = Math.Clamp(x + dir * (8 + (float)rng.NextDouble() * 6), at.X - 18, at.X + 18);
            t += Math.Abs(nx - x) / 1.4;
            x = nx;
            keys.Add(new(t, x, at.Y, "quieto", line(i++)));
            t += 5;
            if (Math.Abs(x - at.X) >= 17.9f) dir = -dir;
            else if (rng.Next(3) == 0) dir = -dir;
        }
        return new([new("persona", "canillita", keys, 0.82f)], []);
    }

    /// <summary>Un camión llega, estaciona contra el cordón y dos changarines bajan cajones hasta la puerta.</summary>
    private static SceneTimeline Unload(Vector2 door, double d, Random rng, Func<int, string> line)
    {
        var side = door.Y < 0 ? -1f : 1f;           // vereda norte o sur
        var curb = new Vector2(door.X, side * 4.4f);
        var lane = side * 3.2f;
        var arrive = 10.0;
        var leave = d - 12;
        var from = door.X + side * 70;               // llega por su mano
        var truck = new List<SceneKey> { new(0, from, lane, "andar"), new(arrive, curb.X, curb.Y, "frenar"), new(leave, curb.X, curb.Y, "andar"), new(d, door.X - side * 70, lane, "andar") };
        var tracks = new List<SceneTrack> { new("camion", "reparto", truck) };
        for (var w = 0; w < 2; w++)
        {
            var keys = new List<SceneKey> { new(0, curb.X + 1 - w * 2, curb.Y + side * 1.3f, "", null) };
            var t = arrive + 3 + w * 6;
            keys.Add(new(t, curb.X + 2.5f - w * 5, curb.Y + side * 1.6f, "quieto", w == 0 ? line(rng.Next(10)) : null));
            var trips = 0;
            while (t + 14 < leave - 2)
            {
                keys.Add(new(t + 1, curb.X + 2.5f - w * 5, curb.Y + side * 1.6f, "cargar"));
                keys.Add(new(t + 7, door.X + (w == 0 ? -0.8f : 0.8f), door.Y - side * 1.0f, "cargar"));
                keys.Add(new(t + 8, door.X + (w == 0 ? -0.8f : 0.8f), door.Y - side * 1.0f, "quieto", trips % 3 == 2 && w == 1 ? line(trips + 1) : null));
                keys.Add(new(t + 14, curb.X + 2.5f - w * 5, curb.Y + side * 1.6f, "caminar"));
                t += 14;
                trips++;
            }
            keys.Add(new(leave - 1, curb.X + 2.5f - w * 5, curb.Y + side * 1.6f, "quieto"));
            keys.Add(new(leave, curb.X, curb.Y, "subir")); // suben al camión
            tracks.Add(new("persona", "obrero", keys, 1f, w));
        }
        return new(tracks, [new(arrive - 2, "bocina", curb.X, curb.Y), new(leave, "motor", curb.X, curb.Y)]);
    }

    /// <summary>Dos vecinos discuten; se juntan curiosos; al final uno se va enojado.</summary>
    private static SceneTimeline Argument(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var a = at + new Vector2(-0.8f, 0);
        var b = at + new Vector2(0.8f, 0);
        var ka = new List<SceneKey> { new(0, a.X - 6, a.Y, "caminar") };
        var kb = new List<SceneKey> { new(0, b.X + 6, b.Y, "caminar") };
        ka.Add(new(4, a.X, a.Y, "quieto"));
        kb.Add(new(4, b.X, b.Y, "quieto"));
        var i = rng.Next(100);
        for (var t = 5.0; t < d - 8; t += 4.5)
        {
            var who = ((int)(t / 4.5)) % 2 == 0;
            (who ? ka : kb).Add(new(t, who ? a.X : b.X, at.Y, "gesto", line(i++)));
        }
        ka.Add(new(d - 7, a.X, a.Y, "quieto", "¡Esto no termina acá!"));
        ka.Add(new(d, a.X - 12, a.Y + 0.5f, "caminar"));
        kb.Add(new(d, b.X, b.Y, "quieto"));
        var tracks = new List<SceneTrack> { new("persona", "vecino", ka, 1f, 1), new("persona", "vecino", kb, 1f, 2) };
        var watchers = 2 + rng.Next(3);
        for (var w = 0; w < watchers; w++)
        {
            // Los curiosos se paran del lado de la calzada (no contra las paredes).
            var ang = Math.PI * (0.15 + 0.7 * w / Math.Max(1, watchers - 1));
            var towardRoad = at.Y < 0 ? 1f : -1f;
            var spot = at + new Vector2((float)Math.Cos(ang) * 3.2f, (float)Math.Abs(Math.Sin(ang)) * 2.4f * towardRoad);
            var start = 15 + w * 6;
            var far = spot + new Vector2((float)Math.Cos(ang) * 14, 0);
            tracks.Add(new("persona", w % 2 == 0 ? "vecina" : "vecino", [new(0, far.X, spot.Y, ""), new(start, far.X, spot.Y, "caminar"), new(start + 8, spot.X, spot.Y, "mirar"), new(d - 4, spot.X, spot.Y, "mirar"), new(d + 6, far.X, spot.Y, "caminar")], 1f, w + 3));
        }
        return new(tracks, []);
    }

    /// <summary>Un borracho sale tambaleando y canta.</summary>
    private static SceneTimeline Drunk(Vector2 door, double d, Random rng, Func<int, string> line)
    {
        var keys = new List<SceneKey> { new(0, door.X, door.Y, "tambalear") };
        var side = door.Y < 0 ? 1f : -1f; // se aleja de la puerta hacia la calzada
        var x = door.X;
        var i = rng.Next(100);
        for (var t = 2.0; t < d; t += 2.2)
        {
            x += (float)(rng.NextDouble() - 0.4) * 2.2f;
            var z = door.Y + side * (1.6f + (float)rng.NextDouble() * 1.8f);
            keys.Add(new(t, x, z, "tambalear", rng.Next(4) == 0 ? line(i++) : null));
        }
        return new([new("persona", "borracho", keys)], []);
    }

    /// <summary>El lustrabotas en la esquina; de a uno llegan clientes, esperan y se van.</summary>
    private static SceneTimeline Shoeshine(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var tracks = new List<SceneTrack>
        {
            new("persona", "lustrabotas", [new(0, at.X, at.Y, "agachado", line(0)), new(d, at.X, at.Y, "agachado")], 0.8f),
            new("cajon", "lustre", [new(0, at.X, at.Y - 0.7f, ""), new(d, at.X, at.Y - 0.7f, "")]),
        };
        var keysBoy = tracks[0].Keys;
        var n = 0;
        for (var t = 8.0; t + 40 < d; t += 55 + rng.Next(20))
        {
            var from = at + new Vector2(rng.Next(2) == 0 ? -20 : 20, 0);
            var spot = at + new Vector2(0, -1.3f);
            tracks.Add(new("persona", "ejecutivo", [new(0, from.X, from.Y, ""), new(t, from.X, from.Y, "caminar"), new(t + 12, spot.X, spot.Y, "quieto"),
                new(t + 34, spot.X, spot.Y, "quieto", "Gracias, pibe."), new(t + 48, at.X + (from.X > at.X ? -20 : 20), from.Y, "caminar")], 1f, n++));
            keysBoy.Add(new(t + 13, at.X, at.Y, "agachado", line(n)));
        }
        keysBoy.Sort((a, b) => a.T.CompareTo(b.T));
        return new(tracks, []);
    }

    /// <summary>Chicos que corren detrás de una pelota y gritan.</summary>
    private static SceneTimeline Kids(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var ball = new List<SceneKey>();
        var kids = Enumerable.Range(0, 3).Select(_ => new List<SceneKey>()).ToList();
        var i = rng.Next(100);
        for (var t = 0.0; t < d; t += 2.5)
        {
            var p = at + new Vector2((float)(rng.NextDouble() - 0.5) * 9, (float)(rng.NextDouble() - 0.5) * 5);
            ball.Add(new(t, p.X, p.Y, ""));
            for (var k = 0; k < 3; k++)
            {
                var off = new Vector2((float)Math.Cos(k * 2.1 + t) * 1.4f, (float)Math.Sin(k * 2.1 + t) * 1.4f);
                kids[k].Add(new(t + 0.6 + k * 0.3, p.X + off.X, p.Y + off.Y, "correr", rng.Next(8) == 0 ? line(i++) : null));
            }
        }
        var tracks = new List<SceneTrack> { new("pelota", "", ball) };
        tracks.AddRange(kids.Select((k, n) => new SceneTrack("persona", n == 1 ? "nena" : "chico", k, 0.68f, n)));
        return new(tracks, []);
    }

    /// <summary>Un músico callejero toca; la gente se para un rato a escuchar.</summary>
    private static SceneTimeline Musician(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var tracks = new List<SceneTrack> { new("persona", "musico", [new(0, at.X, at.Y, "tocar", line(0)), new(d / 2, at.X, at.Y, "tocar", line(1)), new(d, at.X, at.Y, "tocar")]) };
        var sounds = new List<SceneSound>();
        for (var t = 0.0; t < d; t += 16)
            sounds.Add(new(t, "acordeon", at.X, at.Y));
        for (var n = 0; n < 4; n++)
        {
            var t = 10 + n * 40 + rng.Next(15);
            if (t + 30 > d) break;
            var spot = at + new Vector2(-2.5f + n * 1.6f, at.Y < 0 ? 2.2f : -2.2f);
            var from = spot + new Vector2(n % 2 == 0 ? -18 : 18, 0);
            tracks.Add(new("persona", n % 2 == 0 ? "vecina" : "vecino", [new(0, from.X, from.Y, ""), new(t, from.X, from.Y, "caminar"), new(t + 10, spot.X, spot.Y, "mirar"), new(t + 28, spot.X, spot.Y, "aplaudir"), new(t + 40, spot.X - (from.X - spot.X), spot.Y, "caminar")], 1f, n + 7));
        }
        return new(tracks, sounds);
    }

    /// <summary>Una familia pasea despacio por la vereda (los domingos).</summary>
    private static SceneTimeline Stroll(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var dir = rng.Next(2) == 0 ? 1f : -1f;
        var length = (float)(d * 0.9);
        SceneTrack Walker(string look, float lateral, float behind, float scale, int v) =>
            new("persona", look, [new(0, at.X - dir * (length / 2 + behind), at.Y + lateral, "caminar", v == 0 ? line(0) : null), new(d, at.X + dir * (length / 2 - behind), at.Y + lateral, "caminar", null)], scale, v);
        return new([Walker("vecino", -0.5f, 0, 1, 0), Walker("vecina", 0.5f, 0, 1, 1), Walker("chico", 0, 1.6f, 0.68f, 2), Walker("nena", 0.7f, 1.4f, 0.65f, 3)], []);
    }

    // ---------------- Eventos ----------------

    /// <summary>Feria en la Calle Principal: puestos en la calzada y gente que va de uno a otro.</summary>
    private static SceneTimeline Fair(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var tracks = new List<SceneTrack>();
        var stalls = new List<Vector2>();
        for (var i = 0; i < 6; i++)
        {
            var p = at + new Vector2(-30 + i * 12, i % 2 == 0 ? -2.5f : 2.5f);
            stalls.Add(p);
            tracks.Add(new("puesto", "feria", [new(0, p.X, p.Y, ""), new(d, p.X, p.Y, "")], 1f, i));
            tracks.Add(new("persona", i % 2 == 0 ? "vecina" : "vecino", [new(0, p.X, p.Y + (p.Y < 0 ? -1.2f : 1.2f), "quieto", line(i)), new(d / 2, p.X, p.Y + (p.Y < 0 ? -1.2f : 1.2f), "gesto", line(i + 6)), new(d, p.X, p.Y + (p.Y < 0 ? -1.2f : 1.2f), "quieto")], 1f, 20 + i));
        }
        for (var n = 0; n < 10; n++)
        {
            var keys = new List<SceneKey>();
            for (var t = 0.0; t < d; t += 18 + rng.Next(12))
            {
                var s = stalls[rng.Next(stalls.Count)];
                keys.Add(new(t, s.X + (float)(rng.NextDouble() - 0.5) * 3, s.Y * -0.2f, "caminar"));
                keys.Add(new(t + 8, s.X + (float)(rng.NextDouble() - 0.5) * 2, s.Y * 0.35f, "mirar"));
            }
            tracks.Add(new("persona", n % 3 == 0 ? "vecina" : n % 3 == 1 ? "vecino" : "obrero", keys, n % 5 == 4 ? 0.68f : 1f, 30 + n));
        }
        var sounds = new List<SceneSound>();
        for (var t = 0.0; t < d; t += 16)
            sounds.Add(new(t, "acordeon", at.X, at.Y));
        return new(tracks, sounds);
    }

    /// <summary>Un barco entra al puerto, toca la bocina, descargan y se va.</summary>
    private static SceneTimeline Ship(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var dock = new Vector2(at.X, at.Y); // donde amarra, en el agua
        var ship = new List<SceneKey> { new(0, dock.X + 160, dock.Y, "navegar"), new(60, dock.X, dock.Y, "frenar"), new(d - 60, dock.X, dock.Y, "navegar"), new(d, dock.X + 160, dock.Y, "navegar") };
        var tracks = new List<SceneTrack> { new("barco", "carguero", ship) };
        for (var w = 0; w < 3; w++)
        {
            var pier = new Vector2(CityLayout.Docks.MaxX - 2.5f, dock.Y - 4 + w * 4);
            var shed = new Vector2(CityLayout.Docks.MaxX - 18, dock.Y - 4 + w * 4);
            var keys = new List<SceneKey> { new(0, shed.X, shed.Y, "quieto") };
            for (var t = 70.0 + w * 4; t + 30 < d - 60; t += 28)
            {
                keys.Add(new(t, pier.X, pier.Y, "caminar"));
                keys.Add(new(t + 2, pier.X, pier.Y, "cargar", t < 120 && w == 0 ? line(0) : null));
                keys.Add(new(t + 14, shed.X, shed.Y, "cargar"));
                keys.Add(new(t + 15, shed.X, shed.Y, "quieto"));
            }
            tracks.Add(new("persona", "obrero", keys, 1f, 40 + w));
        }
        return new(tracks, [new(58, "sirena-barco", dock.X + 20, dock.Y), new(d - 62, "sirena-barco", dock.X + 20, dock.Y)]);
    }

    /// <summary>Redada: llegan dos patrulleros con sirena, los agentes rodean la puerta y después se van.</summary>
    private static SceneTimeline Raid(Vector2 door, double d, Random rng, Func<int, string> line)
    {
        var side = door.Y < 0 ? -1f : 1f;
        var tracks = new List<SceneTrack>();
        var sounds = new List<SceneSound>();
        for (var c = 0; c < 2; c++)
        {
            var stop = new Vector2(door.X - 4 + c * 8, side * 4.2f);
            var from = door.X + side * (80 + c * 12);
            tracks.Add(new("policia", "patrullero", [new(0, from, side * 3.2f, "andar"), new(14 + c * 2, stop.X, stop.Y, "frenar"), new(d - 15, stop.X, stop.Y, "andar"), new(d, door.X - side * 90, side * 3.2f, "andar")], 1f, c));
            sounds.Add(new(2 + c, "sirena", from, side * 3.2f));
        }
        for (var a = 0; a < 4; a++)
        {
            var car = new Vector2(door.X - 4 + (a / 2) * 8, side * 5.6f);
            var post = door + new Vector2(-2.4f + a * 1.6f, -side * 1.5f);
            tracks.Add(new("persona", "policia", [new(0, car.X, car.Y, ""), new(17, car.X, car.Y, "caminar"), new(24, post.X, post.Y, "quieto", a == 0 ? line(0) : null),
                new(d / 2, post.X, post.Y, "quieto", a == 1 ? line(1) : null), new(d - 22, post.X, post.Y, "quieto"), new(d - 16, car.X, car.Y, "caminar"), new(d - 15, car.X, car.Y, "subir")], 1f, a));
        }
        tracks.Add(new("persona", "comerciante", [new(0, door.X, door.Y - side * 0.8f, ""), new(26, door.X, door.Y - side * 0.8f, "gesto", line(2)), new(d - 20, door.X, door.Y - side * 0.8f, "quieto", line(3)), new(d - 10, door.X, door.Y + side * 0.6f, "caminar")]));
        return new(tracks, sounds);
    }

    /// <summary>Cortejo fúnebre: el auto negro va a paso de hombre por la calle y detrás la gente de luto.</summary>
    private static SceneTimeline Funeral(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var from = at.X - 70;
        var to = at.X + 70;
        var tracks = new List<SceneTrack> { new("funebre", "funebre", [new(0, from, 1.6f, "andar"), new(d, to, 1.6f, "andar")]) };
        for (var p = 0; p < 9; p++)
        {
            var behind = 5 + (p / 3) * 1.6f;
            var lateral = -1.2f + (p % 3) * 1.2f;
            tracks.Add(new("persona", p % 3 == 1 ? "luto-mujer" : "luto", [new(0, from - behind, 1.6f + lateral, "caminar", p == 4 ? line(0) : null), new(d, to - behind, 1.6f + lateral, "caminar")], 1f, 50 + p));
        }
        return new(tracks, [new(0, "campana", at.X, at.Y), new(d / 2, "campana", at.X, at.Y)]);
    }

    // ---------------- Crimen ----------------

    /// <summary>Persecución: un auto a toda velocidad por la Calle Principal y la Avenida del Puerto, con el patrullero atrás.</summary>
    private static SceneTimeline Chase(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var east = rng.Next(2) == 0;
        float a = east ? -25 : 240, b = east ? 240 : -25;
        var lane = east ? 3.2f : -3.2f;
        var run = Math.Min(d - 6, 28.0);
        var getaway = new List<SceneKey> { new(0, a, lane, "andar"), new(run, b, lane, "andar") };
        var police = new List<SceneKey> { new(0, a, lane, ""), new(3, a, lane, "andar"), new(run + 3, b, lane, "andar") };
        var tracks = new List<SceneTrack> { new("auto", "sedan", getaway, 1f, rng.Next(4)), new("policia", "patrullero", police) };
        var sounds = new List<SceneSound> { new(0, "chirrido", a, lane), new(3, "sirena", a, lane), new(run / 2 + 3, "sirena", (a + b) / 2, lane) };
        var incidents = new List<(double, float, float, string)> { (run * 0.3, a + (b - a) * 0.3f, lane, "asalto") };
        return new(tracks, sounds, Incidents: incidents);
    }

    /// <summary>Tiroteo entre gente de dos familias detrás de autos; uno cae, los demás escapan; después llega la policía.</summary>
    private static SceneTimeline Shootout(Vector2 at, double d, Random rng, Func<int, string> line)
    {
        var tracks = new List<SceneTrack>();
        var sounds = new List<SceneSound>();
        var incidents = new List<(double, float, float, string)>();
        var fight = Math.Min(50, d * 0.5);
        for (var side = -1; side <= 1; side += 2)
        {
            var car = at + new Vector2(side * 11, 0);
            tracks.Add(new("auto", "sedan", [new(0, car.X + side * 40, car.Y, "andar"), new(6, car.X, car.Y, "frenar"), new(d, car.X, car.Y, "")], 1f, side + 2));
            for (var g = 0; g < 3; g++)
            {
                var cover = car + new Vector2(side * 1.8f, -1.6f + g * 1.6f);
                var keys = new List<SceneKey> { new(0, car.X + side * 40, car.Y, ""), new(6, car.X, car.Y, ""), new(8, cover.X, cover.Y, "apuntar", g == 0 ? line(side + 1) : null) };
                for (var t = 10.0 + g; t < fight; t += 3 + rng.Next(3))
                    keys.Add(new(t, cover.X, cover.Y, "disparar"));
                if (side == 1 && g == 1)
                {
                    keys.Add(new(fight, cover.X, cover.Y, "caido"));
                    keys.Add(new(d, cover.X, cover.Y, "caido"));
                }
                else
                {
                    keys.Add(new(fight, cover.X, cover.Y, "correr"));
                    keys.Add(new(fight + 9, cover.X + side * 30, cover.Y + (g - 1) * 6, "correr"));
                }
                tracks.Add(new("persona", "maton", keys, 1f, side * 10 + g));
            }
        }
        for (var t = 10.0; t < fight; t += 0.9 + rng.NextDouble())
            sounds.Add(new(t, "tiro", at.X + (rng.Next(2) == 0 ? -12 : 12), at.Y));
        incidents.Add((10, at.X, at.Y, "tiro"));
        incidents.Add((fight, at.X + 13, at.Y, "herido"));
        // Llega la policía.
        var cop = new Vector2(at.X - 30, at.Y < 0 ? -3.2f : 3.2f);
        tracks.Add(new("policia", "patrullero", [new(0, cop.X - 80, cop.Y, ""), new(fight + 5, cop.X - 80, cop.Y, "andar"), new(fight + 18, cop.X, cop.Y, "frenar"), new(d, cop.X, cop.Y, "")]));
        sounds.Add(new(fight + 4, "sirena", cop.X - 60, cop.Y));
        for (var a = 0; a < 2; a++)
            tracks.Add(new("persona", "policia", [new(0, cop.X, cop.Y + 1.5f, ""), new(fight + 20, cop.X, cop.Y + 1.5f, "caminar"), new(fight + 30, at.X + 11, at.Y + a * 1.4f, "quieto", a == 0 ? line(2) : null), new(d, at.X + 11, at.Y + a * 1.4f, "quieto")], 1f, a + 60));
        return new(tracks, sounds, Incidents: incidents);
    }

    /// <summary>Dos matones le «piden colaboración» a un comerciante; él les da un sobre y se van.</summary>
    private static SceneTimeline Extortion(Vector2 door, double d, Random rng, Func<int, string> line)
    {
        var side = door.Y < 0 ? 1f : -1f; // hacia la calzada
        var shop = door + new Vector2(0, side * 0.9f);
        var tracks = new List<SceneTrack>
        {
            new("persona", "comerciante", [new(0, shop.X, shop.Y, "quieto"), new(14, shop.X, shop.Y, "quieto", line(1)), new(32, shop.X, shop.Y, "gesto", line(3)), new(d, shop.X, shop.Y, "quieto")]),
        };
        for (var g = 0; g < 2; g++)
        {
            var spot = shop + new Vector2(-1 + g * 2, side * 1.6f);
            var from = spot + new Vector2(g == 0 ? -25 : 25, side * 1.5f);
            tracks.Add(new("persona", "maton", [new(0, from.X, from.Y, ""), new(1, from.X, from.Y, "caminar"), new(10, spot.X, spot.Y, "quieto", g == 0 ? line(0) : null),
                new(24, spot.X, spot.Y, "gesto", g == 1 ? line(2) : null), new(38, spot.X, spot.Y, "quieto", g == 0 ? line(4) : null), new(d, from.X - (g == 0 ? -50 : 50), from.Y, "caminar")], 1f, g + 70));
        }
        return new(tracks, []);
    }

    /// <summary>Contrabando de noche: una lancha en el muelle, cajones a una camioneta, uno vigila.</summary>
    private static SceneTimeline Smuggling(Vector2 pier, double d, Random rng, Func<int, string> line)
    {
        var tracks = new List<SceneTrack>
        {
            new("barco", "lancha", [new(0, pier.X + 60, pier.Y, "navegar"), new(25, pier.X + 4, pier.Y, "frenar"), new(d - 25, pier.X + 4, pier.Y, "navegar"), new(d, pier.X + 60, pier.Y, "navegar")]),
            new("camioneta", "contrabando", [new(0, pier.X - 18, pier.Y + 3, "frenar"), new(d - 10, pier.X - 18, pier.Y + 3, "andar"), new(d, pier.X - 80, pier.Y + 3, "andar")]),
            new("persona", "maton", [new(0, pier.X - 12, pier.Y - 7, "mirar", line(0)), new(d / 2, pier.X - 12, pier.Y - 7, "mirar", line(1)), new(d, pier.X - 12, pier.Y - 7, "mirar")], 1f, 80),
        };
        for (var w = 0; w < 2; w++)
        {
            var keys = new List<SceneKey> { new(0, pier.X - 15, pier.Y + 1 + w, "quieto") };
            for (var t = 30.0 + w * 5; t + 22 < d - 12; t += 20)
            {
                keys.Add(new(t, pier.X - 1, pier.Y + w, "caminar"));
                keys.Add(new(t + 2, pier.X - 1, pier.Y + w, "cargar"));
                keys.Add(new(t + 10, pier.X - 16, pier.Y + 2 + w, "cargar"));
                keys.Add(new(t + 11, pier.X - 16, pier.Y + 2 + w, "quieto", w == 1 && t < 60 ? line(2) : null));
            }
            tracks.Add(new("persona", "obrero", keys, 1f, 81 + w));
        }
        return new(tracks, []);
    }

    /// <summary>
    /// Dónde está alguien de la escena al segundo <paramref name="t"/>. No se ve antes de su primer momento, mientras su
    /// primer momento no tenga acción (todavía no llegó), después de «subir» ni a los 3 segundos de su último momento.
    /// </summary>
    public static ScenePose Pose(SceneTrack track, double t)
    {
        var k = track.Keys;
        if (k.Count == 0 || t < k[0].T - 0.001 || t > k[^1].T + 3)
            return new(0, 0, 0, 0, "", null, false);
        var i = 0;
        while (i + 1 < k.Count && k[i + 1].T <= t)
            i++;
        var a = k[i];
        var b = i + 1 < k.Count ? k[i + 1] : a;
        // Un primer momento sin acción quiere decir «todavía no llegó»; «subir» al final, que se subió a un vehículo.
        if (i == 0 && a.Act == "" && k.Count > 1 && t < k[1].T || i == k.Count - 1 && a.Act == "subir")
            return new(a.X, a.Z, 0, 0, "", null, false);
        var span = b.T - a.T;
        var f = span > 1e-6 ? (float)Math.Clamp((t - a.T) / span, 0, 1) : 0f;
        var x = a.X + (b.X - a.X) * f;
        var z = a.Z + (b.Z - a.Z) * f;
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var dist = MathF.Sqrt(dx * dx + dz * dz);
        var speed = span > 1e-6 && f < 1 ? dist / (float)span : 0f;
        // Mira hacia donde va; si está quieto, hacia el próximo movimiento o hacia el centro de la escena.
        var yaw = dist > 0.05f ? MathF.Atan2(-dx, -dz) : float.NaN;
        // Lo que dice dura 4 segundos desde su momento.
        var say = k.LastOrDefault(key => key.Say is not null && key.T <= t && t - key.T < 4.0)?.Say;
        return new(x, z, yaw, speed, speed > 0.05f ? (a.Act is "" ? "caminar" : a.Act) : (a.Act is "caminar" or "correr" or "andar" ? "quieto" : a.Act), say, true);
    }
}
