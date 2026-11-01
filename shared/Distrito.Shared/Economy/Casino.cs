namespace Distrito.Shared.Economy;

/// <summary>
/// Casino «El Trébol» (aprobado por el equipo 2026-11-01): lo maneja la familia Valenti; ruleta, blackjack y dados.
/// Apuestas de $1 a $50 (hasta $200 en la mesa VIP, desde rango 6 de prestigio). La casa tiene una ventaja real chica
/// (ruleta europea 2,7 %, dados de pase 1,4 %, blackjack ~0,5 %) y lo que se pierde va a la caja de Valenti.
/// </summary>
public static class CasinoRules
{
    public const string Npc = "npc:casino-trebol";
    public const string Name = "Casino El Trébol";
    public const long MinBetCents = 100;
    public const long MaxBetCents = 5_000;
    public const long VipMaxBetCents = 20_000;

    public static readonly int[] RedNumbers = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];

    /// <summary>Apuestas de ruleta: color, paridad, mitad o un número (paga 35 a 1).</summary>
    public static readonly string[] RouletteBets = ["rojo", "negro", "par", "impar", "bajo", "alto", "numero"];

    /// <summary>Cuánto devuelve la ruleta (apuesta incluida) para una bola, o 0 si pierde.</summary>
    public static long RoulettePayout(string bet, int number, int ball, long stake) => bet switch
    {
        "numero" => ball == number ? stake * 36 : 0,
        _ when ball == 0 => 0, // el cero: pierden las apuestas sencillas (ventaja de la casa)
        "rojo" => RedNumbers.Contains(ball) ? stake * 2 : 0,
        "negro" => !RedNumbers.Contains(ball) ? stake * 2 : 0,
        "par" => ball % 2 == 0 ? stake * 2 : 0,
        "impar" => ball % 2 == 1 ? stake * 2 : 0,
        "bajo" => ball <= 18 ? stake * 2 : 0,
        "alto" => ball >= 19 ? stake * 2 : 0,
        _ => 0,
    };

    /// <summary>Dados (línea de pase): 7 u 11 gana, 2, 3 o 12 pierde; si no, hay punto y se tira hasta sacar el punto (gana) o 7 (pierde).</summary>
    public static (bool Win, List<(int, int)> Rolls) PassLine(Func<int> die)
    {
        var rolls = new List<(int, int)>();
        (int, int) Roll() { var r = (die(), die()); rolls.Add(r); return r; }
        var (a, b) = Roll();
        var first = a + b;
        if (first is 7 or 11) return (true, rolls);
        if (first is 2 or 3 or 12) return (false, rolls);
        while (true)
        {
            var (c, d) = Roll();
            if (c + d == first) return (true, rolls);
            if (c + d == 7) return (false, rolls);
        }
    }

    // ---------- Blackjack (crupier se planta en 17; blackjack paga 3 a 2) ----------

    public static int CardValue(int card) => card switch { 1 => 11, >= 10 => 10, _ => card }; // 1 = as, 11–13 figuras

    public static int HandValue(IEnumerable<int> cards)
    {
        var total = 0;
        var aces = 0;
        foreach (var c in cards)
        {
            total += CardValue(c);
            if (c == 1) aces++;
        }
        while (total > 21 && aces-- > 0)
            total -= 10;
        return total;
    }

    public static bool IsBlackjack(IReadOnlyList<int> cards) => cards.Count == 2 && HandValue(cards) == 21;

    public static string CardName(int card) => card switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => card.ToString() };
}

public sealed record RouletteRequest(Guid CharacterId, long StakeCents, string Bet, int Number, string RequestId);
public sealed record DiceRequest(Guid CharacterId, long StakeCents, string RequestId);
public sealed record BlackjackRequest(Guid CharacterId, string Action, long StakeCents, string RequestId);

/// <summary>Mano de blackjack (las cartas del crupier se ven todas recién cuando termina).</summary>
public sealed record BlackjackHand(Guid Id, long StakeCents, List<int> Player, List<int> Dealer, string Status, bool DealerHidden);

/// <summary>Resultado de una jugada: mensaje, detalle (bola, dados, cartas), lo que volvió y el saldo.</summary>
public sealed record CasinoResult(bool Ok, string Message, string Detail, long PaidCents, long BalanceCents, BlackjackHand? Hand = null, long MaxBetCents = CasinoRules.MaxBetCents);
