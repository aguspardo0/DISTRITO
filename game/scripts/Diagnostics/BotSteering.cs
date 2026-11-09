using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Para los bots de prueba que van a un NPC: siguen el camino de la grilla de la ciudad (la misma de los habitantes,
/// <see cref="CityNavigation"/>) en lugar de ir en línea recta, así no se traban contra los edificios de la Calle
/// Principal. Si no hay camino, van derecho.
/// </summary>
public sealed class BotSteering
{
    private IReadOnlyList<(double X, double Z)> _route = [];
    private Vector2? _target;
    private int _next;

    public Vector2 Direction(Vector3 position, Vector2 target)
    {
        var here = new Vector2(position.X, position.Z);
        if (_target is not { } current || current.DistanceTo(target) > 0.5f)
        {
            _target = target;
            _route = CityNavigation.Route(here.X, here.Y, target.X, target.Y);
            _next = 0;
        }
        while (_next < _route.Count && here.DistanceTo(Point(_next)) < 1.2f)
            _next++;
        var goal = _next < _route.Count ? Point(_next) : target;
        var d = goal - here;
        return d.LengthSquared() < 1e-4f ? Vector2.Zero : d.Normalized();
    }

    private Vector2 Point(int i) => new((float)_route[i].X, (float)_route[i].Z);
}
