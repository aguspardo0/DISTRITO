using Distrito.Game.Player;
using Distrito.Server.Core.Interest;
using Godot;

namespace Distrito.Game.Network;

/// <summary>
/// Servidor: decide qué personajes recibe cada cliente, según distancia (<see cref="InterestRules"/>).
/// Usa los filtros de visibilidad de MultiplayerSynchronizer: si un personaje deja de ser visible
/// para un peer, Godot deja de mandarle sus datos y lo elimina de su escena (y lo vuelve a crear al acercarse).
/// Se recalcula unas pocas veces por segundo, no cada frame.
/// </summary>
public partial class InterestManager : Node
{
    private const double UpdateIntervalSeconds = 0.25;

    private readonly Dictionary<(long Target, long Observer), bool> _visible = new();
    private readonly Dictionary<long, PlayerCharacter> _players = new();
    private InterestRules _rules = null!;
    private double _untilUpdate;

    public float Radius => _rules.EnterRadius;

    public void Initialize(float radius) => _rules = new InterestRules(radius);

    /// <summary>Se llama en el servidor al crear el personaje de un peer.</summary>
    public void Register(PlayerCharacter player)
    {
        var target = player.PeerId;
        _players[target] = player;
        var sync = player.GetNode<MultiplayerSynchronizer>("Sync");
        sync.VisibilityUpdateMode = MultiplayerSynchronizer.VisibilityUpdateModeEnum.None;
        sync.AddVisibilityFilter(Callable.From((int observer) => IsVisible(target, observer)));
        player.TreeExiting += () => Unregister(target);
    }

    private void Unregister(long target)
    {
        _players.Remove(target);
        foreach (var key in _visible.Keys.Where(k => k.Target == target || k.Observer == target).ToList())
            _visible.Remove(key);
    }

    /// <summary>Cantidad de pares (observador, objetivo) visibles ahora. Para métricas.</summary>
    public int VisiblePairs => _visible.Count(kv => kv.Value && kv.Key.Target != kv.Key.Observer);

    private bool IsVisible(long target, long observer)
    {
        if (target == observer)
            return true; // cada uno siempre ve su propio personaje
        if (_visible.TryGetValue((target, observer), out var cached))
            return cached;
        return Compute(target, observer, wasVisible: false);
    }

    private bool Compute(long target, long observer, bool wasVisible)
    {
        if (!_players.TryGetValue(target, out var t) || !_players.TryGetValue(observer, out var o))
            return false; // quien todavía no tiene personaje no ve a nadie
        var tp = t.GlobalPosition;
        var op = o.GlobalPosition;
        var visible = _rules.IsVisible(new System.Numerics.Vector3(op.X, op.Y, op.Z), new System.Numerics.Vector3(tp.X, tp.Y, tp.Z), wasVisible);
        _visible[(target, observer)] = visible;
        return visible;
    }

    public override void _Process(double delta)
    {
        if (!Multiplayer.IsServer() || _players.Count == 0)
            return;
        _untilUpdate -= delta;
        if (_untilUpdate > 0)
            return;
        _untilUpdate = UpdateIntervalSeconds;

        foreach (var target in _players.Keys)
            foreach (var observer in _players.Keys)
                if (target != observer)
                    Compute(target, observer, _visible.GetValueOrDefault((target, observer)));

        foreach (var player in _players.Values)
            player.GetNode<MultiplayerSynchronizer>("Sync").UpdateVisibility();
    }
}
