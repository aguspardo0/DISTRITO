using System.Numerics;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.World;

/// <summary>Tipo de distrito: el Mercado (comercios), Los Muelles, el barrio de una familia o una avenida.</summary>
public enum DistrictKind { Market, Docks, Barrio, Avenue }

/// <summary>Un rectángulo caminable de la ciudad (en planta, metros).</summary>
public sealed record District(string Code, string Name, DistrictKind Kind, float MinX, float MinZ, float MaxX, float MaxZ, string? Family = null)
{
    public float Width => MaxX - MinX;
    public float Depth => MaxZ - MinZ;
    public float CenterX => (MinX + MaxX) / 2;
    public float CenterZ => (MinZ + MaxZ) / 2;
    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

/// <summary>Un edificio generado de un barrio, con su estilo (índice de la paleta del barrio) y altura.</summary>
public sealed record CityBuilding(MapBuilding Plan, float Height, int Tone, bool Headquarters = false);

/// <summary>Tramo de pared invisible del borde de la ciudad (una línea horizontal o vertical en planta).</summary>
public sealed record EdgeWall(float X1, float Z1, float X2, float Z2)
{
    public bool Horizontal => Z1 == Z2;
    public float Length => Horizontal ? X2 - X1 : Z2 - Z1;
}

/// <summary>
/// Barrio de una familia: rectángulo, por qué lado se entra (mirando al Mercado) y su paleta.
/// El generador trabaja en coordenadas locales: u = de la entrada hacia el fondo (0…100), v = a lo ancho (−50…50).
/// </summary>
public sealed record BarrioPlan(District Area, District Avenue, Vector2 Inward, string Family, string Boss, string Title,
    string Motto, (float R, float G, float B)[] Palette, (float R, float G, float B) Accent)
{
    public const float Size = 100f;

    /// <summary>Punto de entrada (centro del borde que da a la avenida).</summary>
    public Vector2 Entrance => Inward.X != 0
        ? new Vector2(Inward.X > 0 ? Area.MinX : Area.MaxX, Area.CenterZ)
        : new Vector2(Area.CenterX, Inward.Y > 0 ? Area.MinZ : Area.MaxZ);

    /// <summary>De coordenadas locales (u hacia adentro, v a lo ancho) a la ciudad.</summary>
    public Vector2 ToWorld(float u, float v)
    {
        var side = new Vector2(-Inward.Y, Inward.X);
        return Entrance + Inward * u + side * v;
    }

    /// <summary>Rectángulo local (u1…u2 × v1…v2) a edificio de la ciudad.</summary>
    public MapBuilding Rect(float u1, float u2, float v1, float v2)
    {
        var a = ToWorld(u1, v1);
        var b = ToWorld(u2, v2);
        return new MapBuilding((a.X + b.X) / 2, (a.Y + b.Y) / 2, MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));
    }

    /// <summary>Dónde espera el jefe: en la plaza, frente a la puerta del cuartel.</summary>
    public Vector2 BossSpot => ToWorld(CityLayout.HqFront - 2.5f, 0);
}

/// <summary>
/// Plano de Bellmont (pedido del equipo, 2026-11-01): las familias son rivales y no se mezclan, cada una en su barrio;
/// los proveedores y comercios están en el Mercado (el barrio original, terreno neutral), con Los Muelles al este.
/// Los barrios no se tocan entre sí: solo se llega a cada uno por su avenida desde el Mercado.
///
///                    Barrio Valenti (norte)
///                          │ Avenida Norte
///   Barrio Moretti ── Av. Oeste ── EL MERCADO ── Los Muelles
///                          │ Avenida Sur
///                    Barrio Caruso (sur)
///
/// El mismo plano lo usan el mundo 3D (servidor y clientes), las paredes del borde, el mapa (M), las zonas de combate
/// y los puntos de aparición. Los edificios de los barrios se generan con una semilla fija: son iguales en todos lados.
/// </summary>
public static class CityLayout
{
    public const string MarketZone = "el-mercado";
    public const float HqFront = 76f; // el cuartel ocupa el fondo del eje central

    public static readonly District Market = new(MarketZone, "El Mercado", DistrictKind.Market, -30, -30, 30, 30);
    public static readonly District Docks = new(TerritoryRules.Docks, "Los Muelles", DistrictKind.Docks, 30, -30, 84, 30);

