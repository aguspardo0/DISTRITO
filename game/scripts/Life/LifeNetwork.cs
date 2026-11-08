using Distrito.Game.Network;
using Distrito.Game.Player;
using Distrito.Server.Core.World;
using Distrito.Shared.Life;
using Godot;

namespace Distrito.Game.Life;

/// <summary>
/// Vida autónoma de los NPC. En el servidor del juego: al arrancar pide los habitantes al backend (que los crea la
/// primera vez), los hace vivir (rutinas, necesidades y decisiones) y cada pocos minutos guarda su estado; el servidor
/// decide y el backend solo guarda. Etapa 5: a cada jugador le manda los habitantes que tiene cerca, un tramo por
/// decisión (de dónde sale, por qué esquinas y a dónde llega), y en el cliente caminan solos por las veredas, entran a
/// los locales y patrullan. Los lejanos no se dibujan ni viajan por la red.
/// Sin backend (modo invitado) no hay habitantes.
/// </summary>
public partial class LifeNetwork : Node
{
    private const double RetrySeconds = 15;
    private const double SaveSeconds = 300;
    private const double StepSeconds = 1;

    private GameNetwork _network = null!;
    private readonly Dictionary<Guid, Resident> _residents = new();
    private readonly Dictionary<Guid, ResidentState> _states = new();
    private readonly List<ResidentEvent> _pendingEvents = new();
    private LifeConfig? _config;
    private LifeSimulation? _sim;
    private double _untilStep;
    private double _untilRetry;
    private double _untilSave = SaveSeconds;
    private bool _loading, _saving;

    public IReadOnlyDictionary<Guid, Resident> Residents => _residents;
    public IReadOnlyDictionary<Guid, ResidentState> States => _states;
    public LifeConfig? Config => _config;
    public bool Loaded => _config is not null;

    public void Initialize(GameNetwork network, Distrito.Game.Combat.CombatNetwork? combat = null)
    {
        _network = network;
        if (combat is not null)
            combat.Incident += (kind, at) =>
            {
                if (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer())
                    OnIncident(kind, at.X, at.Z, DateTimeOffset.UtcNow);
            };
        Multiplayer.PeerDisconnected += peer => _sent.Remove(peer);
    }

