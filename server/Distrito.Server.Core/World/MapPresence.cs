namespace Distrito.Server.Core.World;

/// <summary>Un jugador en el mapa: solo nombre y posición en planta. No es una entidad 3D.</summary>
public sealed record MapMarker(long Peer, string Name, float X, float Z);

/// <summary>Un edificio del barrio visto desde arriba (centro y tamaño en planta).</summary>
public sealed record MapBuilding(float X, float Z, float Width, float Depth);

/// <summary>
/// Quién aparece en el mapa de quién.
/// Decisión del equipo (2026-10-31): todos ven a todos los conectados. Las otras opciones quedan para cuando haya
/// amigos/familias con permisos (Etapa C de la hoja de ruta).
/// </summary>
public enum MapVisibility { Everyone }

/// <summary>
/// Presencia cartográfica, separada de la visibilidad 3D: el mapa recibe datos reducidos (nombre y posición
/// redondeada, una vez por segundo) de todos, aunque estén fuera del radio de interés y no se carguen sus modelos.
/// </summary>
public static class MapPresence
{
    /// <summary>Frecuencia de envío del mapa.</summary>
    public const double IntervalSeconds = 1.0;

    /// <summary>Precisión de la posición en el mapa (metros).</summary>
    public const float Resolution = 0.5f;

    // Límites del mundo en planta: el rectángulo que contiene toda la ciudad (ver CityLayout; no todo es caminable).
    public static readonly float MinX = CityLayout.MinX, MaxX = CityLayout.MaxX, MinZ = CityLayout.MinZ, MaxZ = CityLayout.MaxZ;

    /// <summary>Los Muelles: zona libre (sin protección de PvP), en el borde este de la ciudad.</summary>
    public static readonly float DocksStartX = CityLayout.Docks.MinX;

    public static float Quantize(float v) => MathF.Round(v / Resolution) * Resolution;

    public static IReadOnlyList<MapMarker> VisibleTo(long viewer, IEnumerable<MapMarker> everyone, MapVisibility policy = MapVisibility.Everyone) =>
        policy switch
        {
            MapVisibility.Everyone => everyone.Select(m => m with { X = Quantize(m.X), Z = Quantize(m.Z) }).OrderBy(m => m.Peer).ToList(),
            _ => everyone.Where(m => m.Peer == viewer).ToList(),
        };

    /// <summary>
    /// Galpones y pilas de cajones de Los Muelles (los construye el juego desde esta lista). Se mudaron al borde este
    /// el 2026-11-09: la misma disposición de antes, corrida <see cref="CityLayout.DocksShift"/> metros.
    /// </summary>
    public static readonly IReadOnlyList<MapBuilding> DocksBuildings = new MapBuilding[]
    {
        new(42, -20, 12, 9), new(60, -20, 14, 9), new(77, -21, 10, 8),
        new(46, 19, 10, 7), new(66, 20, 14, 7), new(79, 14, 6, 4),
        new(54, 9, 2.5f, 2.5f), new(71, -10, 3, 2), new(38, 9, 2, 3),
    }.Select(b => b with { X = b.X + CityLayout.DocksShift }).ToList();

    /// <summary>Casino «El Trébol»: en su lote de la Calle Principal (lo construye el juego).</summary>
    public static readonly MapBuilding Casino = MainStreet.Lot("casino").Plan;

    /// <summary>
    /// Planta de toda la ciudad: la Calle Principal (los locales con interior, solo sus paredes: se entra por la puerta),
    /// Los Muelles, los barrios, sus muros y el Centro.
    /// </summary>
    public static readonly IReadOnlyList<MapBuilding> Buildings =
    [
        .. MainStreet.Buildings.Where(l => InteriorLayout.Find(l.Code) is null).Select(l => l.Plan),
        .. InteriorLayout.All.SelectMany(InteriorLayout.Walls), // los locales: solo sus paredes (con el hueco de la puerta)
        .. DocksBuildings,
        .. CityLayout.BarrioBuildings.Select(b => b.Plan),
        .. CityLayout.BarrioWalls,
        .. CityLayout.DowntownBuildings.Select(b => b.Plan),
    ];

    public static bool InDocks(float x, float z) => CityLayout.Docks.Contains(x, z);


    /// <summary>Formato compacto para la red: arreglos paralelos.</summary>
    public static (long[] Peers, string[] Names, float[] Coords) Pack(IReadOnlyList<MapMarker> markers) =>
        (markers.Select(m => m.Peer).ToArray(), markers.Select(m => m.Name).ToArray(),
         markers.SelectMany(m => new[] { m.X, m.Z }).ToArray());

    public static IReadOnlyList<MapMarker> Unpack(long[] peers, string[] names, float[] coords)
    {
        var count = Math.Min(peers.Length, Math.Min(names.Length, coords.Length / 2));
        var list = new List<MapMarker>(count);
        for (var i = 0; i < count; i++)
            list.Add(new MapMarker(peers[i], names[i], coords[2 * i], coords[2 * i + 1]));
        return list;
    }
}
