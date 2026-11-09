using System.Reflection;
using Distrito.Game.Network;
using Distrito.Game.Player;
using Distrito.Server.Core.World;
using Distrito.Shared.Api;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Escenas de la ciudad en la red (ver <see cref="SceneDirector"/> y <see cref="SceneScripts"/>). El servidor decide
/// qué empieza y avisa a todos con lo mínimo (código, lugar, semilla y hora); cada equipo arma la coreografía igual y la
/// dibuja solo si está cerca (o si es para toda la ciudad, como la lluvia). Las que tienen tiros les avisan a los
/// habitantes en el servidor (huyen, se esconden y llaman a la policía).
/// </summary>
public partial class SceneNetwork : Node
{
    private const float DrawRadius = 170f;

    public static SceneNetwork? Instance { get; private set; }

    private GameNetwork _network = null!;
    private Distrito.Game.Life.LifeNetwork? _life;
    private SceneConfig? _config;
    private SceneDirector? _director;
    private readonly Dictionary<long, (ActiveScene Scene, SceneTimeline Timeline)> _active = new();
    private readonly Dictionary<long, SceneView> _views = new();
    private readonly List<(long AtMs, string Kind, float X, float Z)> _incidents = new();
    /// <summary>Pistas de cada escena que ya no siguen la coreografía (índice → segundo en que quedaron; −1 = las maneja PveNetwork).</summary>
    private readonly Dictionary<long, Dictionary<int, double>> _taken = new();
    private long _offsetMs;
    private double _tick, _sync;
    private GpuParticles3D? _rain;
    private AudioStreamPlayer? _rainSound;
    private float _overcast;

    /// <summary>Escenas en curso (para el panel de administración y las pruebas).</summary>
    public IEnumerable<ActiveScene> Active => _active.Values.Select(a => a.Scene);

    public void Initialize(GameNetwork network, Distrito.Game.Life.LifeNetwork? life)
    {
        Instance = this;
        _network = network;
        _life = life;
        _network.PlayerAdmitted += (peer, _) =>
        {
            if (Multiplayer.IsServer())
                RpcId(peer, MethodName.ReceiveAll, Pack(_active.Values.Select(a => a.Scene)), NowMs());
        };
    }

