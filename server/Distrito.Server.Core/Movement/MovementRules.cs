using System.Numerics;

namespace Distrito.Server.Core.Movement;

/// <summary>Intención de movimiento enviada por un cliente. Dirección en el plano XZ del mundo.</summary>
public readonly record struct MovementInput(float X, float Z, bool Jump, bool Sprint = false)
{
    public static MovementInput None => default;
}

/// <summary>
/// Reglas de movimiento que aplica el servidor. El cliente solo propone una dirección;
/// la velocidad y el resultado los decide el servidor.
/// </summary>
public static class MovementRules
{
    public const float WalkSpeed = 5.0f;
    /// <summary>Correr (Shift).</summary>
    public const float RunSpeed = 7.5f;
    public const float JumpVelocity = 4.5f;

    /// <summary>Si un jugador cae por debajo de esta altura, el servidor lo devuelve a su punto de aparición.</summary>
    public const float KillPlaneY = -20f;

    /// <summary>
    /// Limpia una entrada recibida de la red: descarta valores no finitos y limita la dirección
    /// a longitud 1, para que nadie pueda moverse más rápido mandando vectores grandes.
    /// </summary>
    public static MovementInput Sanitize(MovementInput input)
    {
        if (!float.IsFinite(input.X) || !float.IsFinite(input.Z))
            return MovementInput.None with { Jump = input.Jump };

        var lengthSquared = input.X * input.X + input.Z * input.Z;
        if (lengthSquared <= 1f)
            return input;

        var length = MathF.Sqrt(lengthSquared);
        return input with { X = input.X / length, Z = input.Z / length };
    }

    /// <summary>Velocidad horizontal (X, Z) resultante de una entrada ya saneada.</summary>
    public static Vector2 HorizontalVelocity(MovementInput sanitized)
    {
        var speed = sanitized.Sprint ? RunSpeed : WalkSpeed;
        return new(sanitized.X * speed, sanitized.Z * speed);
    }
}