    // Avenidas: salen del Mercado por los huecos entre edificios que ya existían.
    public static readonly District WestAvenue = new("avenida-oeste", "Avenida Oeste", DistrictKind.Avenue, -60, -5, -30, 5, EconomyCatalog.Moretti);
    public static readonly District NorthAvenue = new("avenida-norte", "Avenida Norte", DistrictKind.Avenue, 19.5f, -60, 29.5f, -30, EconomyCatalog.Valenti);
    public static readonly District SouthAvenue = new("avenida-sur", "Avenida Sur", DistrictKind.Avenue, 8, 30, 18, 60, EconomyCatalog.Caruso);

    public static readonly BarrioPlan Moretti = new(
        new District(TerritoryRules.BarrioMoretti, "Barrio Moretti", DistrictKind.Barrio, -160, -50, -60, 50, EconomyCatalog.Moretti),
        WestAvenue, new Vector2(-1, 0), EconomyCatalog.Moretti, "Don Moretti", "VILLA MORETTI", "Ladrillo, familia y palabra",
        [(0.52f, 0.25f, 0.18f), (0.6f, 0.38f, 0.22f), (0.45f, 0.3f, 0.24f), (0.66f, 0.5f, 0.34f)], (0.25f, 0.55f, 0.3f));

    public static readonly BarrioPlan Valenti = new(
        new District(TerritoryRules.BarrioValenti, "Barrio Valenti", DistrictKind.Barrio, -25.5f, -160, 74.5f, -60, EconomyCatalog.Valenti),
        NorthAvenue, new Vector2(0, -1), EconomyCatalog.Valenti, "Don Valenti", "PALAZZO VALENTI", "El dinero no duerme",
        [(0.78f, 0.74f, 0.64f), (0.3f, 0.3f, 0.33f), (0.62f, 0.58f, 0.5f), (0.2f, 0.22f, 0.28f)], (0.85f, 0.68f, 0.3f));

    public static readonly BarrioPlan Caruso = new(
        new District(TerritoryRules.BarrioCaruso, "Barrio Caruso", DistrictKind.Barrio, -37, 60, 63, 160, EconomyCatalog.Caruso),
        SouthAvenue, new Vector2(0, 1), EconomyCatalog.Caruso, "Don Caruso", "TALLERES CARUSO", "Acá se trabaja con las manos",
        [(0.4f, 0.4f, 0.38f), (0.42f, 0.3f, 0.2f), (0.5f, 0.32f, 0.22f), (0.33f, 0.36f, 0.4f)], (0.9f, 0.5f, 0.2f));

    public static readonly IReadOnlyList<BarrioPlan> Barrios = [Moretti, Valenti, Caruso];

    public static readonly IReadOnlyList<District> Districts =
        [Market, Docks, WestAvenue, NorthAvenue, SouthAvenue, Moretti.Area, Valenti.Area, Caruso.Area];

    // Límites totales (para el mapa y validaciones rápidas).
    public static readonly float MinX = Districts.Min(d => d.MinX), MaxX = Districts.Max(d => d.MaxX);
    public static readonly float MinZ = Districts.Min(d => d.MinZ), MaxZ = Districts.Max(d => d.MaxZ);

    public static District? DistrictAt(float x, float z) =>
        Districts.FirstOrDefault(d => d.Kind != DistrictKind.Avenue && d.Contains(x, z)) ?? Districts.FirstOrDefault(d => d.Contains(x, z));

    /// <summary>¿Se puede estar ahí? Con margen: el punto y los cuatro que lo rodean a esa distancia (sirve en las uniones).</summary>
    public static bool IsWalkable(float x, float z, float margin = 0f)
    {
        bool In(float px, float pz) => Districts.Any(d => d.Contains(px, pz)) || InteriorLayout.At(px, pz) is not null;
        return In(x, z) && (margin <= 0 || (In(x - margin, z) && In(x + margin, z) && In(x, z - margin) && In(x, z + margin)));
    }

    public static BarrioPlan? BarrioOf(string family) => Barrios.FirstOrDefault(b => b.Family == family);

    /// <summary>Barrio (o su avenida) en un punto, si hay.</summary>
    public static BarrioPlan? BarrioAt(float x, float z) =>
        Barrios.FirstOrDefault(b => b.Area.Contains(x, z) || b.Avenue.Contains(x, z));

    // --- Edificios de los barrios ---