    public override void _Process(double delta)
    {
        if (!Multiplayer.HasMultiplayerPeer())
            return;
        if (!Multiplayer.IsServer())
        {
            AnimateFigures((float)delta);
            return;
        }
        if (_network.Backend is null)
            return;
        if (!Loaded)
        {
            _untilRetry -= delta;
            if (_untilRetry <= 0 && !_loading)
            {
                _untilRetry = RetrySeconds;
                _ = LoadAsync();
            }
            return;
        }
        _untilStep -= delta;
        if (_untilStep <= 0)
        {
            _untilStep = StepSeconds;
            Step(DateTimeOffset.UtcNow);
        }
        _untilSave -= delta;
        if (_untilSave <= 0 && !_saving)
        {
            _untilSave = SaveSeconds;
            GD.Print($"[DISTRITO] VIDA {Summary(DateTimeOffset.UtcNow)}");
            _ = SaveAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var result = await _network.Backend!.GetResidentsAsync(_network.ServerKey);
            if (!result.IsSuccess)
                return;
            var config = LifeConfig.Parse(result.Value!.ConfigJson);
            _residents.Clear();
            _states.Clear();
            var now = DateTimeOffset.UtcNow;
            foreach (var r in result.Value.Residents)
            {
                _residents[r.Identity.Id] = r.Identity;
                _states[r.Identity.Id] = r.State ?? ResidentGenerator.InitialState(r.Identity, now);
            }
            foreach (var (id, seeded) in LifeSociety.Seed(_residents.Values.ToList(), _states))
                _states[id] = seeded; // la primera vez: conocen a algunos vecinos y a los compañeros
            _lastMeet = now;
            _config = config;
            _sim = new LifeSimulation(config, CityNavigation.Route);
            Step(now); // se pone al día con lo que pasó mientras el servidor estaba apagado
            GD.Print($"[DISTRITO] VIDA habitantes={_residents.Count} hora_bellmont={BellmontClock.TimeOfDay(now):hh\\:mm} " +
                     $"noche={(BellmontClock.IsNight(now) ? "si" : "no")} " +
                     string.Join(" ", _residents.Values.GroupBy(r => r.Profession).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
            GD.Print($"[DISTRITO] VIDA {Summary(now)}");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            GD.PushWarning($"[DISTRITO] No se pudieron leer los habitantes: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Avanza a todos los habitantes (los que no terminaron lo que hacen casi no cuestan nada).</summary>
    private void Step(DateTimeOffset now)
    {
        if (_sim is null)
            return;
        var world = new LifeWorld(_residents, _states);
        foreach (var r in _residents.Values)
        {
            var before = _states[r.Id];
            var after = _sim.Advance(r, before, now, world);
            if (before.Money > 0 && after.Money == 0)
                Record(r.Id, "sin-plata", $"{r.FullName} se quedó sin plata.");
            _states[r.Id] = after;
        }
        // Etapa 6: cada 15 minutos de Bellmont, los que coinciden en un lugar se conocen, se hacen amigos o se pelean.
        var hours = (now - _lastMeet).TotalHours * BellmontClock.Speed;
        if (hours >= LifeSimulation.CheckMinutes / 60)
        {
            _lastMeet = now;
            var (changed, events) = LifeSociety.Meet(_residents, _states, now, hours);
            foreach (var (id, state) in changed)
                _states[id] = state;
            _pendingEvents.AddRange(events);
            foreach (var e in events.Where((_, i) => i % 2 == 0))
                GD.Print($"[DISTRITO] VIDA relacion {e.Kind}: {e.Text}");
        }
        SyncNearby(now);
    }

    private DateTimeOffset _lastMeet;

    // --- Etapa 7: reacciones ---

    private readonly List<LifeIncident> _recentIncidents = new();

    /// <summary>
    /// Pasó algo en la calle: cada habitante que lo ve o lo oye reacciona según su personalidad y la distancia. Si
    /// alguien avisa, van hasta dos policías de turno (sin arrestar). Los tiros seguidos en el mismo lugar cuentan una vez.
    /// </summary>
    public void OnIncident(string kind, double x, double z, DateTimeOffset now)
    {
        if (_sim is null)
            return;
        _recentIncidents.RemoveAll(i => (now - i.At).TotalSeconds > 20);
        if (_recentIncidents.Any(i => i.Kind == kind && (i.X - x) * (i.X - x) + (i.Z - z) * (i.Z - z) < 15 * 15))
            return;
        var incident = new LifeIncident(kind, Math.Round(x, 2), Math.Round(z, 2), now);
        _recentIncidents.Add(incident);
        var counts = new Dictionary<string, int>();
        var witnesses = new HashSet<Guid>();
        foreach (var r in _residents.Values.OrderBy(r => r.Id))
        {
            var (after, outcome) = _sim.React(r, _states[r.Id], incident);
            if (outcome is null)
                continue;
            witnesses.Add(r.Id);
            _states[r.Id] = after;
            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
            if (outcome is "huyeron" or "escondidos" or "avisaron")
                Record(r.Id, "reaccion", $"{r.FullName}: {after.Reason}.");
        }
        if (counts.GetValueOrDefault("avisaron") > 0)
        {
            var hour = BellmontClock.Hour(now);
            var police = _residents.Values
                .Where(r => r.Profession == "policia" && r.OnShift(hour) && _states[r.Id].Activity != LifeActivities.Responding)
                .OrderBy(r => Distance(_states[r.Id].PositionAt(now), x, z)).Take(2).ToList();
            foreach (var cop in police)
            {
                _states[cop.Id] = _sim.Respond(cop, _states[cop.Id], incident, $"le avisaron de {IncidentKinds.Describe(kind)} y va para allá");
                Record(cop.Id, "reaccion", $"{cop.FullName} acudió a un aviso de {IncidentKinds.Describe(kind)}.");
                counts["policias"] = counts.GetValueOrDefault("policias") + 1;
            }
        }
        if (_config is not null)
        {
            _rumors.Add(new LifeRumor(kind, incident.X, incident.Z, now, ResidentTalk.NearestPlaceName(_config, x, z), witnesses));
            _rumors.RemoveAll(r => (now - r.At).TotalHours > ResidentTalk.RumorHours);
            if (_rumors.Count > 40)
                _rumors.RemoveRange(0, _rumors.Count - 40);
        }
        GD.Print($"[DISTRITO] VIDA reaccion {kind} en ({x:0},{z:0}): " +
                 (counts.Count == 0 ? "nadie cerca" : string.Join(" ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"))));
        SyncNearby(now); // los que salieron corriendo se ven enseguida
    }

    private readonly List<LifeRumor> _rumors = new();

    // --- Etapa 8: hablar con los habitantes ---

    /// <summary>A cuántos metros se puede hablar con alguien (el servidor lo vuelve a revisar con margen).</summary>
    public const float TalkRange = 2.4f;

    /// <summary>Cliente: lo que contestó un habitante (para el panel de diálogo).</summary>
    public event Action<Distrito.Server.Core.Npcs.NpcDialog>? Spoke;

    /// <summary>Código de diálogo de un habitante (para distinguirlo de los mostradores).</summary>
    public const string DialogPrefix = "habitante:";

    /// <summary>Cliente: preguntarle algo (pregunta vacía = saludar).</summary>
    public void Ask(Guid resident, string question) => RpcId(1, MethodName.ServerAsk, resident.ToString("N"), question);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerAsk(string residentId, string question)
    {
        if (!Multiplayer.IsServer() || _sim is null || _config is null)
            return;
        var peer = Multiplayer.GetRemoteSenderId();
        if (!Guid.TryParse(residentId, out var id) || !_residents.TryGetValue(id, out var r) || _network.GetPlayer(peer) is not { } player)
            return;
        var now = DateTimeOffset.UtcNow;
        var s = _states[id];
        var (x, z) = s.PositionAt(now);
        if (!s.Visible(now) || Distance((x, z), player.GlobalPosition.X, player.GlobalPosition.Z) > TalkRange + 3)
            return;
        var world = new LifeWorld(_residents, _states);
        var text = question.Length == 0
            ? ResidentTalk.Greeting(r, s, _config, now)
            : ResidentTalk.Answer(question, r, s, world, _rumors, _config, now);
        if (question.Length > 0)
            Record(id, "charla", $"Un forastero le preguntó a {r.FullName}: {question}.");
        var options = ResidentTalk.Questions.Where(q => q.Id != question).ToList();
        RpcId(peer, MethodName.ReceiveAnswer, residentId, r.FullName, text,
            options.Select(o => o.Id).ToArray(), options.Select(o => o.Label).ToArray());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAnswer(string residentId, string name, string text, string[] ids, string[] labels) =>
        Spoke?.Invoke(new Distrito.Server.Core.Npcs.NpcDialog(DialogPrefix + residentId, name, text,
            ids.Zip(labels, (i, l) => new Distrito.Server.Core.Npcs.NpcOption(i, l)).ToList()));

    /// <summary>Cliente: el habitante visible más cercano a un punto, si está al alcance para hablar.</summary>
    public (Guid Id, string Name)? NearestFigure(Vector3 at)
    {
        (Guid, string)? best = null;
        var bestDistance = TalkRange;
        foreach (var (id, f) in _figures)
        {
            if (!f.Root.Visible)
                continue;
            var d = new Vector2(f.Last.X - at.X, f.Last.Z - at.Z).Length();
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = (id, f.View.Name);
            }
        }
        return best;
    }

    private static double Distance((double X, double Z) p, double x, double z) => Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));

    private (int Friends, int Rivals) CountTies()
    {
        int friends = 0, rivals = 0;
        foreach (var (id, s) in _states)
            foreach (var (other, tie) in s.Ties ?? new())
                if (id.CompareTo(other) < 0)
                {
                    if (tie >= LifeSociety.Friend) friends++;
                    else if (tie <= LifeSociety.Rival) rivals++;
                }
        return (friends, rivals);
    }

    // --- Etapa 5: los que están cerca de cada jugador ---

    /// <summary>Por jugador: qué habitantes ya tiene y con qué decisión (para mandar solo lo nuevo).</summary>
    private readonly Dictionary<long, Dictionary<Guid, long>> _sent = new();
    private int _near, _middle;

    private void SyncNearby(DateTimeOffset now)
    {
        var peers = Multiplayer.GetPeers().Select(p => (long)p).ToHashSet();
        var players = _network.AllPlayers.Where(p => p.IsInsideTree() && peers.Contains(p.PeerId)).ToList();
        var positions = _states.ToDictionary(kv => kv.Key, kv => kv.Value.PositionAt(now));
        _near = _middle = 0;
        foreach (var (_, (x, z)) in positions)
        {
            var closest = players.Count == 0 ? double.MaxValue : players.Min(p =>
                (p.GlobalPosition.X - x) * (p.GlobalPosition.X - x) + (p.GlobalPosition.Z - z) * (p.GlobalPosition.Z - z));
            if (closest <= ResidentLevels.Near * ResidentLevels.Near) _near++;
            else if (closest <= ResidentLevels.Middle * ResidentLevels.Middle) _middle++;
        }

        var nowMs = now.ToUnixTimeMilliseconds();
        foreach (var player in players)
        {
            var sent = _sent.TryGetValue(player.PeerId, out var known) ? known : (_sent[player.PeerId] = new());
            var views = new List<ResidentView>();
            var removed = new List<Guid>();
            foreach (var (id, (x, z)) in positions)
            {
                var seen = sent.TryGetValue(id, out var decision);
                var state = _states[id];
                if (ResidentLevels.InRange(player.GlobalPosition.X - x, player.GlobalPosition.Z - z, seen))
                {
                    if (!seen || decision != state.Decisions)
                    {
                        views.Add(ResidentView.Of(_residents[id], state));
                        sent[id] = state.Decisions;
                    }
                }
                else if (seen)
                {
                    removed.Add(id);
                    sent.Remove(id);
                }
            }
            if (views.Count > 0 || removed.Count > 0)
                RpcId(player.PeerId, MethodName.ReceiveResidents, ResidentSyncCodec.Encode(nowMs, views, removed));
        }
        foreach (var gone in _sent.Keys.Where(k => !peers.Contains(k)).ToList())
            _sent.Remove(gone);
    }

    // --- Cliente: las figuras ---

    private sealed class Figure
    {
        public required ResidentView View;
        public required Node3D Root;
        public required CharacterModel Model;
        public Vector3 Last;
    }

    private readonly Dictionary<Guid, Figure> _figures = new();
    private long _clockOffsetMs; // hora del servidor − hora local
    private bool _haveClock;

    public int FigureCount => _figures.Count;

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveResidents(byte[] data)
    {
        (long ServerNowMs, List<ResidentView> Views, List<Guid> Removed) packet;
        try
        {
            packet = ResidentSyncCodec.Decode(data);
        }
        catch (ArgumentException)
        {
            return;
        }
        var offset = packet.ServerNowMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _clockOffsetMs = _haveClock ? (long)(_clockOffsetMs * 0.8 + offset * 0.2) : offset;
        _haveClock = true;
        foreach (var id in packet.Removed)
            if (_figures.Remove(id, out var gone))
                gone.Root.QueueFree();
        foreach (var view in packet.Views)
        {
            if (_figures.TryGetValue(view.Id, out var figure))
            {
                figure.View = view;
                if (figure.Root.GetNodeOrNull<Node3D>("Bolsa") is { } held)
                    held.Visible = view.Bag;
                continue;
            }
            var root = new Node3D { Name = $"Habitante_{view.Id:N}" };
            var model = CharacterModel.Build(ResidentLooks.For(view));
            root.AddChild(model);
            var bag = new MeshInstance3D
            {
                Name = "Bolsa",
                Mesh = new BoxMesh { Size = new Vector3(0.26f, 0.32f, 0.18f) },
                Position = new Vector3(0.34f, 0.78f, -0.05f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.5f, 0.33f), Roughness = 0.95f },
                Visible = view.Bag,
            };
            root.AddChild(bag);
            root.AddChild(new Label3D
            {
                Text = view.Name,
                Position = new Vector3(0, 2.15f, 0),
                FontSize = 28,
                PixelSize = 0.004f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(0.95f, 0.92f, 0.85f),
                OutlineSize = 6,
                VisibilityRangeEnd = 14,
            });
            AddChild(root);
            figure = new Figure { View = view, Root = root, Model = model };
            _figures[view.Id] = figure;
            Place(figure, ServerNowMs(), 0, snap: true);
        }
        if (!_loggedFirst && _figures.Count > 0)
        {
            _loggedFirst = true;
            GD.Print($"[DISTRITO] VIDA_CLIENTE habitantes_cerca={_figures.Count} reloj_ms={_clockOffsetMs}");
        }
    }

    private bool _loggedFirst;

    private long ServerNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _clockOffsetMs;

    private void AnimateFigures(float delta)
    {
        if (_figures.Count == 0)
            return;
        var now = ServerNowMs();
        foreach (var figure in _figures.Values)
            Place(figure, now, delta, snap: false);
    }

    private static void Place(Figure f, long nowMs, float delta, bool snap)
    {
        var visible = f.View.Visible(nowMs);
        f.Root.Visible = visible;
        if (!visible)
            return;
        var (x, z, dx, dz, walking) = f.View.At(nowMs);
        var target = new Vector3(x, 0, z);
        // Si el tramo nuevo empieza un poco corrido (redondeos, reloj), se acomoda de a poco en vez de saltar.
        var position = snap || f.Last.DistanceTo(target) > 3 ? target : f.Last.Lerp(target, Mathf.Clamp(delta * 10, 0, 1));
        var speed = delta > 0 ? f.Last.DistanceTo(position) / delta : 0;
        f.Last = position;
        f.Root.GlobalPosition = position;
        if (walking)
        {
            var yaw = Mathf.Atan2(-dx, -dz); // el modelo mira hacia −Z
            f.Root.Rotation = new Vector3(0, Mathf.LerpAngle(f.Root.Rotation.Y, yaw, snap ? 1 : Mathf.Clamp(delta * 8, 0, 1)), 0);
        }
        f.Model.Animate(walking ? Math.Max(speed, (float)LifeSimulation.WalkSpeed) : 0, delta);
    }

    public string Summary(DateTimeOffset now)
    {
        var counts = _states.Values.GroupBy(s => s.Travelling(now) ? "caminando" : s.Activity).OrderBy(g => g.Key).Select(g => $"{g.Key.Replace(' ', '_')}={g.Count()}");
        return $"hora_bellmont={BellmontClock.TimeOfDay(now):hh\\:mm} {string.Join(" ", counts)} decisiones={_sim?.DecisionsMade ?? 0} " +
               $"amistades={CountTies().Friends} rivalidades={CountTies().Rivals} cerca={_near} intermedio={_middle} lejos={_states.Count - _near - _middle} celdas_caminables={CityNavigation.WalkableCells}";
    }

    /// <summary>Qué hace cada habitante y por qué (herramienta de inspección del panel de administración).</summary>
    public string Report(DateTimeOffset now)
    {
        if (_config is null)
            return "Los habitantes todavía no se cargaron (hace falta el backend).";
        var lines = new List<string>
        {
            $"Hora de Bellmont {BellmontClock.TimeOfDay(now):hh\\:mm} ({(BellmontClock.IsNight(now) ? "de noche" : "de día")}) · {_residents.Count} habitantes · cerca de algún jugador: {_near} (se dibujan) · intermedio: {_middle} · lejos: {_states.Count - _near - _middle}",
            "",
        };
        var professions = _config.Professions.ToDictionary(p => p.Code, p => p.Name);
        foreach (var r in _residents.Values.OrderBy(r => r.Profession).ThenBy(r => r.LastName))
        {
            var s = _states[r.Id];
            var doing = s.Travelling(now) ? $"va a {s.Decision}" : s.Decision ?? s.Activity;
            lines.Add($"{r.FullName} ({professions.GetValueOrDefault(r.Profession, r.Profession).ToLowerInvariant()}, {r.Age}): {doing}.");
            lines.Add($"    Por qué: {s.Reason}. Hambre {s.Hunger:0.00} · cansancio {s.Fatigue:0.00} · ganas de ver gente {s.Social:0.00}.");
            lines.Add($"    Relaciones: {LifeSociety.Describe(s, _residents)}.");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Anota un hecho importante de la vida de un habitante (se guarda con el próximo guardado).</summary>
    public void Record(Guid resident, string kind, string text) =>
        _pendingEvents.Add(new ResidentEvent(resident, DateTimeOffset.UtcNow, kind, text));

    private async Task SaveAsync()
    {
        _saving = true;
        try
        {
            var events = _pendingEvents.ToList();
            var result = await _network.Backend!.SaveResidentsAsync(_network.ServerKey, new LifeStateBatch(_states.Values.ToList(), events));
            if (result.IsSuccess)
                _pendingEvents.RemoveRange(0, Math.Min(events.Count, _pendingEvents.Count));
        }
        finally
        {
            _saving = false;
        }
    }
}
