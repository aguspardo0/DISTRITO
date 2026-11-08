using Distrito.Shared.Economy;

namespace Distrito.Backend.Economy;

/// <summary>
/// Turnos de empleado en la Esquina del café (aprobado por el equipo 2026-11-08): $3 por turno de 10 minutos y propinas
/// de hasta $1. Lo cobra el personaje al terminar; la plata sale de la clientela (<c>system:poblacion</c>), como las
/// ventas de los negocios. El servidor del juego controla el turno; acá se revisan los topes y que no se cobre dos veces.
/// </summary>
public sealed partial class EconomyService
{
    public Task<EconomyActionResult> CoffeeShiftPayAsync(CoffeeShiftPayRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (r.PayCents is < 0 or > CoffeeShiftRules.PayCents || r.TipCents is < 0 or > CoffeeShiftRules.MaxTipCents || r.Served < 0)
                throw new EconomyRejected("invalid_shift", "Ese turno no cierra.");
            if (r.PayCents + r.TipCents == 0)
                throw new EconomyRejected("no_pay", $"Para cobrar el turno hay que servir al menos {CoffeeShiftRules.MinServed} cafés.");
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var customers = await AccountIdByCodeAsync(c, tx, EconomyCatalog.PopulationAccount, ct);
            await LockAccountsAsync(c, tx, ct, me, customers);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "coffee_shift", $"Turno en la Esquina del café ({r.Served} cafés)", ct);
            await TransferMoneyAsync(c, tx, txId, from: customers, to: me, r.PayCents + r.TipCents, ct);
            return $"Cobraste {EconomyCatalog.FormatMoney(r.PayCents)} del turno" +
                   (r.TipCents > 0 ? $" y {EconomyCatalog.FormatMoney(r.TipCents)} de propina." : ".");
        });
}
