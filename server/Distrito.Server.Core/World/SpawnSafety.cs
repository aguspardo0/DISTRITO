using System.Numerics;

namespace Distrito.Server.Core.World;

/// <summary>
/// Reglas para decidir si una posición guardada es segura para hacer aparecer a un personaje.
/// Si no lo es (fuera del barrio, bajo el piso, valores corruptos), el servidor usa el punto de aparición.
/// La comprobación de "no quedar adentro de un edificio" la hace el servidor de juego con la física del mundo.
/// </summary>
public static class SpawnSafety
{
    /// <summary>Margen con los bordes de la ciudad para no aparecer contra una pared.</summary>
    public const float Margin = 1.5f;
    public const float MinHeight = -1f;
    public const float MaxHeight = 10f;

    public static bool IsWithinWorld(Vector3 p) =>
        float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
        && CityLayout.IsWalkable(p.X, p.Z, Margin)
        && p.Y >= MinHeight && p.Y <= MaxHeight;
}
