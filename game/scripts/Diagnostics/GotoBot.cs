using Distrito.Game.Network;
using Distrito.Game.Player;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>Bot de prueba que camina en línea recta de un punto al siguiente de su recorrido y se queda en el último.</summary>
public partial class GotoBot : Node
{
    private const float ArriveDistance = 1.0f;

    private GameNetwork _network = null!;
    private readonly List<Vector2> _route = [];
    private int _next;
    private Vector2 _target => _route[_next];

    public bool Arrived { get; private set; }

    public void Initialize(GameNetwork network, IReadOnlyList<(float X, float Z)> route)
    {
        _network = network;
        _route.AddRange(route.Select(p => new Vector2(p.X, p.Z)));
    }

    public override void _Process(double delta)
    {
        var player = _network.LocalPlayer;
        var controller = player?.GetNodeOrNull<LocalPlayerController>("LocalController");
        if (player is null || controller is null)
            return;

        var toTarget = _target - new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z);
        if (toTarget.Length() <= ArriveDistance && _next < _route.Count - 1)
        {
            GD.Print($"[DISTRITO] [bot-destino] paso por ({_target.X:0.0}, {_target.Y:0.0})");
            _next++;
            return;
        }
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
