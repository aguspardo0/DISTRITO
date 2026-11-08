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

    private GameNetwork _network = null!;
    private readonly Dictionary<Guid, Resident> _residents = new();
    private readonly Dictionary<Guid, ResidentState> _states = new();
    private readonly List<ResidentEvent> _pendingEvents = new();
    private LifeConfig? _config;
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
        _untilSave -= delta;
        if (_untilSave <= 0 && !_saving)
        {
            _untilSave = SaveSeconds;
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
            GD.Print($"[DISTRITO] VIDA habitantes={_residents.Count} hora_bellmont={BellmontClock.TimeOfDay(now):hh\\:mm} " +
                     $"noche={(BellmontClock.IsNight(now) ? "si" : "no")} " +
                     string.Join(" ", _residents.Values.GroupBy(r => r.Profession).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
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
