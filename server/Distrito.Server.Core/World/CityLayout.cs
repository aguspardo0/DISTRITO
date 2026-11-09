using System.Numerics;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.World;

/// <summary>Tipo de distrito: el Mercado (comercios), Los Muelles, el barrio de una familia o una avenida.</summary>
public enum DistrictKind { Market, Docks, Barrio, Avenue, Downtown }

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

/// <summary>Una manzana del Centro (fase 1 del mapa): rectángulo en planta, rodeado de calle con veredas.</summary>
public sealed record DowntownBlock(float MinX, float MinZ, float MaxX, float MaxZ)
{
    public float Width => MaxX - MinX;
    public float Depth => MaxZ - MinZ;
    public bool Contains(float x, float z, float margin = 0) => x >= MinX - margin && x <= MaxX + margin && z >= MinZ - margin && z <= MaxZ + margin;
}

/// <summary>Edificio del Centro: planta, altura, pisos, tono (índice de la paleta) y hacia dónde da su frente (normal en planta).</summary>
public sealed record DowntownBuilding(MapBuilding Plan, float Height, int Stories, int Tone, float FrontX, float FrontZ);

/// <summary>
/// Local vacío en planta baja de un edificio del Centro, listo para cuando se definan las reglas de los negocios físicos
/// (alquiler o compra). Posición: el centro de la vidriera, sobre la vereda; (NX, NZ): hacia dónde mira.
/// </summary>
public sealed record EmptyShop(string Code, float X, float Z, float NX, float NZ, float Width);

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
/// los proveedores y comercios están en la Calle Principal (antes «el Mercado», terreno neutral) y Los Muelles quedan en
/// el borde este, contra el agua, al final de la Avenida del Puerto (rehecho el 2026-11-09).
/// Los barrios no se tocan entre sí: solo se llega a cada uno por su avenida desde el Mercado.
///
///                    Barrio Valenti (norte)
///                          │ Avenida Norte
///   Barrio Moretti ── Av. Oeste ── CALLE PRINCIPAL ── Avenida del Puerto ── Los Muelles (borde este)
///                          │ Avenida Sur
///                    Barrio Caruso (sur)
///
/// Fase 1 del mapa (decisión del equipo, 2026-11-08): alrededor de todo esto, <b>El Centro</b>, un cuadrado de 600 m
/// con grilla de manzanas de los años 50 (generada, después se retoca a mano), calles con autos y veredas, edificios de
/// época sin interior y locales vacíos en planta baja. Los barrios siguen cerrados por un muro: solo se entra por su
/// avenida.
///
/// El mismo plano lo usan el mundo 3D (servidor y clientes), las paredes del borde, el mapa (M), las zonas de combate
/// y los puntos de aparición. Los edificios de los barrios se generan con una semilla fija: son iguales en todos lados.
/// </summary>
public static class CityLayout
{
    public const string MarketZone = "el-mercado";
    public const float HqFront = 76f; // el cuartel ocupa el fondo del eje central

    /// <summary>
    /// La Calle Principal (rehecha a pedido del equipo, 2026-11-09; el código sigue siendo «el-mercado»): una calle
    /// comercial de 135 m que va de la entrada del Barrio Moretti (Avenida Oeste) a la calle del Centro en x = 114, con
    /// los negocios a los dos lados, cada uno en su lote. Ver <see cref="MainStreet"/>.
    /// </summary>
    public static readonly District Market = new(MarketZone, "Calle Principal", DistrictKind.Market, -30, -30, 105, 30);

    /// <summary>Los Muelles: zona libre en el borde este de la ciudad, contra el agua (antes estaban pegados al Mercado).</summary>
    public static readonly District Docks = new(TerritoryRules.Docks, "Los Muelles", DistrictKind.Docks, 246, -30, 300, 30);

    /// <summary>Cuánto se corrieron Los Muelles al mudarse al borde (sus galpones y su gente se corren igual).</summary>
    public const float DocksShift = 216f;

