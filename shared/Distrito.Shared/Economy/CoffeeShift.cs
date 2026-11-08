namespace Distrito.Shared.Economy;

/// <summary>Un pedido en la barra: quién, cuántos terrones de azúcar y si va con leche o crema.</summary>
public sealed record CoffeeOrder(int Id, string Customer, int Sugar, string Milk)
{
    public string Text => Milk switch
    {
        CoffeeShiftRules.WithMilk => "con leche",
        CoffeeShiftRules.WithCream => "con crema",
        _ => "negro",
    } + Sugar switch { 0 => ", sin azúcar", 1 => ", un terrón", _ => $", {Sugar} terrones" };
}

/// <summary>Lo que entrega el empleado: hasta dónde llenó la taza, qué le puso y si la urna estaba vacía (café aguado).</summary>
public sealed record CoffeeServe(int OrderId, double Fill, int Sugar, string Milk, bool Watery = false);

/// <summary>
/// Turnos de empleado en la Esquina del café (primera hora de juego, decisión del equipo 2026-11-08): el jugador nuevo
/// trabaja unos turnos antes de abrir su propio puesto; el tutorial lo guía pero no lo obliga, y los turnos quedan
/// como ingreso extra.
///
/// Cómo se hacía el café en 1952: en las cafeterías y los diners de EE.UU. salía de la <b>urna</b> (una cafetera grande
/// de filtro, siempre caliente) y se servía en taza gruesa, negro o con leche o crema y terrones de azúcar. La máquina
/// de espresso existía, pero era cosa de los barrios italianos: queda para el Barrio Moretti.
///
/// El turno dura 10 minutos y paga <b>$3</b> (si servís al menos 6 pedidos) más <b>propinas de hasta $1</b> según qué
/// tan bien salieron. La plata sale de la clientela (<c>system:poblacion</c>), como las ventas de los negocios.
/// </summary>
public static class CoffeeShiftRules
{
    public const string WithMilk = "leche";
    public const string WithCream = "crema";

    public const int ShiftSeconds = 600;
    public const long PayCents = 300;
    public const long MaxTipCents = 100;
    public const int MinServed = 6;

    /// <summary>Cada cuánto llega un cliente, cuántos pueden esperar a la vez y cuánto esperan antes de irse.</summary>
    public const int OrderEverySeconds = 30;
    public const int MaxWaiting = 3;
    public const int PatienceSeconds = 60;

    /// <summary>La urna: tazas que salen por carga y cuánto tarda en volver a estar lista.</summary>
    public const int UrnCups = 10;
    public const int RefillSeconds = 12;

    /// <summary>La taza bien servida: hasta la línea (un dedo antes del borde). Más de 0,98 se rebalsa.</summary>
    public const double TargetFill = 0.85;
    public const double Spill = 0.98;

    /// <summary>Distancia máxima a la barra durante el turno (si se va, abandona y no cobra).</summary>
    public const float CounterRange = 7f;

    private static readonly string[] Customers =
    [
        "Un obrero del puerto", "La señora Russo", "Un camionero", "Un policía de ronda", "El diariero", "Una enfermera",
        "Un viajante de comercio", "Dos estudiantes", "El sastre Bianchi", "Un marinero", "Una maestra", "Un cobrador",
    ];

    /// <summary>Pedido número <paramref name="id"/> del turno (determinista por semilla).</summary>
    public static CoffeeOrder Order(int seed, int id)
    {
        var h = (uint)unchecked(seed * 31 + id * 7919);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        var milk = (h % 5) switch { 0 or 1 => "", 2 or 3 => WithMilk, _ => WithCream };
        return new CoffeeOrder(id, Customers[(h >> 4) % (uint)Customers.Length], (int)((h >> 9) % 3), milk);
    }

    /// <summary>
    /// Qué tan bien salió (0 a 1): la taza hasta la línea, lo que pidió (azúcar y leche) y rápido. Rebalsada no sirve; con
    /// la urna vacía sale aguado (la mitad).
    /// </summary>
    public static double Score(CoffeeOrder order, CoffeeServe serve, double waitedSeconds)
    {
        if (serve.Fill >= Spill || serve.Fill <= 0.2)
            return 0;
        var error = Math.Abs(serve.Fill - TargetFill);
        var fill = error <= 0.05 ? 1 : Math.Max(0.4, 1 - (error - 0.05) * 6);
        var wrong = (serve.Sugar == order.Sugar ? 0 : 1) + (serve.Milk == order.Milk ? 0 : 1);
        var asked = wrong switch { 0 => 1.0, 1 => 0.5, _ => 0.2 };
        var speed = waitedSeconds <= 20 ? 1 : Math.Max(0.5, 1 - (waitedSeconds - 20) / 50);
        return Math.Round(fill * asked * speed * (serve.Watery ? 0.5 : 1), 3);
    }

    /// <summary>Lo que se cobra al terminar: el sueldo si serviste lo mínimo, y la propina según el promedio.</summary>
    public static (long Pay, long Tip) Earnings(IReadOnlyList<double> scores)
    {
        if (scores.Count < MinServed)
            return (0, 0);
        var expected = ShiftSeconds / OrderEverySeconds * 0.6; // con 12 bien servidos ya se cobra toda la propina
        var tip = (long)Math.Round(MaxTipCents * scores.Average() * Math.Min(1, scores.Count / expected));
        return (PayCents, Math.Clamp(tip, 0, MaxTipCents));
    }

    public static string Verdict(double score) => score switch
    {
        >= 0.9 => "¡Perfecto!",
        >= 0.6 => "Bien.",
        >= 0.3 => "Pasable…",
        > 0 => "Mal servido.",
        _ => "¡Se rebalsó o está vacía!",
    };
}

/// <summary>Pago del turno (el servidor del juego lo pide al backend al terminar).</summary>
public sealed record CoffeeShiftPayRequest(Guid CharacterId, string RequestId, long PayCents, long TipCents, int Served);
