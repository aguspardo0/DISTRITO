using System.Numerics;

namespace Distrito.Server.Core.World;

/// <summary>
/// La Estación de Bellmont y su barrio (contenido de la demo de marzo, decisión del equipo 2026-11-09: «un barrio
/// nuevo»; para qué sirve: la llegada del jugador y vida). Queda al norte del Centro, al final de la calle x = 114:
/// se sale de la estación, se baja derecho por la Avenida de la Estación y la calle del Centro y se llega a la punta
/// este de la Calle Principal (unos 400 m). Las vías corren de este a oeste del otro lado del andén, afuera de la
/// ciudad (no se pisan); los trenes los dibuja la escena «tren» (<see cref="SceneScripts"/>).
///
///      ═══════════════ vías (z ≈ −421) ═══════════════
///      ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ andén (z −417 … −408) ▓▓▓▓▓▓▓▓▓
///      [depósito]   [  hall de la estación  ]  [tanque]
///                  plaza con fuente (z −384 … −352)
///      [casas obreras] │ Avenida │ [casas obreras]
///      ─────────────── El Centro (z = −300) ───────────
/// </summary>
public static class Station
{
    public const string Code = "barrio-estacion";
    public const float AxisX = 114f;

    public static readonly District Area = new(Code, "Barrio de la Estación", DistrictKind.Station, 60, -417, 168, -300);

    // Andén y vías.
    public const float PlatformMinX = 66, PlatformMaxX = 162, PlatformMinZ = -417, PlatformMaxZ = -408;
    public const float PlatformZ = -412.5f;
    public const float TrackZ = -421.5f;
    /// <summary>Puertas de los coches cuando el tren está parado (borde del andén).</summary>
    public static readonly float[] CoachDoors = [80, 98, 116, 134, 152];

    // El hall: un salón de 44 × 22 m con puertas al andén (tres) y a la plaza (una grande).
    public static readonly MapBuilding Hall = new(AxisX, -396, 44, 22);
    public const float WallThickness = 0.5f;
    public static readonly float[] PlatformDoors = [104, 114, 124];
    public const float PlatformDoorWidth = 3.4f, FrontDoorWidth = 5f;
    public static float HallFront => Hall.Z + Hall.Depth / 2;   // −385
    public static float HallBack => Hall.Z - Hall.Depth / 2;    // −407

    // Plaza y avenida.
    public const float PlazaMinX = 70, PlazaMaxX = 158, PlazaMinZ = -384, PlazaMaxZ = -352;
    public static readonly Vector2 Fountain = new(AxisX, -368);
    public const float AvenueHalf = 9f; // 18 m como las calles del Centro (11 de calzada y dos veredas)

    /// <summary>Paredes del hall con los huecos de las puertas (sirven para el mundo, el mapa y los caminos).</summary>
    public static readonly IReadOnlyList<MapBuilding> HallWalls = MakeHallWalls();

    private static List<MapBuilding> MakeHallWalls()
    {
        var t = WallThickness;
        var minX = Hall.X - Hall.Width / 2;
        var maxX = Hall.X + Hall.Width / 2;
        var walls = new List<MapBuilding>();
        void Along(float z, IEnumerable<(float A, float B)> gaps)
        {
            var cursor = minX;
            foreach (var (a, b) in gaps.OrderBy(g => g.A))
            {
                walls.Add(new MapBuilding((cursor + a) / 2, z, a - cursor, t));
                cursor = b;
            }
            walls.Add(new MapBuilding((cursor + maxX) / 2, z, maxX - cursor, t));
        }
        Along(HallBack + t / 2, PlatformDoors.Select(x => (x - PlatformDoorWidth / 2, x + PlatformDoorWidth / 2)));
        Along(HallFront - t / 2, [(AxisX - FrontDoorWidth / 2, AxisX + FrontDoorWidth / 2)]);
        walls.Add(new MapBuilding(minX + t / 2, Hall.Z, t, Hall.Depth));
        walls.Add(new MapBuilding(maxX - t / 2, Hall.Z, t, Hall.Depth));
        return walls;
    }

    /// <summary>Lo que hay adentro del hall: boletería, bancos y el puesto de diarios (sólidos, en planta).</summary>
    public static readonly IReadOnlyList<MapBuilding> HallFurniture =
    [
        new(95, -396, 2.4f, 10),                           // boletería (contra la pared oeste, mirando al hall)
        new(133, -396, 1.2f, 7),                           // banco largo contra la pared este
        new(106, -393, 5, 1.0f), new(122, -393, 5, 1.0f),  // bancos de espera (fuera del paso de las puertas)
    ];

    /// <summary>Postes del techo del andén (sin tapar las puertas del hall ni las de los coches).</summary>
    public static readonly IReadOnlyList<MapBuilding> CanopyPosts =
        new float[] { 70, 88, 109, 119, 142, 158 }.Select(x => new MapBuilding(x, -410, 0.4f, 0.4f)).ToList();