    /// <summary>
    /// Avenida del Puerto: sigue derecho a la Calle Principal (mismo eje, z = 0) hasta Los Muelles, entre las manzanas
    /// del Centro. Es calle del Centro (no un distrito aparte): solo corta las manzanas.
    /// </summary>
    public static readonly DowntownBlock PortAvenue = new(105, -9, 246, 9);

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

    /// <summary>Los distritos de antes del Centro (el Mercado, Los Muelles, las avenidas y los barrios). No se pisan.</summary>
    public static readonly IReadOnlyList<District> Districts =
        [Market, Docks, WestAvenue, NorthAvenue, SouthAvenue, Moretti.Area, Valenti.Area, Caruso.Area];

    // --- El Centro (fase 1 del mapa) ---

    public const float DowntownHalf = 300f;
    /// <summary>Medio ancho del cuadrado del Mercado original (60 m): la grilla del Centro se arma alrededor de él.</summary>
    public const float CoreHalf = 30f;
    /// <summary>De eje a eje de calle (manzanas de 57 m con calles de 18 m: 11 m de calzada y dos veredas de 3,5 m). Ver <see cref="StreetLines"/>.</summary>
    public const float BlockPitch = 75f;
    public const float StreetWidth = 18f;
    public const float SidewalkWidth = 3.5f;

    /// <summary>Todo el cuadrado de 600 m. Rodea a los distritos de antes (que tienen prioridad donde se superponen).</summary>
    public static readonly District Downtown = new(TerritoryRules.Downtown, "El Centro", DistrictKind.Downtown, -DowntownHalf, -DowntownHalf, DowntownHalf, DowntownHalf);

    // Límites totales (para el mapa y validaciones rápidas).
    public static readonly float MinX = Math.Min(Districts.Min(d => d.MinX), Downtown.MinX), MaxX = Math.Max(Districts.Max(d => d.MaxX), Downtown.MaxX);
    public static readonly float MinZ = Math.Min(Districts.Min(d => d.MinZ), Downtown.MinZ), MaxZ = Math.Max(Districts.Max(d => d.MaxZ), Downtown.MaxZ);

    public static District? DistrictAt(float x, float z) =>
        Districts.FirstOrDefault(d => d.Kind != DistrictKind.Avenue && d.Contains(x, z)) ?? Districts.FirstOrDefault(d => d.Contains(x, z))
        ?? (Downtown.Contains(x, z) ? Downtown : null);

    /// <summary>¿Es calle del Centro (no de los distritos de antes)?</summary>
    public static bool InDowntown(float x, float z) => Downtown.Contains(x, z) && !Districts.Any(d => d.Contains(x, z));