    public override void _Ready()
    {
        Name = "Scenes";
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("escenas.json");
            if (stream is null)
                return;
            using var reader = new StreamReader(stream);
            var (config, errors) = SceneConfig.Parse(reader.ReadToEnd());
            if (config is null)
                GD.PushWarning($"[DISTRITO] escenas.json con errores: {string.Join(" | ", errors)}");
            _config = config;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[DISTRITO] no se pudieron leer las escenas: {e.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        DayNightCycle.Overcast = 0;
        DayNightCycle.Blackout = null;
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private long ServerNowMs() => NowMs() + _offsetMs;

    // ---------------- Servidor ----------------

    /// <summary>Empieza una escena ya, en el lugar indicado (panel de administración y capturas de prueba).</summary>
    public ActiveScene? Trigger(string code, int? place = null, double secondsAgo = 0)
    {
        if (_config is null || !Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer())
            return null;
        _director ??= new SceneDirector(_config);
        if (_director.Find(code) is not { } def)
            return null;
        var scene = _director.Start(def, NowMs() - (long)(secondsAgo * 1000), place);
        Started(scene);
        return scene;
    }

    private void Started(ActiveScene scene)
    {
        Register(scene);
        if (_active.TryGetValue(scene.Id, out var entry) && entry.Timeline.Incidents is { } incidents)
            foreach (var (t, x, z, kind) in incidents)
                _incidents.Add((scene.StartMs + (long)(t * 1000), kind, x, z));
        GD.Print($"[DISTRITO] ESCENA {scene.Code} en ({scene.X:0}, {scene.Z:0}) por {scene.DurationMs / 60000.0:0.#} min");
        foreach (var peer in Multiplayer.GetPeers())
            RpcId(peer, MethodName.ReceiveStart, Pack([scene]), NowMs());
    }

    private void ServerTick()
    {
        if (_config is null)
            return;
        _director ??= new SceneDirector(_config);
        var now = DateTimeOffset.UtcNow;
        var (started, ended) = _director.Tick(now);
        foreach (var s in started)
            Started(s);
        foreach (var s in ended)
        {
            _active.Remove(s.Id);
            EndFights(s.Id);
            foreach (var peer in Multiplayer.GetPeers())
                RpcId(peer, MethodName.ReceiveEnd, s.Id);
        }
        // Los tiros y los heridos de las escenas: los habitantes reaccionan como con los de verdad.
        var ms = now.ToUnixTimeMilliseconds();
        foreach (var inc in _incidents.Where(i => i.AtMs <= ms).ToList())
        {
            _incidents.Remove(inc);
            _life?.OnIncident(inc.Kind, inc.X, inc.Z, now);
        }
        if ((_sync -= 1) <= 0)
        {
            _sync = 10;
            foreach (var peer in Multiplayer.GetPeers())
                RpcId(peer, MethodName.ReceiveAll, Pack(_active.Values.Select(a => a.Scene)), NowMs());
        }
    }

    private string Pack(IEnumerable<ActiveScene> scenes) =>
        string.Join(';', scenes.Select(s => FormattableString.Invariant($"{s.Id}|{s.Code}|{s.X}|{s.Z}|{s.Seed}|{s.StartMs}|{s.DurationMs}|{PackTaken(s.Id)}")));

    private string PackTaken(long id) => _taken.TryGetValue(id, out var t)
        ? string.Join(',', t.Select(kv => FormattableString.Invariant($"{kv.Key}:{kv.Value:0.##}")))
        : "";

    private IEnumerable<ActiveScene> Unpack(string data)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var item in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = item.Split('|');
            if (p.Length >= 7 && long.TryParse(p[0], out var id) && float.TryParse(p[2], inv, out var x)
                && float.TryParse(p[3], inv, out var z) && int.TryParse(p[4], out var seed)
                && long.TryParse(p[5], out var start) && long.TryParse(p[6], out var duration))
            {
                if (p.Length >= 8 && p[7].Length > 0)
                {
                    var taken = new Dictionary<int, double>();
                    foreach (var pair in p[7].Split(','))
                        if (pair.Split(':') is [var a, var b] && int.TryParse(a, out var track) && double.TryParse(b, inv, out var at))
                            taken[track] = at;
                    _taken[id] = taken;
                }
                yield return new ActiveScene(id, p[1], x, z, seed, start, duration);
            }
        }
    }

