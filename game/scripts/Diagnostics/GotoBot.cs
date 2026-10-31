using Distrito.Game.Network;
using Distrito.Game.Player;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>Bot de prueba que camina en línea recta hasta un punto y se queda ahí.</summary>
public partial class GotoBot : Node
{
    private const float ArriveDistance = 1.0f;

    private GameNetwork _network = null!;
    private Vector2 _target;

    public bool Arrived { get; private set; }

    public void Initialize(GameNetwork network, (float X, float Z) target)
    {
        _network = network;
        _target = new Vector2(target.X, target.Z);
    }

    public override void _Process(double delta)
    {
        var player = _network.LocalPlayer;
        var controller = player?.GetNodeOrNull<LocalPlayerController>("LocalController");
        if (player is null || controller is null)
            return;

        var toTarget = _target - new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z);
        if (toTarget.Length() <= ArriveDistance)
        {
            controller.ScriptedDirection = Vector2.Zero;
            if (!Arrived)
                GD.Print($"[DISTRITO] [bot-destino] llegué a ({_target.X:0.0}, {_target.Y:0.0})");
            Arrived = true;
            return;
        }
        controller.ScriptedDirection = toTarget.Normalized();
    }
}
