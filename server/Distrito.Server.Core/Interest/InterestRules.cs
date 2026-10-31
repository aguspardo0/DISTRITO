using System.Numerics;

namespace Distrito.Server.Core.Interest;

/// <summary>
/// Interés de red: qué entidades recibe cada jugador. Primera versión: radio fijo en el plano XZ.
/// Con histéresis (radio de salida un poco mayor que el de entrada) para que una entidad
/// en el borde no aparezca y desaparezca todo el tiempo.
/// </summary>
public sealed class InterestRules(float enterRadius, float hysteresis = 5f)
{
    public float EnterRadius { get; } = enterRadius > 0
        ? enterRadius
        : throw new ArgumentOutOfRangeException(nameof(enterRadius), "El radio debe ser positivo.");

    public float ExitRadius => EnterRadius + hysteresis;

    /// <summary>
    /// Decide si <paramref name="observer"/> debe ver a <paramref name="target"/>,
    /// sabiendo si ya lo estaba viendo (<paramref name="wasVisible"/>).
    /// </summary>
    public bool IsVisible(Vector3 observer, Vector3 target, bool wasVisible)
    {
        var dx = observer.X - target.X;
        var dz = observer.Z - target.Z;
        var distanceSquared = dx * dx + dz * dz;
        var limit = wasVisible ? ExitRadius : EnterRadius;
        return distanceSquared <= limit * limit;
    }
}
