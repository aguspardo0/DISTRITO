namespace Distrito.Server.Core.Movement;

/// <summary>
/// Última entrada conocida de un jugador. Si el cliente deja de mandar entradas (lag, desconexión),
/// el personaje se detiene en lugar de seguir caminando indefinidamente.
/// </summary>
public sealed class PlayerInputState
{
    public static readonly TimeSpan InputTimeout = TimeSpan.FromMilliseconds(250);

    private MovementInput _last;
    private double _receivedAtSeconds = double.NegativeInfinity;

    public void Update(MovementInput input, double nowSeconds)
    {
        _last = MovementRules.Sanitize(input);
        _receivedAtSeconds = nowSeconds;
    }

    public MovementInput Current(double nowSeconds) =>
        nowSeconds - _receivedAtSeconds <= InputTimeout.TotalSeconds ? _last : MovementInput.None;
}
