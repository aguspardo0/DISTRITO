namespace Distrito.Server.Core.World;

/// <summary>
/// Caminos por la ciudad para los habitantes (vida de los NPC, etapa 5). Una grilla de celdas de 1,5 m sobre el plano
/// de <see cref="CityLayout"/>: caminable si está dentro de un distrito y lejos de los edificios. Busca el camino más
/// corto (A*) y lo simplifica a unas pocas esquinas en línea recta. Se arma una sola vez y es igual en todos lados.
/// Sirve también cuando crezca el mapa: sale del mismo plano que el mundo 3D.
/// </summary>
public static class CityNavigation
{
    public const float Cell = 1.5f;
    private const float BuildingMargin = 0.6f;

    private static readonly float OriginX = CityLayout.MinX, OriginZ = CityLayout.MinZ;
    private static readonly int Width = (int)MathF.Ceiling((CityLayout.MaxX - CityLayout.MinX) / Cell) + 1;
    private static readonly int Depth = (int)MathF.Ceiling((CityLayout.MaxZ - CityLayout.MinZ) / Cell) + 1;
    private static readonly Lazy<bool[]> Walkable = new(Build);

    public static int WalkableCells => Walkable.Value.Count(w => w);

    private static bool[] Build()
    {
        var grid = new bool[Width * Depth];
        for (var gz = 0; gz < Depth; gz++)
        for (var gx = 0; gx < Width; gx++)
        {
            var (x, z) = Center(gx, gz);
            grid[gz * Width + gx] = CityLayout.NpcArea(x, z) && CityLayout.IsWalkable(x, z, 0.4f);
        }
        // Los edificios (y los muros de los barrios) se marcan de una: recorre solo las celdas que tapan.
        foreach (var b in MapPresence.Buildings)
        {
            var (x1, z1) = CellOf(b.X - b.Width / 2 - BuildingMargin, b.Z - b.Depth / 2 - BuildingMargin);
            var (x2, z2) = CellOf(b.X + b.Width / 2 + BuildingMargin, b.Z + b.Depth / 2 + BuildingMargin);
            for (var gz = z1; gz <= z2; gz++)
            for (var gx = x1; gx <= x2; gx++)
            {
                var (x, z) = Center(gx, gz);
                if (MathF.Abs(x - b.X) < b.Width / 2 + BuildingMargin && MathF.Abs(z - b.Z) < b.Depth / 2 + BuildingMargin)
                    grid[gz * Width + gx] = false;
            }
        }
        return grid;
    }

    private static (float X, float Z) Center(int gx, int gz) => (OriginX + gx * Cell, OriginZ + gz * Cell);

    private static (int X, int Z) CellOf(double x, double z) =>
        (Math.Clamp((int)Math.Round((x - OriginX) / Cell), 0, Width - 1), Math.Clamp((int)Math.Round((z - OriginZ) / Cell), 0, Depth - 1));

    private static bool Free(int gx, int gz) => gx >= 0 && gz >= 0 && gx < Width && gz < Depth && Walkable.Value[gz * Width + gx];

    public static bool IsFree(double x, double z)
    {
        var (gx, gz) = CellOf(x, z);
        return Free(gx, gz);
    }

    private static bool IsWalkableExact(double x, double z) =>
        CityLayout.IsWalkable((float)x, (float)z, 0.3f) && !MapPresence.Buildings.Any(b =>
            MathF.Abs((float)x - b.X) < b.Width / 2 + 0.3f && MathF.Abs((float)z - b.Z) < b.Depth / 2 + 0.3f);

    /// <summary>La celda libre más cercana (un destino adentro de un edificio se acerca a su puerta).</summary>
    private static (int X, int Z) NearestFree((int X, int Z) c)
    {
        if (Free(c.X, c.Z))
            return c;
        for (var r = 1; r < 40; r++)
            for (var dz = -r; dz <= r; dz++)
            for (var dx = -r; dx <= r; dx++)
                if ((Math.Abs(dx) == r || Math.Abs(dz) == r) && Free(c.X + dx, c.Z + dz))
                    return (c.X + dx, c.Z + dz);
        return c;
    }