    /// <summary>¿Se puede estar ahí? Con margen: el punto y los cuatro que lo rodean a esa distancia (sirve en las uniones).</summary>
    public static bool IsWalkable(float x, float z, float margin = 0f)
    {
        bool In(float px, float pz) => Downtown.Contains(px, pz) || Districts.Any(d => d.Contains(px, pz)) || InteriorLayout.At(px, pz) is not null;
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

    // --- El Centro: manzanas, edificios y locales vacíos ---

    /// <summary>
    /// Ejes de las calles del Centro (en x y en z), centrados en el Mercado (pedido del equipo, 2026-11-08): la primera
    /// calle rodea al Mercado (eje a ±39 m, justo pegada a su borde) y de ahí sigue la grilla cada 75 m hasta el borde.
    /// Así el Mercado queda como la manzana del medio y sus calles siguen derecho por toda la ciudad. En el borde
    /// (±300) no hay calle: la última fila de edificios da contra la pared.
    /// </summary>
    public static readonly float[] StreetLines = MakeLines();

    private static float[] MakeLines()
    {
        var first = CoreHalf + StreetWidth / 2; // la grilla sigue centrada donde estaba el Mercado
        var lines = new List<float> { -DowntownHalf, DowntownHalf };
        for (var v = first; v < DowntownHalf - StreetWidth; v += BlockPitch)
        {
            lines.Add(v);
            lines.Add(-v);
        }
        return lines.OrderBy(v => v).ToArray();
    }

    /// <summary>Distancia al eje de calle más cercano (sin contar el borde de la ciudad).</summary>
    public static float ToStreetLine(float v) =>
        StreetLines.Where(l => MathF.Abs(l) < DowntownHalf).Min(l => MathF.Abs(v - l));

    /// <summary>
    /// Manzanas: entre los ejes de calle. Alrededor de cada distrito de antes queda una calle de 18 m; donde una
    /// manzana lo pisaría, se recorta (y si queda muy chica, no va).
    /// </summary>
    public static readonly IReadOnlyList<DowntownBlock> DowntownBlocks = MakeBlocks();

    private static List<DowntownBlock> MakeBlocks()
    {
        var lines = StreetLines;
        float HalfAt(float line) => MathF.Abs(line) >= DowntownHalf ? 0 : StreetWidth / 2;
        var keepOut = Districts.Select(d => new DowntownBlock(d.MinX - StreetWidth, d.MinZ - StreetWidth, d.MaxX + StreetWidth, d.MaxZ + StreetWidth)).ToList();
        keepOut.Add(PortAvenue); // 18 m entre manzanas, como cualquier calle (las veredas son de las manzanas)
        var blocks = new List<DowntownBlock>();
        for (var j = 0; j + 1 < lines.Length; j++)
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            var pieces = new List<DowntownBlock> { new(lines[i] + HalfAt(lines[i]), lines[j] + HalfAt(lines[j]), lines[i + 1] - HalfAt(lines[i + 1]), lines[j + 1] - HalfAt(lines[j + 1])) };
            foreach (var cut in keepOut)
                pieces = pieces.SelectMany(p => Cut(p, cut)).ToList();
            blocks.AddRange(pieces.Where(p => p.Width >= 16 && p.Depth >= 16));
        }
        return blocks;
    }

    /// <summary>Lo que queda de un rectángulo al sacarle otro (hasta cuatro pedazos).</summary>
    private static IEnumerable<DowntownBlock> Cut(DowntownBlock p, DowntownBlock c)
    {
        if (c.MinX >= p.MaxX || c.MaxX <= p.MinX || c.MinZ >= p.MaxZ || c.MaxZ <= p.MinZ)
        {
            yield return p;
            yield break;
        }
        if (c.MinX > p.MinX) yield return p with { MaxX = c.MinX };
        if (c.MaxX < p.MaxX) yield return p with { MinX = c.MaxX };
        var x1 = Math.Max(p.MinX, c.MinX);
        var x2 = Math.Min(p.MaxX, c.MaxX);
        if (c.MinZ > p.MinZ) yield return new DowntownBlock(x1, p.MinZ, x2, c.MinZ);
        if (c.MaxZ < p.MaxZ) yield return new DowntownBlock(x1, c.MaxZ, x2, p.MaxZ);
    }

    /// <summary>Paleta del Centro: ladrillo, piedra caliza, brownstone, crema, gris y ladrillo oscuro.</summary>
    public static readonly (float R, float G, float B)[] DowntownPalette =
        [(0.55f, 0.3f, 0.22f), (0.74f, 0.7f, 0.6f), (0.42f, 0.3f, 0.24f), (0.8f, 0.74f, 0.6f), (0.5f, 0.5f, 0.5f), (0.38f, 0.22f, 0.18f)];

    private static readonly (List<DowntownBuilding> Buildings, List<EmptyShop> Shops) Generated = MakeDowntown();
    public static readonly IReadOnlyList<DowntownBuilding> DowntownBuildings = Generated.Buildings;
    /// <summary>
    /// El Café La Perla (la competencia de los puestos de café, ver «competidores» en config/negocios.json): ocupa el
    /// local del Centro más cercano al Mercado. No es de ningún jugador.
    /// </summary>
    public static readonly EmptyShop RivalCafe = Generated.Shops.OrderBy(s => s.X * s.X + s.Z * s.Z).First() with { Code = "cafe-la-perla" };

