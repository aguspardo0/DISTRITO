using Distrito.Game.Network;
using Distrito.Shared.Life;
using Godot;

namespace Distrito.Game.Life;

/// <summary>
/// Vida autónoma de los NPC en el servidor del juego (etapa 1: identidad y estado guardado). Al arrancar pide los
/// habitantes al backend (que los crea la primera vez) y cada pocos minutos guarda su estado. En las próximas etapas
/// acá corren las rutinas, necesidades y decisiones; el servidor decide y el backend solo guarda.
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

    public void Initialize(GameNetwork network) => _network = network;

    public override void _Process(double delta)
    {
        if (!Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer() || _network.Backend is null)
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
            _config = config;
            _sim = new LifeSimulation(config);
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
        foreach (var r in _residents.Values)
        {
            var before = _states[r.Id];
            var after = _sim.Advance(r, before, now);
            if (before.Money > 0 && after.Money == 0)
                Record(r.Id, "sin-plata", $"{r.FullName} se quedó sin plata.");
            _states[r.Id] = after;
        }
    }

    public string Summary(DateTimeOffset now)
    {
        var counts = _states.Values.GroupBy(s => s.Travelling(now) ? "caminando" : s.Activity).OrderBy(g => g.Key).Select(g => $"{g.Key.Replace(' ', '_')}={g.Count()}");
        return $"hora_bellmont={BellmontClock.TimeOfDay(now):hh\\:mm} {string.Join(" ", counts)} decisiones={_sim?.DecisionsMade ?? 0}";
    }

    /// <summary>Qué hace cada habitante y por qué (herramienta de inspección del panel de administración).</summary>
    public string Report(DateTimeOffset now)
    {
        if (_config is null)
            return "Los habitantes todavía no se cargaron (hace falta el backend).";
        var lines = new List<string>
        {
            $"Hora de Bellmont {BellmontClock.TimeOfDay(now):hh\\:mm} ({(BellmontClock.IsNight(now) ? "de noche" : "de día")}) · {_residents.Count} habitantes · nivel: lejos (todavía no se dibujan)",
            "",
        };
        var professions = _config.Professions.ToDictionary(p => p.Code, p => p.Name);
        foreach (var r in _residents.Values.OrderBy(r => r.Profession).ThenBy(r => r.LastName))
        {
            var s = _states[r.Id];
            var doing = s.Travelling(now) ? $"va a {s.Decision}" : s.Decision ?? s.Activity;
            lines.Add($"{r.FullName} ({professions.GetValueOrDefault(r.Profession, r.Profession).ToLowerInvariant()}, {r.Age}): {doing}.");
            lines.Add($"    Por qué: {s.Reason}. Hambre {s.Hunger:0.00} · cansancio {s.Fatigue:0.00} · ganas de ver gente {s.Social:0.00}.");
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