    /// <summary>
    /// Camino de (fromX, fromZ) a (toX, toZ): las esquinas por las que pasa, sin el punto de partida y con el destino
    /// al final (o la vereda más cercana, si el punto pedido cae adentro de un edificio). Si no hay camino, va en línea
    /// recta (mejor que quedarse quieto).
    /// </summary>
    public static IReadOnlyList<(double X, double Z)> Route(double fromX, double fromZ, double toX, double toZ)
    {
        var start = NearestFree(CellOf(fromX, fromZ));
        var asked = CellOf(toX, toZ);
        var goal = NearestFree(asked);
        if (goal != asked || !IsWalkableExact(toX, toZ))
        {
            var (gx, gz) = Center(goal.X, goal.Z);
            (toX, toZ) = (Math.Round(gx, 2), Math.Round(gz, 2));
        }
        if (start == goal)
            return [(toX, toZ)];
        var cells = AStar(start, goal);
        if (cells is null)
            return [(toX, toZ)];

        // Se queda solo con las esquinas: desde cada punto, el más lejano que se ve en línea recta. Si sale de un
        // punto que no es una vereda libre (la puerta de su casa), primero se para en la vereda.
        var corners = new List<(double X, double Z)>();
        var (sx, sz) = Center(start.X, start.Z);
        if (Math.Abs(sx - fromX) > 0.3 || Math.Abs(sz - fromZ) > 0.3)
            corners.Add((Math.Round(sx, 2), Math.Round(sz, 2)));
        var anchor = 0;
        while (anchor < cells.Count - 1)
        {
            var next = anchor + 1;
            for (var j = cells.Count - 1; j > anchor + 1; j--)
                if (LineFree(cells[anchor], cells[j])) { next = j; break; }
            if (next < cells.Count - 1)
            {
                var (x, z) = Center(cells[next].X, cells[next].Z);
                corners.Add((Math.Round(x, 2), Math.Round(z, 2)));
            }
            anchor = next;
        }
        corners.Add((toX, toZ));
        return corners;
    }

    private static List<(int X, int Z)>? AStar((int X, int Z) start, (int X, int Z) goal)
    {
        var open = new PriorityQueue<(int X, int Z), float>();
        var came = new Dictionary<(int, int), (int, int)>();
        var cost = new Dictionary<(int, int), float> { [start] = 0 };
        open.Enqueue(start, 0);
        var steps = 0;
        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
            {
                var path = new List<(int X, int Z)> { current };
                while (came.TryGetValue(current, out var prev)) { current = prev; path.Add(current); }
                path.Reverse();
                return path;
            }
            if (++steps > 250_000)
                return null;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                var n = (X: current.X + dx, Z: current.Z + dz);
                if (!Free(n.X, n.Z)) continue;
                if (dx != 0 && dz != 0 && (!Free(current.X + dx, current.Z) || !Free(current.X, current.Z + dz))) continue; // sin cortar esquinas
                var step = dx != 0 && dz != 0 ? 1.4142f : 1f;
                var g = cost[current] + step;
                if (cost.TryGetValue(n, out var old) && old <= g) continue;
                cost[n] = g;
                came[n] = current;
                var h = MathF.Sqrt((n.X - goal.X) * (n.X - goal.X) + (n.Z - goal.Z) * (n.Z - goal.Z));
                open.Enqueue(n, g + h);
            }
        }
        return null;
    }

    /// <summary>¿Se puede ir en línea recta de una celda a otra sin pisar celdas ocupadas?</summary>
    private static bool LineFree((int X, int Z) a, (int X, int Z) b)
    {
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Z - a.Z)) * 2;
        for (var i = 1; i < steps; i++)
        {
            var t = i / (double)steps;
            var x = a.X + (b.X - a.X) * t;
            var z = a.Z + (b.Z - a.Z) * t;
            if (!Free((int)Math.Floor(x + 0.25), (int)Math.Floor(z + 0.25)) || !Free((int)Math.Ceiling(x - 0.25), (int)Math.Ceiling(z - 0.25)))
                return false;
        }
        return true;
    }
}