    public static readonly IReadOnlyList<EmptyShop> EmptyShops = Generated.Shops.OrderBy(s => s.X * s.X + s.Z * s.Z).Skip(1).OrderBy(s => s.Code).ToList();

    /// <summary>
    /// Cada manzana se llena de edificios pegados, de 9 a 18 m de frente, mirando a la calle; las manzanas hondas tienen
    /// dos filas con un pasaje de 4 m en el medio. De 2 a 6 pisos, y cerca del medio alguna torre de hasta 15. Algún
    /// baldío. En planta baja, uno de cada cinco o seis tiene un local vacío. Semilla fija: igual en todos lados.
    /// </summary>
    private static (List<DowntownBuilding>, List<EmptyShop>) MakeDowntown()
    {
        var buildings = new List<DowntownBuilding>();
        var shops = new List<EmptyShop>();
        var rng = new Random(1952);
        foreach (var block in DowntownBlocks)
        {
            var alongX = block.Width >= block.Depth;
            var length = alongX ? block.Width : block.Depth;
            var depth = alongX ? block.Depth : block.Width;
            var rows = depth >= 34 ? 2 : 1;
            var rowDepth = rows == 2 ? (depth - 4) / 2 : depth;
            for (var row = 0; row < rows; row++)
            {
                // Hacia dónde da el frente: en dos filas, cada una a su calle; en una sola, a cualquiera de las dos.
                var side = rows == 2 ? (row == 0 ? -1 : 1) : (rng.NextDouble() < 0.5 ? -1 : 1);
                // Del lado del borde de la ciudad no hay calle: ese frente mira hacia adentro (al pasaje o a la otra calle).
                var lo = alongX ? block.MinZ : block.MinX;
                var hi = alongX ? block.MaxZ : block.MaxX;
                if (side < 0 && lo <= -DowntownHalf + 0.01f || side > 0 && hi >= DowntownHalf - 0.01f)
                    side = -side;
                var a0 = row == 0 ? 0 : depth - rowDepth;
                var cursor = 0f;
                while (cursor < length - 0.01f)
                {
                    var width = 9f + (float)rng.NextDouble() * 9f;
                    if (length - cursor - width < 9f)
                        width = length - cursor; // el último se estira hasta la esquina
                    var empty = rng.NextDouble() < 0.06;
                    var (u1, u2, v1, v2) = (cursor, cursor + width, a0, a0 + rowDepth);
                    cursor += width;
                    if (empty)
                        continue; // baldío
                    var plan = alongX
                        ? new MapBuilding(block.MinX + (u1 + u2) / 2, block.MinZ + (v1 + v2) / 2, u2 - u1, v2 - v1)
                        : new MapBuilding(block.MinX + (v1 + v2) / 2, block.MinZ + (u1 + u2) / 2, v2 - v1, u2 - u1);
                    var far = Math.Max(Math.Abs(plan.X), Math.Abs(plan.Z));
                    var stories = far < 190 && rng.NextDouble() < 0.12 ? 7 + rng.Next(0, 9) : 2 + rng.Next(0, 5);
                    var (fx, fz) = alongX ? (0f, (float)side) : ((float)side, 0f);
                    buildings.Add(new DowntownBuilding(plan, stories * 3.6f + 0.6f, stories, rng.Next(DowntownPalette.Length), fx, fz));
                    if (width >= 9 && stories <= 6 && rng.NextDouble() < 0.18)
                    {
                        var frontX = plan.X + fx * (plan.Width / 2 + 0.6f);
                        var frontZ = plan.Z + fz * (plan.Depth / 2 + 0.6f);
                        shops.Add(new EmptyShop($"local-{shops.Count + 1:000}", MathF.Round(frontX, 2), MathF.Round(frontZ, 2), fx, fz, MathF.Min(width - 2, 8)));
                    }
                }
            }
        }
        return (buildings, shops);
    }

