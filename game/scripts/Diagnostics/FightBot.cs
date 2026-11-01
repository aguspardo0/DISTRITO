using Distrito.Game.Combat;
using Distrito.Game.Network;
using Distrito.Game.Player;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>Bot de prueba de PvP: busca al otro jugador, se le acerca y le pega hasta dejarlo herido.</summary>
public partial class FightBot : Node
{
    private const float CloseEnough = 1.1f;
    private GameNetwork _network = null!;
    private CombatNetwork _combat = null!;
    private double _nextPunch;
    private double _clock;

    public bool Completed { get; private set; }
    public int Hits => _combat.ConfirmedHits;

    public void Initialize(GameNetwork network, CombatNetwork combat) => (_network, _combat) = (network, combat);

    public override void _Process(double delta)
    {
        _clock += delta;
        var me = _network.LocalPlayer;
        var controller = me?.GetNodeOrNull<LocalPlayerController>("LocalController");
        if (me is null || controller is null)
            return;

        var target = _network.AllPlayers.Where(p => p != me).OrderBy(p => p.GlobalPosition.DistanceTo(me.GlobalPosition)).FirstOrDefault();
        if (target is null)
            return;
        if (target.Downed && !Completed)
        {
            Completed = true;
            GD.Print($"[DISTRITO] [bot-pelea] dejé herido a {target.DisplayName} con {Hits} piñas");
        }
        if (Completed)
        {
            controller.ScriptedDirection = Vector2.Zero;
            return;
        }

        var to = new Vector2(target.GlobalPosition.X - me.GlobalPosition.X, target.GlobalPosition.Z - me.GlobalPosition.Z);
        if (to.Length() > CloseEnough)
        {
            controller.ScriptedDirection = to.Normalized();
            return;
        }
        // Cerca: un paso cortito hacia el objetivo para quedar de frente, y piña.
        controller.ScriptedDirection = _clock % 1.0 < 0.08 ? to.Normalized() * 0.2f : Vector2.Zero;
        if (_clock >= _nextPunch)
        {
            _nextPunch = _clock + 0.7;
            _combat.RequestPunch();
        }
    }
}
