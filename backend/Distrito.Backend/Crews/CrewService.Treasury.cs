using System.Globalization;
using Distrito.Backend.Economy;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Crews;

/// <summary>
/// Caja común de la cuadrilla (propuesta aprobada por el equipo, 2026-10-31). Es una cuenta del libro contable
/// (kind 'crew'): depósitos, retiros y aportes son transacciones de doble entrada e idempotentes por RequestId.
/// </summary>
public sealed partial class CrewService
{
    public Task<CrewActionResult> DepositAsync(CrewMoneyRequest r, CancellationToken ct) => MoveMoneyAsync(r, deposit: true, ct);
    public Task<CrewActionResult> WithdrawAsync(CrewMoneyRequest r, CancellationToken ct) => MoveMoneyAsync(r, deposit: false, ct);

    private Task<CrewActionResult> MoveMoneyAsync(CrewMoneyRequest r, bool deposit, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            if (string.IsNullOrWhiteSpace(r.RequestId) || r.RequestId.Length > 100 || r.AmountCents < 0)
                throw new CrewRejected("invalid_request", "Pedido inválido.");
            if (await EconomyService.ExistsAsync(c, tx, "SELECT 1 FROM distrito.ledger_transactions WHERE idempotency_key = @k", r.RequestId, ct))
                return "Ya estaba hecho.";

            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);
            if (!deposit && !(await PermissionsAsync(c, tx, crew.Id, ct)).CanWithdraw(crew.Role))
                throw new CrewRejected("not_allowed", "Tu rol no puede sacar plata de la caja.");

            var member = await EconomyService.EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var box = await EnsureCrewAccountAsync(c, tx, crew.Id, ct);
            await EconomyService.LockAccountsAsync(c, tx, ct, member, box);

            var from = deposit ? member : box;
            var available = await EconomyService.BalanceAsync(c, tx, from, ct);
            var amount = r.AmountCents == 0 ? available : r.AmountCents;
            if (amount <= 0)
                throw new CrewRejected("no_money", deposit ? "No tenés plata para poner." : "La caja está vacía.");
            if (amount > available)
                throw new CrewRejected("insufficient_funds", deposit ? "No te alcanza." : "La caja no tiene tanto.");

            var txId = await EconomyService.NewLedgerTransactionAsync(c, tx, r.RequestId, deposit ? "crew_deposit" : "crew_withdrawal",
                deposit ? "Depósito en la caja de la cuadrilla" : "Retiro de la caja de la cuadrilla", ct);
            await EconomyService.TransferMoneyAsync(c, tx, txId, from: from, to: deposit ? box : member, amount, ct);
            var text = EconomyCatalog.FormatMoney(amount);
            await LogAsync(c, tx, crew.Id, deposit ? "deposit" : "withdraw", r.CharacterId, text, ct);
            return deposit ? $"Pusiste {text} en la caja." : $"Sacaste {text} de la caja.";
        });

    public Task<CrewActionResult> SetSettingsAsync(CrewSettingsRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var p = r.Permissions;
            if (p.TithePercent is < 0 or > CrewPermissions.MaxTithePercent)
                throw new CrewRejected("invalid_settings", $"El aporte va de 0 a {CrewPermissions.MaxTithePercent} %.");
            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);
            await ApplyInactivitySuccessionAsync(c, tx, crew.Id, ct);
            crew = await CrewOfAsync(c, tx, r.CharacterId, ct) ?? crew;
            if (crew.Role != CrewRoles.Leader)
                throw new CrewRejected("not_leader", "Solo el jefe cambia los permisos.");

            await using var cmd = new NpgsqlCommand("""
                UPDATE distrito.crews SET second_can_withdraw = @sw, second_can_invite = @si, member_can_invite = @mi, contract_tithe_pct = @t
                WHERE id = @id
                """, c, tx);
            cmd.Parameters.AddWithValue("sw", p.SecondCanWithdraw);
            cmd.Parameters.AddWithValue("si", p.SecondCanInvite);
            cmd.Parameters.AddWithValue("mi", p.MemberCanInvite);
            cmd.Parameters.AddWithValue("t", p.TithePercent);
            cmd.Parameters.AddWithValue("id", crew.Id);
            await cmd.ExecuteNonQueryAsync(ct);
            await LogAsync(c, tx, crew.Id, "settings", r.CharacterId, Describe(p), ct);
            return "Permisos actualizados.";
        });

    private static string Describe(CrewPermissions p) =>
        $"segundo {(p.SecondCanWithdraw ? "saca" : "no saca")} de la caja, segundo {(p.SecondCanInvite ? "invita" : "no invita")}, " +
        $"miembros {(p.MemberCanInvite ? "invitan" : "no invitan")}, aporte {p.TithePercent.ToString(CultureInfo.InvariantCulture)} %";

    private static async Task<CrewPermissions> PermissionsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT second_can_withdraw, second_can_invite, member_can_invite, contract_tithe_pct FROM distrito.crews WHERE id = @id", c, tx);
        cmd.Parameters.AddWithValue("id", crewId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new CrewPermissions(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetInt32(3))
            : CrewPermissions.Default;
    }

    /// <summary>La cuenta de la caja se crea la primera vez que se usa.</summary>
    internal static async Task<Guid> EnsureCrewAccountAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        await using (var find = new NpgsqlCommand("SELECT account_id FROM distrito.crews WHERE id = @id", c, tx))
        {
            find.Parameters.AddWithValue("id", crewId);
            if (await find.ExecuteScalarAsync(ct) is Guid existing)
                return existing;
        }
        var account = Guid.CreateVersion7();
        await using var create = new NpgsqlCommand("""
            INSERT INTO distrito.economic_accounts (id, kind, display_name)
            SELECT @acc, 'crew', 'Caja de la cuadrilla ' || name FROM distrito.crews WHERE id = @id;
            UPDATE distrito.crews SET account_id = @acc WHERE id = @id;
            """, c, tx);
        create.Parameters.AddWithValue("acc", account);
        create.Parameters.AddWithValue("id", crewId);
        await create.ExecuteNonQueryAsync(ct);
        return account;
    }

    /// <summary>
    /// Aporte automático de un cobro de contrato: lo llama la economía dentro de la misma transacción del pago.
    /// Devuelve lo aportado (0 si no está en cuadrilla o el aporte es 0 %).
    /// </summary>
    internal static async Task<long> ApplyContractTitheAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid txId,
        Guid characterId, Guid characterAccount, long paidCents, CancellationToken ct)
    {
        Guid crewId;
        CrewPermissions permissions;
        await using (var cmd = new NpgsqlCommand("""
            SELECT cr.id, cr.second_can_withdraw, cr.second_can_invite, cr.member_can_invite, cr.contract_tithe_pct
              FROM distrito.crew_members m JOIN distrito.crews cr ON cr.id = m.crew_id WHERE m.character_id = @c
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return 0;
            crewId = reader.GetGuid(0);
            permissions = new CrewPermissions(reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetInt32(4));
        }
        var tithe = permissions.TitheOf(paidCents);
        if (tithe <= 0)
            return 0;
        var box = await EnsureCrewAccountAsync(c, tx, crewId, ct);
        await EconomyService.LockAccountsAsync(c, tx, ct, characterAccount, box);
        await EconomyService.TransferMoneyAsync(c, tx, txId, from: characterAccount, to: box, tithe, ct);
        await LogAsync(c, tx, crewId, "tithe", characterId, EconomyCatalog.FormatMoney(tithe), ct);
        return tithe;
    }
}