    /// <summary>El depósito del ferrocarril (oeste del hall) y el tanque de agua con su galpón (este).</summary>
    public static readonly MapBuilding FreightDepot = new(77, -397, 24, 16);
    public static readonly MapBuilding WaterTower = new(146, -397, 5, 5);
    public static readonly MapBuilding Shed = new(157, -397, 10, 12);

    /// <summary>Casas obreras: dos manzanas a los lados de la avenida (frente a la avenida y frente al borde).</summary>
    public static readonly DowntownBlock WestBlock = new(64, -348, AxisX - AvenueHalf - 3.5f, -304);
    public static readonly DowntownBlock EastBlock = new(AxisX + AvenueHalf + 3.5f, -348, 164, -304);

    public static readonly (float R, float G, float B)[] Palette =
        [(0.5f, 0.27f, 0.2f), (0.42f, 0.33f, 0.27f), (0.58f, 0.42f, 0.3f), (0.36f, 0.3f, 0.28f), (0.62f, 0.55f, 0.45f)];

    public static readonly IReadOnlyList<DowntownBuilding> Houses = MakeHouses();

    /// <summary>Casas angostas de dos o tres pisos, pegadas, en dos filas por manzana con un pasillo de 4 m.</summary>
    private static List<DowntownBuilding> MakeHouses()
    {
        var list = new List<DowntownBuilding>();
        var rng = new Random(1935);
        foreach (var (block, avenueSide) in new[] { (WestBlock, 1f), (EastBlock, -1f) })
        {
            var rowDepth = (block.Width - 4) / 2;
            for (var row = 0; row < 2; row++)
            {
                var x1 = row == 0 ? block.MinX : block.MaxX - rowDepth;
                // La fila de la avenida mira a la avenida; la otra, al borde (al pasillo de afuera).
                var faces = (row == 0) == (avenueSide < 0) ? avenueSide : -avenueSide;
                var cursor = block.MinZ;
                while (cursor < block.MaxZ - 0.01f)
                {
                    var width = 6.5f + (float)rng.NextDouble() * 4f;
                    if (block.MaxZ - cursor - width < 6.5f)
                        width = block.MaxZ - cursor;
                    var stories = 2 + rng.Next(0, 2);
                    var plan = new MapBuilding(x1 + rowDepth / 2, cursor + width / 2, rowDepth, width);
                    cursor += width;
                    list.Add(new DowntownBuilding(plan, stories * 3.3f + 0.8f, stories, rng.Next(Palette.Length), faces, 0));
                }
            }
        }
        return list;
    }

    /// <summary>Todo lo sólido del barrio, en planta.</summary>
    public static readonly IReadOnlyList<MapBuilding> Buildings =
        [.. HallWalls, .. HallFurniture, .. CanopyPosts, FreightDepot, WaterTower, Shed, new MapBuilding(Fountain.X, Fountain.Y, 6.4f, 6.4f), .. Houses.Select(h => h.Plan)];

    /// <summary>Faroles: plaza y avenida.</summary>
    public static readonly IReadOnlyList<Vector2> Lamps =
    [
        new(PlazaMinX + 4, PlazaMinZ + 4), new(PlazaMaxX - 4, PlazaMinZ + 4), new(PlazaMinX + 4, PlazaMaxZ - 2), new(PlazaMaxX - 4, PlazaMaxZ - 2),
        new(AxisX - 6, -368), new(AxisX + 6, -368),
        new(AxisX - AvenueHalf + 0.4f, -340), new(AxisX + AvenueHalf - 0.4f, -340), new(AxisX - AvenueHalf + 0.4f, -315), new(AxisX + AvenueHalf - 0.4f, -315),
    ];

    public static bool OnPlatform(float x, float z) => x >= PlatformMinX && x <= PlatformMaxX && z >= PlatformMinZ && z <= PlatformMaxZ;

    /// <summary>Dónde baja del tren un jugador nuevo (cada uno en su lugar del andén, frente a una puerta).</summary>
    public static Vector3 ArrivalSpot(int slot)
    {
        var door = CoachDoors[Math.Abs(slot) % CoachDoors.Length];
        var offset = (Math.Abs(slot) / CoachDoors.Length % 4 - 1.5f) * 1.4f;
        return new Vector3(door + offset, 1f, PlatformZ);
    }

    /// <summary>¿Es la posición de un personaje recién creado (todavía no se movió nunca)?</summary>
    public static bool IsNewcomer(double x, double y, double z) =>
        Math.Abs(x - Distrito.Shared.Api.ZoneIds.NewCharacterSpawn.X) < 1e-6 && Math.Abs(z - Distrito.Shared.Api.ZoneIds.NewCharacterSpawn.Z) < 1e-6
        && Math.Abs(y - Distrito.Shared.Api.ZoneIds.NewCharacterSpawn.Y) < 1e-6;
}
