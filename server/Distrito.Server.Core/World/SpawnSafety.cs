using System.Numerics;

namespace Distrito.Server.Core.World;

/// <summary>
/// Reglas para decidir si una posición guardada es segura para hacer aparecer a un personaje.
/// Si no lo es (fuera del barrio, bajo el piso, valores corruptos), el servidor usa el punto de aparición.
/// La comprobación de "no quedar adentro de un edificio" la hace el servidor de juego con la física del mundo.
/// </summary>
public static class SpawnSafety
{
    /// <summary>Mitad del lado del barrio de prueba (60 × 60 m), con margen para no aparecer en el borde.</summary>
    public const float WorldHalfExtent = 28.5f;
    public const float MinHeight = -1f;
    public const float MaxHeight = 10f;

    public static bool IsWithinWorld(Vector3 p) =>
        float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
        && MathF.Abs(p.X) <= WorldHalfExtent && MathF.Abs(p.Z) <= WorldHalfExtent
        && p.Y >= MinHeight && p.Y <= MaxHeight;
}