    private void Register(ActiveScene scene)
    {
        if (_config is null || _active.ContainsKey(scene.Id) || _config.Escenas.FirstOrDefault(d => d.Codigo == scene.Code) is not { } def)
            return;
        _active[scene.Id] = (scene, SceneScripts.Build(def, scene));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveStart(string data, long serverNowMs)
    {
        _offsetMs = serverNowMs - NowMs();
        foreach (var s in Unpack(data))
            Register(s);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveEnd(long id)
    {
        _active.Remove(id);
        _taken.Remove(id);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveAll(string data, long serverNowMs)
    {
        _offsetMs = serverNowMs - NowMs();
        var list = Unpack(data).ToList();
        foreach (var id in _active.Keys.Except(list.Select(s => s.Id)).ToList())
            _active.Remove(id);
        foreach (var s in list)
            Register(s);
    }

    // ---------------- Dibujo ----------------

    public override void _Process(double delta)
    {
        if (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer() && (_tick += delta) >= 1)
        {
            _tick = 0;
            ServerTick();
        }
        if (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer())
            CheckFights(delta);
        if (DisplayServer.GetName() == "headless")
            return;
        var me = _network?.LocalPlayer;
        var camera = GetViewport().GetCamera3D();
        var here = me?.VisualPosition ?? camera?.GlobalPosition ?? Vector3.Zero;
        var now = ServerNowMs();
        var raining = false;
        (Vector3, float)? blackout = null;
        foreach (var (id, (scene, timeline)) in _active.ToList())
        {
            var t = scene.Seconds(now);
            if (t > scene.DurationMs / 1000.0 + 4)
            {
                _active.Remove(id);
                _taken.Remove(id);
                continue;
            }
            raining |= timeline.Weather == "lluvia" && t >= 0;
            if (timeline.Blackout > 0 && t >= 0)
                blackout = (new Vector3(scene.X, 0, scene.Z), timeline.Blackout);
            var near = new Vector2(here.X - scene.X, here.Z - scene.Z).Length() < DrawRadius;
            if (timeline.Tracks.Count == 0 && timeline.Sounds.Count == 0)
                continue;
            if (!near)
            {
                if (_views.Remove(id, out var far))
                    far.QueueFree();
                continue;
            }
            if (!_views.TryGetValue(id, out var view))
            {
                view = new SceneView(scene, timeline);
                AddChild(view);
                _views[id] = view;
            }
            view.Advance(t, (float)delta, _taken.GetValueOrDefault(id));
        }
        foreach (var id in _views.Keys.Except(_active.Keys).ToList())
            if (_views.Remove(id, out var gone))
                gone.QueueFree();
        UpdateWeather(raining, camera, (float)delta);
        DayNightCycle.Blackout = blackout;
    }

    private void UpdateWeather(bool raining, Camera3D? camera, float delta)
    {
        _overcast = Mathf.MoveToward(_overcast, raining ? 1f : 0f, delta / 20f); // se nubla y despeja en unos 20 s
        DayNightCycle.Overcast = _overcast;
        if (raining && _rain is null)
        {
            _rain = new GpuParticles3D
            {
                Amount = GraphicsQuality.Current.Rain, Lifetime = 1.2f, Preprocess = 1f, VisibilityAabb = new Aabb(new Vector3(-30, -20, -30), new Vector3(60, 40, 60)),
                ProcessMaterial = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(25, 0.5f, 25),
                    Direction = new Vector3(0.1f, -1, 0), Spread = 3, InitialVelocityMin = 16, InitialVelocityMax = 20, Gravity = new Vector3(0, -9.8f, 0),
                },
                DrawPass1 = new QuadMesh { Size = new Vector2(0.02f, 0.5f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.8f, 0.9f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY } },
            };
            AddChild(_rain);
            _rainSound = new AudioStreamPlayer { Stream = Distrito.Game.Audio.Sfx.Rain, Bus = Distrito.Game.Audio.SoundPlayer.Bus, VolumeDb = -14 };
            AddChild(_rainSound);
            _rainSound.Play();
        }
        if (_rain is not null)
        {
            if (camera is not null)
                _rain.GlobalPosition = camera.GlobalPosition + new Vector3(0, 12, 0);
            _rain.Emitting = raining;
            if (_rainSound is not null)
                _rainSound.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _overcast)) - 14;
            if (!raining && _overcast <= 0.001f)
            {
                _rain.QueueFree();
                _rainSound?.QueueFree();
                _rain = null;
                _rainSound = null;
            }
        }
    }

    // ---------------- Una escena dibujada ----------------

    private sealed partial class SceneView : Node3D
    {
        private readonly ActiveScene _scene;
        private readonly SceneTimeline _timeline;
        private readonly List<(SceneTrack Track, Node3D Node, CharacterModel? Model, Label3D? Bubble, Node3D? Crate, float Yaw)> _parts = new();
        private readonly HashSet<int> _played = new();
        private double _last = -1;

        public SceneView(ActiveScene scene, SceneTimeline timeline)
        {
            _scene = scene;
            _timeline = timeline;
            Name = $"Escena_{scene.Code}_{scene.Id}";
            foreach (var track in timeline.Tracks)
                _parts.Add(Make(track));
        }