    /// <summary>
    /// Cuadrícula local de 3 × 3 manzanas de 24 m con calles de 14 m. En el eje central: bulevar de entrada, plaza y
    /// el cuartel de la familia al fondo. Las otras seis manzanas se parten en lotes de alturas y tonos distintos.
    /// </summary>
    private static readonly (float A, float B)[] Cells = [(0, 24), (38, 62), (76, 100)];
    private static readonly (float A, float B)[] Rows = [(-50, -26), (-12, 12), (26, 50)];

    public static IReadOnlyList<CityBuilding> BuildingsOf(BarrioPlan barrio)
    {
        var list = new List<CityBuilding>();
        var rng = new Random(barrio.Family.Aggregate(17, (h, c) => h * 31 + c));
        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
        {
            var (u1, u2) = Cells[c];
            var (v1, v2) = Rows[r];
            if (r == 1)
            {
                if (c == 2)
                {
                    // Cuartel: ancho y alto, con la puerta hacia la plaza.
                    list.Add(new CityBuilding(barrio.Rect(HqFront, 96, -11, 11), 11f, 0, Headquarters: true));
                }
                continue; // bulevar y plaza: abiertos
            }
            // Lotes: la manzana se parte a lo largo en 2 o 3 y a lo ancho en 1 o 2, con pasajes de 2 m.
            var alongCuts = rng.Next(2, 4);
            var acrossCuts = rng.Next(1, 3);
            var du = (u2 - u1 - 2 * (alongCuts - 1)) / alongCuts;
            var dv = (v2 - v1 - 2 * (acrossCuts - 1)) / acrossCuts;
            for (var i = 0; i < alongCuts; i++)
            for (var j = 0; j < acrossCuts; j++)
            {
                if (rng.NextDouble() < 0.12) continue; // algún baldío
                var a = u1 + i * (du + 2);
                var b = v1 + j * (dv + 2);
                list.Add(new CityBuilding(barrio.Rect(a, a + du, b, b + dv), 4.5f + rng.Next(0, 9), rng.Next(barrio.Palette.Length)));
            }
        }
        return list;
    }

    public static readonly IReadOnlyList<CityBuilding> BarrioBuildings = Barrios.SelectMany(BuildingsOf).ToList();

    /// <summary>Faroles: a lo largo del bulevar y alrededor de la plaza de cada barrio (local u, v).</summary>
    public static IReadOnlyList<Vector2> LampsOf(BarrioPlan b) =>
        [b.ToWorld(6, -9), b.ToWorld(6, 9), b.ToWorld(18, -9), b.ToWorld(18, 9), b.ToWorld(40, -10), b.ToWorld(40, 10), b.ToWorld(60, -10), b.ToWorld(60, 10)];

    // --- Bordes ---

    /// <summary>
    /// Paredes invisibles: los lados de cada distrito que no están pegados a otro distrito (las uniones con las
    /// avenidas quedan abiertas). Se calculan del plano, así que al agregar un distrito se cierran solas.
    /// </summary>
    public static IReadOnlyList<EdgeWall> Edges()
    {
        var walls = new List<EdgeWall>();
        foreach (var d in Districts)
        {
            foreach (var (z, outward) in new[] { (d.MinZ, -1), (d.MaxZ, 1) })
                foreach (var (a, b) in Subtract(d.MinX, d.MaxX, Districts.Where(o => o != d && (outward < 0 ? o.MaxZ == z : o.MinZ == z)).Select(o => (o.MinX, o.MaxX))))
                    walls.Add(new EdgeWall(a, z, b, z));
            foreach (var (x, outward) in new[] { (d.MinX, -1), (d.MaxX, 1) })
                foreach (var (a, b) in Subtract(d.MinZ, d.MaxZ, Districts.Where(o => o != d && (outward < 0 ? o.MaxX == x : o.MinX == x)).Select(o => (o.MinZ, o.MaxZ))))
                    walls.Add(new EdgeWall(x, a, x, b));
        }
        return walls;
    }

    /// <summary>Lo que queda del intervalo [from, to] al sacarle los tramos cubiertos.</summary>
    private static IEnumerable<(float, float)> Subtract(float from, float to, IEnumerable<(float A, float B)> covered)
    {
        var cursor = from;
        foreach (var (a, b) in covered.Where(c => c.B > from && c.A < to).OrderBy(c => c.A))
        {
            if (a > cursor) yield return (cursor, Math.Min(a, to));
            cursor = Math.Max(cursor, b);
        }
        if (cursor < to) yield return (cursor, to);
    }
}
