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

    public const float HalfExtent = 30f;

    public static float Quantize(float v) => MathF.Round(v / Resolution) * Resolution;

    public static IReadOnlyList<MapMarker> VisibleTo(long viewer, IEnumerable<MapMarker> everyone, MapVisibility policy = MapVisibility.Everyone) =>
        policy switch
        {
            MapVisibility.Everyone => everyone.Select(m => m with { X = Quantize(m.X), Z = Quantize(m.Z) }).OrderBy(m => m.Peer).ToList(),
            _ => everyone.Where(m => m.Peer == viewer).ToList(),
        };

    /// <summary>Planta del barrio de prueba (igual a los edificios de Main.tscn).</summary>
    public static readonly IReadOnlyList<MapBuilding> Buildings =
    [
        new(-14, -14, 8, 8), new(-4, -16, 6, 5), new(6, -15, 7, 7), new(16, -12, 6, 10),
        new(-16, 6, 7, 9), new(15, 8, 8, 7), new(0, 17, 12, 5),
    ];

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