        private (SceneTrack, Node3D, CharacterModel?, Label3D?, Node3D?, float) Make(SceneTrack track)
        {
            var node = new Node3D { Visible = false };
            AddChild(node);
            CharacterModel? model = null;
            Label3D? bubble = null;
            Node3D? crate = null;
            switch (track.Kind)
            {
                case "persona":
                    model = CharacterModel.Build(Look(track.Look, track.Variant));
                    model.Scale = Vector3.One * track.Scale;
                    node.AddChild(model);
                    bubble = new Label3D
                    {
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 34, OutlineSize = 10, PixelSize = 0.006f,
                        Modulate = new Color(1f, 0.97f, 0.88f), OutlineModulate = new Color(0.05f, 0.04f, 0.03f), Position = new Vector3(0, 2.25f * track.Scale + 0.1f, 0),
                        Width = 520, AutowrapMode = TextServer.AutowrapMode.WordSmart, VisibilityRangeEnd = 32, Visible = false,
                    };
                    node.AddChild(bubble);
                    crate = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.4f, 0.4f) }, MaterialOverride = InteriorBuilder.Mat(new Color(0.5f, 0.37f, 0.22f)), Position = new Vector3(0, 1.05f, -0.35f), Visible = false };
                    node.AddChild(crate);
                    break;
                case "auto" or "camioneta" or "camion" or "policia" or "funebre":
                    var type = track.Kind == "camion" ? Distrito.Shared.Economy.Vehicles.Truck : Distrito.Shared.Economy.Vehicles.Pickup;
                    Color? paint = track.Kind switch
                    {
                        "policia" => new Color(0.1f, 0.1f, 0.12f),
                        "funebre" => new Color(0.05f, 0.05f, 0.06f),
                        "auto" => new[] { new Color(0.55f, 0.12f, 0.1f), new Color(0.15f, 0.2f, 0.35f), new Color(0.75f, 0.7f, 0.55f), new Color(0.2f, 0.3f, 0.22f), new Color(0.12f, 0.12f, 0.12f) }[Math.Abs(track.Variant) % 5],
                        _ => null,
                    };
                    var style = track.Kind switch { "auto" => "sedan", "policia" => "policia", "funebre" => "funebre", _ => null };
                    node.AddChild(Distrito.Game.Transport.VehicleBody.Visual(type, paint, style));
                    break;
                case "barco":
                    node.AddChild(Boat(track.Look == "lancha"));
                    break;
                case "tren":
                    node.AddChild(Train(track.Variant));
                    break;
                case "pelota":
                    node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f }, MaterialOverride = InteriorBuilder.Mat(new Color(0.9f, 0.88f, 0.8f)), Position = new Vector3(0, 0.12f, 0), Name = "Bola" });
                    break;
                case "puesto":
                    node.AddChild(Stall(track.Variant));
                    break;
                case "cajon":
                    node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.35f, 0.35f) }, MaterialOverride = InteriorBuilder.Mat(new Color(0.35f, 0.22f, 0.12f)), Position = new Vector3(0, 0.18f, 0) });
                    break;
            }
            // Mirada inicial: hacia el centro de la escena (o hacia la calzada).
            var first = track.Keys.Count > 0 ? track.Keys[0] : new SceneKey(0, _scene.X, _scene.Z);
            var to = new Vector2(_scene.X - first.X, _scene.Z - first.Z);
            if (to.LengthSquared() < 0.25f)
                to = new Vector2(0, -first.Z);
            var yaw = to.LengthSquared() > 0.01f ? Mathf.Atan2(-to.X, -to.Y) : 0f;
            return (track, node, model, bubble, crate, yaw);
        }

        public void Advance(double t, float delta, IReadOnlyDictionary<int, double>? taken)
        {
            for (var i = 0; i < _parts.Count; i++)
            {
                var (track, node, model, bubble, crate, yaw) = _parts[i];
                var pose = SceneScripts.Pose(track, t);
                if (taken is not null && taken.TryGetValue(i, out var frozen))
                    // Alguien se metió: el que pasó a ser de verdad (PveNetwork) no se dibuja acá; el resto queda quieto.
                    pose = frozen < 0 ? pose with { Visible = false } : SceneScripts.Pose(track, frozen) with { Speed = 0, Say = null, Act = track.Kind == "persona" ? "quieto" : "" };
                node.Visible = pose.Visible;
                if (!pose.Visible)
                    continue;
                if (!float.IsNaN(pose.Yaw) && track.Kind != "pelota")
                    yaw = Mathf.LerpAngle(yaw, pose.Yaw, 1 - Mathf.Exp(-8 * delta));
                var y = track.Kind == "barco" ? -0.35f : 0f;
                if (track.Kind == "pelota")
                    y = Mathf.Abs(Mathf.Sin((float)t * 5)) * 0.8f;
                node.Position = new Vector3(pose.X, y, pose.Z);
                node.Rotation = new Vector3(pose.Act == "tambalear" ? Mathf.Sin((float)t * 2.3f) * 0.12f : 0, yaw, pose.Act == "tambalear" ? Mathf.Sin((float)t * 1.7f) * 0.1f : 0);
                _parts[i] = (track, node, model, bubble, crate, yaw);
                if (model is not null)
                {
                    model.Animate(pose.Speed, delta);
                    model.SetDowned(pose.Act == "caido");
                    model.SetWeapon(pose.Act is "apuntar" or "disparar" ? "pistola" : Distrito.Shared.Economy.Weapons.Fists);
                    if (pose.Act == "disparar" && (int)(t * 1.3) != (int)((t - delta) * 1.3))
                        model.Recoil();
                    if (pose.Act == "gesto" && (int)(t / 1.5) != (int)((t - delta) / 1.5))
                        model.Punch();
                }
                if (crate is not null)
                    crate.Visible = pose.Act == "cargar";
                if (bubble is not null)
                {
                    bubble.Visible = pose.Say is not null;
                    if (pose.Say is not null && bubble.Text != pose.Say)
                        bubble.Text = pose.Say;
                }
                if (track.Kind == "policia")
                    foreach (var light in node.FindChildren("Baliza*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                        light.Visible = ((int)(t * 4) % 2 == 0) == light.Name.ToString().EndsWith("Roja");
            }
            // Sonidos: los que tocan entre el cuadro anterior y este.
            for (var s = 0; s < _timeline.Sounds.Count; s++)
            {
                var sound = _timeline.Sounds[s];
                if (_played.Contains(s) || sound.T > t || sound.T < _last - 1 && _last >= 0)
                    continue;
                _played.Add(s);
                if (Distrito.Game.Audio.Sfx.ByName(sound.Sound) is { } stream)
                    Distrito.Game.Audio.SoundPlayer.At(stream, new Vector3(sound.X, 1.5f, sound.Z), sound.Sound is "trueno" ? 4 : 0, Distrito.Game.Audio.SoundPlayer.Vary(0.04f),
                        sound.Sound is "trueno" or "sirena-barco" or "campana" or "silbato" ? 400 : sound.Sound is "sirena" or "tiro" ? 120 : 60);
            }
            _last = t;
        }

        private static Node3D Boat(bool small)
        {
            var root = new Node3D();
            if (ModelLibrary.Make("autos", small ? "lancha" : "barco", small ? new Vector3(2.4f, 1.5f, 6) : new Vector3(8, 9, 34), ModelLibrary.Fit.Length) is { } glb)
            {
                root.AddChild(glb);
                return root;
            }
            var hull = InteriorBuilder.Mat(small ? new Color(0.4f, 0.28f, 0.16f) : new Color(0.15f, 0.17f, 0.2f));
            var deck = InteriorBuilder.Mat(new Color(0.55f, 0.42f, 0.28f));
            var white = InteriorBuilder.Mat(new Color(0.88f, 0.86f, 0.8f));
            var red = InteriorBuilder.Mat(new Color(0.6f, 0.12f, 0.1f));
            if (small)
            {
                InteriorBuilder.Box(root, new Vector3(2.4f, 0.9f, 6), new Vector3(0, 0.3f, 0), hull);
                InteriorBuilder.Box(root, new Vector3(1.2f, 0.7f, 1.4f), new Vector3(0, 1.0f, 1), white);
                return root;
            }
            InteriorBuilder.Box(root, new Vector3(8, 3.2f, 34), new Vector3(0, 0.9f, 0), hull);
            InteriorBuilder.Box(root, new Vector3(7.6f, 0.2f, 33), new Vector3(0, 2.6f, 0), deck);
            InteriorBuilder.Box(root, new Vector3(6, 4, 7), new Vector3(0, 4.6f, 9), white);
            InteriorBuilder.Box(root, new Vector3(1.6f, 4, 1.6f), new Vector3(0, 8.2f, 10), red);
            for (var i = 0; i < 4; i++)
                InteriorBuilder.Box(root, new Vector3(2.4f, 1.6f, 2.4f), new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 3.5f, -8 + i * 3.2f), InteriorBuilder.Mat(new[] { new Color(0.5f, 0.2f, 0.15f), new Color(0.2f, 0.35f, 0.45f), new Color(0.45f, 0.45f, 0.3f) }[i % 3]));
            MeshMerge.All(root);
            return root;
        }

        /// <summary>
        /// Tren de pasajeros a vapor (o game/modelos/autos/tren.glb): locomotora adelante (hacia −Z), ténder y cinco
        /// coches con sus puertas donde están las del andén (<see cref="Station.CoachDoors"/>) cuando frena.
        /// </summary>
        private static Node3D Train(int variant)
        {
            var root = new Node3D();
            if (ModelLibrary.Make("autos", "tren", new Vector3(3.2f, 4.6f, 112), ModelLibrary.Fit.Length) is { } glb)
            {
                glb.Position = new Vector3(0, 0, 0);
                root.AddChild(glb);
                return root;
            }
            var black = InteriorBuilder.Mat(new Color(0.07f, 0.07f, 0.08f), 0.5f, 0.4f);
            var red = InteriorBuilder.Mat(new Color(0.55f, 0.1f, 0.08f));
            var brass = InteriorBuilder.Mat(new Color(0.75f, 0.6f, 0.3f), 0.4f, 0.6f);
            Color[] liveries = [new(0.38f, 0.1f, 0.1f), new(0.14f, 0.28f, 0.2f), new(0.22f, 0.26f, 0.34f)];
            var body = InteriorBuilder.Mat(liveries[Math.Abs(variant) % liveries.Length]);
            var roof = InteriorBuilder.Mat(new Color(0.2f, 0.2f, 0.21f));
            var glass = InteriorBuilder.Mat(new Color(0.85f, 0.75f, 0.5f), 0.3f, 0f, 0.25f); // ventanillas con luz cálida
            var stripe = InteriorBuilder.Mat(new Color(0.8f, 0.68f, 0.38f));
            var center = Station.AxisX;
            float Local(float worldX) => -(worldX - center);
            // Locomotora.
            var loco = Local(center + 61);
            var boiler = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.25f, BottomRadius = 1.25f, Height = 9 }, MaterialOverride = black, Position = new Vector3(0, 2.4f, loco - 1.5f), Rotation = new Vector3(Mathf.Pi / 2, 0, 0) };
            root.AddChild(boiler);
            InteriorBuilder.Box(root, new Vector3(3, 3.4f, 4), new Vector3(0, 2.6f, loco + 4.5f), black);          // cabina
            InteriorBuilder.Box(root, new Vector3(3.2f, 0.25f, 4.4f), new Vector3(0, 4.4f, loco + 4.5f), roof);
            InteriorBuilder.Box(root, new Vector3(1.6f, 0.7f, 0.1f), new Vector3(0, 3.3f, loco + 2.45f), glass);   // ventanas de la cabina
            InteriorBuilder.Box(root, new Vector3(3.1f, 0.5f, 14), new Vector3(0, 0.95f, loco), red);              // bastidor
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.35f, Height = 1.6f }, MaterialOverride = black, Position = new Vector3(0, 4.2f, loco - 5.2f) }); // chimenea
            root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.55f, Height = 1.1f }, MaterialOverride = brass, Position = new Vector3(0, 3.65f, loco - 1) }); // domo
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = 0.15f }, MaterialOverride = brass, Position = new Vector3(0, 2.4f, loco - 6.05f), Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
            root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f }, MaterialOverride = InteriorBuilder.Mat(new Color(1f, 0.95f, 0.7f), 0.2f, 0, 2f), Position = new Vector3(0, 3.5f, loco - 6.2f) }); // farol
            InteriorBuilder.Box(root, new Vector3(2.6f, 0.9f, 1.2f), new Vector3(0, 0.6f, loco - 7.3f), red, new Vector3(Mathf.DegToRad(30), 0, 0)); // trompa
            for (var w = 0; w < 4; w++)
                foreach (var side in new[] { -1.45f, 1.45f })
                    root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.85f, Height = 0.18f }, MaterialOverride = red, Position = new Vector3(side, 0.85f, loco - 3 + w * 2.1f), Rotation = new Vector3(0, 0, Mathf.Pi / 2) });
            // Ténder.
            var tender = Local(center + 50.5f);
            InteriorBuilder.Box(root, new Vector3(3, 2.6f, 6.5f), new Vector3(0, 1.9f, tender), black);
            InteriorBuilder.Box(root, new Vector3(2.6f, 0.4f, 5.6f), new Vector3(0, 3.3f, tender), InteriorBuilder.Mat(new Color(0.05f, 0.05f, 0.05f), 1f));
            // Coches de pasajeros.
            foreach (var door in Station.CoachDoors)
            {
                var c = Local(door);
                InteriorBuilder.Box(root, new Vector3(3, 2.9f, 16.6f), new Vector3(0, 2.25f, c), body);
                InteriorBuilder.Box(root, new Vector3(3.15f, 0.35f, 16.8f), new Vector3(0, 3.85f, c), roof);
                InteriorBuilder.Box(root, new Vector3(2.4f, 0.3f, 15.6f), new Vector3(0, 4.15f, c), roof); // lucernario
                InteriorBuilder.Box(root, new Vector3(3.04f, 0.12f, 16.6f), new Vector3(0, 1.5f, c), stripe);
                InteriorBuilder.Box(root, new Vector3(3.04f, 0.8f, 13.4f), new Vector3(0, 2.75f, c), glass);
                InteriorBuilder.Box(root, new Vector3(3.06f, 2.2f, 1.1f), new Vector3(0, 1.85f, c), InteriorBuilder.Mat(new Color(0.18f, 0.12f, 0.08f))); // puerta del medio
                foreach (var end in new[] { -5.8f, 5.8f })
                    InteriorBuilder.Box(root, new Vector3(2.2f, 0.9f, 2.6f), new Vector3(0, 0.55f, c + end), black); // bogies
            }
            MeshMerge.All(root);
            return root;
        }

        private static Node3D Stall(int variant)
        {
            var root = new Node3D();
            var wood = InteriorBuilder.Mat(new Color(0.42f, 0.3f, 0.18f));
            InteriorBuilder.Box(root, new Vector3(2.6f, 0.9f, 1.1f), new Vector3(0, 0.45f, 0), wood);
            foreach (var x in new[] { -1.25f, 1.25f })
                foreach (var z in new[] { -0.5f, 0.5f })
                    InteriorBuilder.Box(root, new Vector3(0.06f, 2.4f, 0.06f), new Vector3(x, 1.2f, z), wood);
            var colors = new[] { new Color(0.6f, 0.15f, 0.12f), new Color(0.15f, 0.35f, 0.25f), new Color(0.2f, 0.25f, 0.5f), new Color(0.6f, 0.45f, 0.12f) };
            for (var i = 0; i < 6; i++)
                InteriorBuilder.Box(root, new Vector3(2.8f / 6, 0.05f, 1.5f), new Vector3(-1.4f + (i + 0.5f) * 2.8f / 6, 2.42f, 0), InteriorBuilder.Mat(i % 2 == 0 ? new Color(0.92f, 0.88f, 0.78f) : colors[variant % colors.Length]));
            for (var i = 0; i < 5; i++)
                InteriorBuilder.Box(root, new Vector3(0.3f, 0.2f, 0.3f), new Vector3(-1 + i * 0.5f, 1.0f, 0), InteriorBuilder.Mat(colors[(i + variant) % colors.Length].Lightened(0.2f)));
            MeshMerge.All(root);
            return root;
        }
    }

    /// <summary>Ropa de época para cada papel de las escenas (o el .glb de game/modelos/personas/&lt;papel&gt;.glb).</summary>
    internal static CharacterLook Look(string role, int variant)
    {
        string[] skins = ["#e8c3a0", "#d9a57f", "#c08a63", "#8a5a3c", "#5e3a26"];
        string[] hairs = ["#1c1612", "#3b2a1d", "#6b4a2b", "#9a8a78", "#c9a36b"];
        string[] coats = ["#3a3530", "#2b3340", "#4a3b2a", "#5a2f2a", "#2f3a2f", "#55504a"];
        var v = Math.Abs(variant * 7919 + role.Length * 31);
        string Pick(string[] list, int salt) => list[(v + salt * 3) % list.Length];
        return role switch
        {
            "canillita" or "chico" or "lustrabotas" => new(role, "Pibe", Pick(skins, 0), Pick(hairs, 1), "#d8d2c4", "#4a3b2a", "#6b5a44", "#4a3b2a", "#2a1e16", "#3b3228", Suspenders: true),
            "nena" => new(role, "Nena", Pick(skins, 0), Pick(hairs, 1), "#e8d8c8", Pick(["#8a2a3a", "#2a4a6a", "#6a5a2a"], 2), Pick(["#8a2a3a", "#2a4a6a", "#6a5a2a"], 2), "#e8e0d0", "#2a1e16", null, LongCoat: true),
            "obrero" => new(role, "Changarín", Pick(skins, 0), Pick(hairs, 1), "#7a8a9a", "#3a3f48", "#5a4a38", "#2a2a2a", "#2a1e16", "#4a4038", Suspenders: true),
            "ferroviario" => new(role, "Jefe de estación", Pick(skins, 0), Pick(hairs, 1), "#e8e4dc", "#1c2433", "#1c2433", "#b08a3a", "#141210", "#1c2433", Jacket: true, Mustache: true),
            "policia" => new(role, "Policía", Pick(skins, 0), Pick(hairs, 1), "#c9d0d8", "#1f2a44", "#26345a", "#1a1a1a", "#141210", "#1f2a44", Jacket: true),
            "maton" => new(role, "Matón", Pick(skins, 0), Pick(hairs, 1), "#d8d4cc", "#1e1e22", Pick(["#1e1e22", "#2b2b33", "#3a2f28"], 3), "#7a1a1a", "#111111", "#1e1e22", Jacket: true, Mustache: v % 2 == 0),
            "luto" => new(role, "Deudo", Pick(skins, 0), Pick(hairs, 1), "#e0dcd4", "#151515", "#151515", "#151515", "#0e0e0e", "#151515", Jacket: true),
            "luto-mujer" => new("vecina", "Viuda", Pick(skins, 0), Pick(hairs, 1), "#151515", "#151515", "#151515", "#151515", "#0e0e0e", null, LongCoat: true),
            "vecina" => new(role, "Vecina", Pick(skins, 0), Pick(hairs, 1), "#e8dcc8", Pick(["#7a3b3b", "#3b4a7a", "#4a6a4a", "#7a6a3b"], 2), Pick(["#7a3b3b", "#3b4a7a", "#4a6a4a", "#7a6a3b"], 2), Pick(coats, 3), "#2a1e16", null, LongCoat: true),
            "musico" => new(role, "Músico", Pick(skins, 0), Pick(hairs, 1), "#e0d8c8", "#2a2a2a", "#5a2f2a", "#c9a36b", "#1a1410", "#2a2420", Vest: true, Mustache: true),
            "borracho" => new(role, "Borracho", Pick(skins, 0), Pick(hairs, 1), "#c8c0a8", "#3a3530", "#4a4038", "#5a2f2a", "#2a1e16", null, Vest: true, Mustache: true),
            "ejecutivo" => new(role, "Señor", Pick(skins, 0), Pick(hairs, 1), "#e8e4dc", "#2b2b33", Pick(coats, 2), "#6b1f1f", "#141210", "#2b2b33", Jacket: true),
            "comerciante" => new(role, "Comerciante", Pick(skins, 0), Pick(hairs, 1), "#e6dfcf", "#2b2622", "#e8e4dc", "#3a3530", "#1a1410", null, Vest: true, Mustache: true),
            _ => new("vecino", "Vecino", Pick(skins, 0), Pick(hairs, 1), "#d8d0c0", Pick(["#3a3530", "#2b3340", "#4a3b2a"], 2), Pick(coats, 3), "#6b1f1f", "#2a1e16", v % 3 == 0 ? null : Pick(coats, 4), Jacket: v % 2 == 0, Mustache: v % 4 == 0),
        };
    }
}
