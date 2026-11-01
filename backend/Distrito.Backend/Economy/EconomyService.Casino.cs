using System.Security.Cryptography;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Casino «El Trébol» (aprobado por el equipo 2026-11-01). El azar lo tira el backend (generador criptográfico); la apuesta
/// va a la caja de Valenti y los premios salen de ahí, en la misma transacción del libro. Si la casa no puede cubrir el
/// premio máximo de una apuesta, no la toma.
/// </summary>
public sealed partial class EconomyService
{
    private static int Rand(int toExclusive) => RandomNumberGenerator.GetInt32(toExclusive);

    private async Task<(Guid Me, Guid House, long Max)> CasinoSetupAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, long stake, long maxPayout, CancellationToken ct)
    {
        var rank = await RankOfCharacterAsync(c, tx, characterId, ct);
        var max = PrestigeRules.CasinoVip(rank) ? CasinoRules.VipMaxBetCents : CasinoRules.MaxBetCents;
        if (stake < CasinoRules.MinBetCents || stake > max)
            throw new EconomyRejected("bet_limits", $"Las apuestas van de {EconomyCatalog.FormatMoney(CasinoRules.MinBetCents)} a {EconomyCatalog.FormatMoney(max)}"
                + (max < CasinoRules.VipMaxBetCents ? " (la mesa VIP es para cuadrillas de rango 6)." : "."));
        var me = await EnsureCharacterAccountAsync(c, tx, characterId, ct);
        var house = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
        await LockAccountsAsync(c, tx, ct, me, house);
        if (await BalanceAsync(c, tx, me, ct) < stake)
            throw new EconomyRejected("insufficient_funds", "No te alcanza para esa apuesta.");
        if (await BalanceAsync(c, tx, house, ct) < maxPayout)
            throw new EconomyRejected("house_limit", "La casa no cubre una apuesta tan grande ahora. Probá con menos.");
        return (me, house, max);
    }

    private async Task<CasinoResult> CasinoRunAsync(Guid characterId, string requestId, CancellationToken ct,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<(string Message, string Detail, long Paid, BlackjackHand? Hand, long Max)>> play)
    {
        string detail = "";
        long paid = 0, max = CasinoRules.MaxBetCents;
        BlackjackHand? hand = null;
        var result = await RunAsync(characterId, requestId, ct, async (c, tx) =>
        {
            var r = await play(c, tx);
            (detail, paid, hand, max) = (r.Detail, r.Paid, r.Hand, r.Max);
            return r.Message;
        });
        return new CasinoResult(result.Ok, result.Message, detail, paid, result.Snapshot.BalanceCents, hand, max);
    }

    public Task<CasinoResult> RouletteAsync(RouletteRequest r, CancellationToken ct) =>
        CasinoRunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (!CasinoRules.RouletteBets.Contains(r.Bet) || r.Bet == "numero" && r.Number is < 0 or > 36)
                throw new EconomyRejected("invalid_bet", "Esa apuesta no existe.");
            var (me, house, max) = await CasinoSetupAsync(c, tx, r.CharacterId, r.StakeCents, r.StakeCents * 36, ct);
            var ball = Rand(37);
            var payout = CasinoRules.RoulettePayout(r.Bet, r.Number, ball, r.StakeCents);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "casino", $"Ruleta: {r.Bet}{(r.Bet == "numero" ? $" {r.Number}" : "")}", ct);
            await TransferMoneyAsync(c, tx, txId, from: me, to: house, r.StakeCents, ct);
            if (payout > 0)
                await TransferMoneyAsync(c, tx, txId, from: house, to: me, payout, ct);
            var color = ball == 0 ? "verde" : CasinoRules.RedNumbers.Contains(ball) ? "rojo" : "negro";
            return (payout > 0 ? $"¡Salió el {ball} {color}! Ganaste {EconomyCatalog.FormatMoney(payout - r.StakeCents)}." : $"Salió el {ball} {color}. Perdiste {EconomyCatalog.FormatMoney(r.StakeCents)}.",
                $"{ball} {color}", payout, null, max);
        });

    public Task<CasinoResult> DiceAsync(DiceRequest r, CancellationToken ct) =>
        CasinoRunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var (me, house, max) = await CasinoSetupAsync(c, tx, r.CharacterId, r.StakeCents, r.StakeCents * 2, ct);
            var (win, rolls) = CasinoRules.PassLine(() => 1 + Rand(6));
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "casino", "Dados (línea de pase)", ct);
            await TransferMoneyAsync(c, tx, txId, from: me, to: house, r.StakeCents, ct);
            if (win)
                await TransferMoneyAsync(c, tx, txId, from: house, to: me, r.StakeCents * 2, ct);
            var detail = string.Join(" → ", rolls.Select(x => $"{x.Item1}+{x.Item2}"));
            return (win ? $"¡Ganaste {EconomyCatalog.FormatMoney(r.StakeCents)}! ({detail})" : $"Perdiste {EconomyCatalog.FormatMoney(r.StakeCents)}. ({detail})",
                detail, win ? r.StakeCents * 2 : 0, null, max);
        });

    private static List<int> NewDeck()
    {
        // Zapato de 4 mazos, mezclado con el generador criptográfico.
        var deck = Enumerable.Range(0, 4 * 52).Select(i => i % 13 + 1).ToList();
        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = Rand(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }

    public Task<CasinoResult> BlackjackAsync(BlackjackRequest r, CancellationToken ct) =>
        CasinoRunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            Guid id;
            long stake;
            List<int> deck, player, dealer;
            await using (var find = new NpgsqlCommand(
                "SELECT id, stake_cents, deck, player, dealer FROM distrito.casino_hands WHERE character_id = @c AND status = 'jugando' FOR UPDATE", c, tx))
            {
                find.Parameters.AddWithValue("c", r.CharacterId);
                await using var reader = await find.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    (id, stake, deck, player, dealer) = (reader.GetGuid(0), reader.GetInt64(1), [.. reader.GetFieldValue<int[]>(2)],
                        [.. reader.GetFieldValue<int[]>(3)], [.. reader.GetFieldValue<int[]>(4)]);
                else
                    (id, stake, deck, player, dealer) = (Guid.Empty, 0, [], [], []);
            }
            var (me, house, max) = (Guid.Empty, Guid.Empty, CasinoRules.MaxBetCents);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "casino", $"Blackjack: {r.Action}", ct);
            int Draw() { var card = deck[^1]; deck.RemoveAt(deck.Count - 1); return card; }

            if (r.Action == "repartir")
            {
                if (id != Guid.Empty)
                    throw new EconomyRejected("hand_open", "Ya tenés una mano en juego: terminala primero.");
                (me, house, max) = await CasinoSetupAsync(c, tx, r.CharacterId, r.StakeCents, r.StakeCents * 5 / 2, ct);
                stake = r.StakeCents;
                deck = NewDeck();
                player = [Draw(), Draw()];
                dealer = [Draw(), Draw()];
                id = Guid.CreateVersion7();
                await TransferMoneyAsync(c, tx, txId, from: me, to: house, stake, ct);
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO distrito.casino_hands (id, character_id, stake_cents, deck, player, dealer, status) VALUES (@id, @c, @s, @deck, @p, @d, 'jugando')
                    """, c, tx);
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("c", r.CharacterId);
                insert.Parameters.AddWithValue("s", stake);
                insert.Parameters.AddWithValue("deck", deck.ToArray());
                insert.Parameters.AddWithValue("p", player.ToArray());
                insert.Parameters.AddWithValue("d", dealer.ToArray());
                await insert.ExecuteNonQueryAsync(ct);
                if (!CasinoRules.IsBlackjack(player) && !CasinoRules.IsBlackjack(dealer))
                    return ("Repartido. ¿Pedís o te plantás?", "", 0, new BlackjackHand(id, stake, player, dealer, "jugando", true), max);
            }
            else
            {
                if (id == Guid.Empty)
                    throw new EconomyRejected("no_hand", "No tenés una mano en juego.");
                me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
                house = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
                await LockAccountsAsync(c, tx, ct, me, house);
                if (r.Action == "pedir")
                {
                    player.Add(Draw());
                    if (CasinoRules.HandValue(player) < 21)
                    {
                        await SaveHandAsync(c, tx, id, deck, player, dealer, "jugando", ct);
                        return ($"Tenés {CasinoRules.HandValue(player)}. ¿Otra?", "", 0, new BlackjackHand(id, stake, player, dealer, "jugando", true), max);
                    }
                }
                else if (r.Action != "plantarse")
                    throw new EconomyRejected("invalid_action", "Acción inválida.");
            }

            // Resolver: el crupier saca hasta 17 (si el jugador no se pasó).
            var mine = CasinoRules.HandValue(player);
            if (mine <= 21 && !CasinoRules.IsBlackjack(player))
                while (CasinoRules.HandValue(dealer) < 17)
                    dealer.Add(Draw());
            var theirs = CasinoRules.HandValue(dealer);
            (string status, long payout, string text) = (CasinoRules.IsBlackjack(player), CasinoRules.IsBlackjack(dealer)) switch
            {
                (true, true) => ("empate", stake, "Blackjack los dos: empate, recuperás la apuesta."),
                (true, false) => ("blackjack", stake * 5 / 2, $"¡Blackjack! Ganaste {EconomyCatalog.FormatMoney(stake * 3 / 2)}."),
                (false, true) => ("perdida", 0, "El crupier tiene blackjack. Perdiste."),
                _ when mine > 21 => ("perdida", 0, $"Te pasaste ({mine}). Perdiste {EconomyCatalog.FormatMoney(stake)}."),
                _ when theirs > 21 || mine > theirs => ("ganada", stake * 2, $"¡Ganaste {EconomyCatalog.FormatMoney(stake)}! ({mine} contra {theirs})"),
                _ when mine == theirs => ("empate", stake, $"Empate ({mine}): recuperás la apuesta."),
                _ => ("perdida", 0, $"Perdiste ({mine} contra {theirs})."),
            };
            if (payout > 0)
                await TransferMoneyAsync(c, tx, txId, from: house, to: me, payout, ct);
            await SaveHandAsync(c, tx, id, deck, player, dealer, status, ct);
            return (text, $"{mine} contra {theirs}", payout, new BlackjackHand(id, stake, player, dealer, status, false), max);
        });

    private static async Task SaveHandAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid id, List<int> deck, List<int> player, List<int> dealer, string status, CancellationToken ct)
    {
        await using var save = new NpgsqlCommand("""
            UPDATE distrito.casino_hands SET deck = @deck, player = @p, dealer = @d, status = @s, closed_at = CASE WHEN @s = 'jugando' THEN NULL ELSE now() END WHERE id = @id
            """, c, tx);
        save.Parameters.AddWithValue("deck", deck.ToArray());
        save.Parameters.AddWithValue("p", player.ToArray());
        save.Parameters.AddWithValue("d", dealer.ToArray());
        save.Parameters.AddWithValue("s", status);
        save.Parameters.AddWithValue("id", id);
        await save.ExecuteNonQueryAsync(ct);
    }
}
