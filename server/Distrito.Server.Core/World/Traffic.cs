using System.Numerics;

namespace Distrito.Server.Core.World;

/// <summary>
/// Tránsito de la ciudad (pedido del equipo, 2026-11-09: «más vida»): autos que circulan solos por sus recorridos (la
/// Calle Principal y la Avenida del Puerto, un anillo grande por el Centro y otro al este), por su mano, frenando si
/// tienen a alguien adelante (una persona o el auto de adelante). Lo simula el servidor; los equipos solo dibujan.
/// </summary>
public static class TrafficRoutes
{
    /// <summary>Recorridos cerrados, ya sobre su carril (mano derecha).</summary>
    public static readonly IReadOnlyList<Vector2[]> Routes =
    [
        // A: la mitad este de la Calle Principal y la Avenida del Puerto, ida por el carril sur y vuelta por el norte, con
        // la vuelta en U en las puntas. La mitad oeste (los locales de las familias, donde aparece la gente) queda peatonal.
        [new(62, 3.2f), new(238, 3.2f), new(243, 0), new(238, -3.2f), new(62, -3.2f), new(57.5f, 0)],
        // B: anillo grande por las calles x = ±189 y z = ±189 (en el sentido de las agujas del reloj, carril de adentro).
        [new(-186, -186), new(186, -186), new(186, 186), new(-186, 186)],
        // C: anillo al este (x de 114 a 189, z de −114 a 114), cruza la Avenida del Puerto.
        [new(117, -111), new(186, -111), new(186, 111), new(117, 111)],
    ];

    /// <summary>Cuántos autos hay en cada recorrido.</summary>
    public static readonly int[] CarsPerRoute = [4, 8, 4];

    public static float Length(Vector2[] route)
    {
        var total = 0f;
        for (var i = 0; i < route.Length; i++)
            total += Vector2.Distance(route[i], route[(i + 1) % route.Length]);
        return total;
    }

    /// <summary>Punto y dirección a <paramref name="distance"/> metros del comienzo del recorrido.</summary>
    public static (Vector2 At, Vector2 Dir) At(Vector2[] route, float distance)
    {
        var total = Length(route);
        var d = ((distance % total) + total) % total;
        for (var i = 0; i < route.Length; i++)
        {
            var a = route[i];
            var b = route[(i + 1) % route.Length];
            var len = Vector2.Distance(a, b);
            if (d <= len)
            {
                var dir = len > 0 ? (b - a) / len : Vector2.UnitX;
                return (a + dir * d, dir);
            }
            d -= len;
        }
        return (route[0], Vector2.UnitX);
    }
}

/// <summary>Un auto del tránsito: en qué recorrido, cuánto avanzó y a qué velocidad va.</summary>
public sealed class TrafficCar(int route, float distance, int paint)
{
    public int Route { get; } = route;
    public int Paint { get; } = paint;
    public float Distance { get; set; } = distance;
    public float Speed { get; set; }
    public bool Parked { get; set; }
    public Vector2 Position { get; private set; }
    public Vector2 Heading { get; private set; } = Vector2.UnitX;

    public void Place()
    {
        var (at, dir) = TrafficRoutes.At(TrafficRoutes.Routes[Route], Distance);
        Position = at;
        Heading = dir;
    }
}

public sealed class TrafficSim
{
    public const float Cruise = 8.5f, Accel = 2.6f, Brake = 7f, LookAhead = 16f, HalfWidth = 1.5f;

    /// <summary>A cuántos metros (desde el centro del auto) se para de lo que tiene adelante.</summary>
    public const float StopGap = 5f;

    public List<TrafficCar> Cars { get; } = [];

    public TrafficSim()
    {
        var paint = 0;
        for (var r = 0; r < TrafficRoutes.Routes.Count; r++)
        {
            var length = TrafficRoutes.Length(TrafficRoutes.Routes[r]);
            for (var i = 0; i < TrafficRoutes.CarsPerRoute[r]; i++)
            {
                var car = new TrafficCar(r, length * i / TrafficRoutes.CarsPerRoute[r], paint++);
                car.Place();
                Cars.Add(car);
            }
        }
    }

    /// <summary>
    /// Avanza <paramref name="dt"/> segundos. <paramref name="people"/>: dónde hay gente (jugadores). Un auto frena si
    /// tiene a alguien adelante en su carril (o un auto que va para el mismo lado); los que cruzan no lo frenan.
    /// <paramref name="closedRoutes"/>: recorridos cortados (por ejemplo, la Calle Principal durante la feria).
    /// </summary>
    public void Step(float dt, IReadOnlyList<Vector2> people, ISet<int>? closedRoutes = null)
    {
        foreach (var car in Cars)
        {
            car.Parked = closedRoutes?.Contains(car.Route) == true;
            if (car.Parked)
            {
                car.Speed = 0;
                continue;
            }
            // Lo más cercano que tiene adelante (una persona, o un auto que va para el mismo lado: los que cruzan no cuentan).
            var gap = float.MaxValue;
            foreach (var p in people)
                gap = MathF.Min(gap, Ahead(car, p));
            foreach (var o in Cars)
                if (o != car && !o.Parked && Vector2.Dot(o.Heading, car.Heading) > 0.5f)
                    gap = MathF.Min(gap, Ahead(car, o.Position) - 1.5f);
            // Velocidad que le permite frenar a tiempo; si alguien se le cruzó muy cerca, frena en seco.
            var wanted = gap <= StopGap ? 0f : MathF.Min(Cruise, (gap - StopGap) * 1.6f + 0.5f);
            if (car.Speed > wanted)
                car.Speed = MathF.Max(wanted, car.Speed - (gap <= StopGap + 0.5f ? 25f : Brake) * dt);
            else
                car.Speed = MathF.Min(wanted, car.Speed + Accel * dt);
            car.Distance += car.Speed * dt;
            car.Place();
        }
    }

    /// <summary>A cuántos metros adelante en su carril está <paramref name="p"/> (o infinito si no está en su camino).</summary>
    private static float Ahead(TrafficCar car, Vector2 p)
    {
        var d = p - car.Position;
        var along = Vector2.Dot(d, car.Heading);
        if (along < 0.5f || along > LookAhead)
            return float.MaxValue;
        var side = MathF.Abs(d.X * car.Heading.Y - d.Y * car.Heading.X);
        return side < HalfWidth ? along : float.MaxValue;
    }
}