    /// <summary>
    /// Muros de los barrios: las familias no se mezclan, así que cada barrio queda cerrado por un muro de ladrillo y solo
    /// se entra por su avenida (el hueco del ancho de la avenida, donde está el arco).
    /// </summary>
    public static IReadOnlyList<MapBuilding> WallsOf(BarrioPlan b)
    {
        const float t = 0.8f;
        var a = b.Area;
        var av = b.Avenue;
        var walls = new List<MapBuilding>();
        void Horizontal(float z, float x1, float x2)
        {
            foreach (var (p, q) in Subtract(x1, x2, av.MaxZ == z || av.MinZ == z ? new[] { (av.MinX, av.MaxX) } : Array.Empty<(float, float)>()))
                walls.Add(new MapBuilding((p + q) / 2, z, q - p, t));
        }
        void Vertical(float x, float z1, float z2)
        {
            foreach (var (p, q) in Subtract(z1, z2, av.MaxX == x || av.MinX == x ? new[] { (av.MinZ, av.MaxZ) } : Array.Empty<(float, float)>()))
                walls.Add(new MapBuilding(x, (p + q) / 2, t, q - p));
        }
        Horizontal(a.MinZ, a.MinX, a.MaxX);
        Horizontal(a.MaxZ, a.MinX, a.MaxX);
        Vertical(a.MinX, a.MinZ, a.MaxZ);
        Vertical(a.MaxX, a.MinZ, a.MaxZ);
        return walls;
    }

    public static readonly IReadOnlyList<MapBuilding> BarrioWalls = Barrios.SelectMany(WallsOf).ToList();

    /// <summary>¿Hay muro de barrio entre dos puntos cercanos? (para las pruebas y las validaciones)</summary>
    public static bool InsideWall(float x, float z) =>
        BarrioWalls.Any(w => MathF.Abs(x - w.X) <= w.Width / 2 && MathF.Abs(z - w.Z) <= w.Depth / 2);

    /// <summary>
    /// Para los habitantes (vida de los NPC): en el Centro caminan por las veredas, cruzan en las esquinas y usan la
    /// calle que rodea a los distritos de antes; no se meten en la calzada ni en los pasajes de las manzanas.
    /// </summary>
    public static bool NpcArea(float x, float z)
    {
        if (Districts.Any(d => d.Contains(x, z)))
            return true;
        if (!Downtown.Contains(x, z))
            return false;
        if (Districts.Any(d => x >= d.MinX - StreetWidth && x <= d.MaxX + StreetWidth && z >= d.MinZ - StreetWidth && z <= d.MaxZ + StreetWidth))
            return true;
        if (ToStreetLine(x) <= StreetWidth / 2 && ToStreetLine(z) <= StreetWidth / 2)
            return true; // esquina (cruce peatonal)
        if (PortAvenue.Contains(x, z) && ToStreetLine(x) <= StreetWidth / 2)
            return true; // cruce de la Avenida del Puerto en cada calle
        return DowntownBlocks.Any(b => b.Contains(x, z, SidewalkWidth) && !b.Contains(x, z, -0.01f));
    }

    // --- Bordes ---

    /// <summary>
    /// Paredes invisibles: el borde del Centro (el cuadrado de 600 m). Los muros de los barrios son de verdad (se ven) y
    /// están en <see cref="BarrioWalls"/>.
    /// </summary>
    public static IReadOnlyList<EdgeWall> Edges()
    {
        var d = Downtown;
        return
        [
            new EdgeWall(d.MinX, d.MinZ, d.MaxX, d.MinZ), new EdgeWall(d.MinX, d.MaxZ, d.MaxX, d.MaxZ),
            new EdgeWall(d.MinX, d.MinZ, d.MinX, d.MaxZ), new EdgeWall(d.MaxX, d.MinZ, d.MaxX, d.MaxZ),
        ];
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
