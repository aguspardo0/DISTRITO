using Distrito.Shared.Economy;
using Distrito.Shared.Progression;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Operaciones económicas. Cada una es una transacción de PostgreSQL que:
///   - bloquea las cuentas involucradas en orden fijo (evita interbloqueos),
///   - valida saldo, stock y estado del contrato,
///   - registra dinero en el libro (asientos que suman cero) y mercancías en item_movements,
///   - es idempotente: el mismo RequestId no se aplica dos veces.
/// </summary>
public sealed partial class EconomyService(NpgsqlDataSource db, TimeProvider clock, IConfiguration config)
{
    private sealed class EconomyRejected(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    // ---------- Consultas ----------

    public async Task<EconomySnapshot> GetSnapshotAsync(Guid characterId, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await EnsureCharacterAccountAsync(connection, tx, characterId, ct);
        await ExpireContractsAsync(connection, tx, characterId, ct);
        var snapshot = await LoadSnapshotAsync(connection, tx, characterId, ct);
        await tx.CommitAsync(ct);
        return snapshot;
    }

    // ---------- Acciones ----------

    public Task<EconomyActionResult> AcceptContractAsync(AcceptContractRequest request, CancellationToken ct) =>
        RunAsync(request.CharacterId, request.RequestId, ct, async (c, tx) =>
        {
            if (!EconomyCatalog.Templates.TryGetValue(request.TemplateCode, out var template))
                throw new EconomyRejected("unknown_contract", "Ese contrato no existe.");

            if (template.RequiredReputation is { } required)
            {
                await using var rep = new NpgsqlCommand(
                    "SELECT COALESCE((SELECT value FROM distrito.character_reputation WHERE character_id = @c AND family_code = @f), 0)", c, tx);
                rep.Parameters.AddWithValue("c", request.CharacterId);
                rep.Parameters.AddWithValue("f", required.FamilyCode);
                if ((int)(await rep.ExecuteScalarAsync(ct))! < required.Delta)
                    throw new EconomyRejected("not_enough_reputation",
                        $"Necesitás {required.Delta} de reputación con los {EconomyCatalog.FamilyName(required.FamilyCode)}.");
            }
            if (template.RequiresCrewMate &&
                !await ExistsAsync(c, tx, "SELECT 1 FROM distrito.crew_members WHERE character_id = @k::uuid", request.CharacterId.ToString(), ct))
                throw new EconomyRejected("no_crew", "Este trabajo es para una cuadrilla: formá o unite a una primero.");

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.contracts (id, template_code, character_id, status, accept_key, expires_at)
                VALUES (@id, @t, @c, 'active', @k, @exp)
                ON CONFLICT DO NOTHING
                """, c, tx);
            cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
            cmd.Parameters.AddWithValue("t", template.Code);
            cmd.Parameters.AddWithValue("c", request.CharacterId);
            cmd.Parameters.AddWithValue("k", request.RequestId);
            var extraSeconds = template.HasTimeLimit
                ? ProgressionRules.ExtraDeadlineSeconds((await LoadProgressAsync(c, tx, request.CharacterId, ct)).LogisticsLevel)
                : 0;
            cmd.Parameters.AddWithValue("exp", template.HasTimeLimit
                ? clock.GetUtcNow().AddSeconds(template.TimeLimitSeconds + extraSeconds)
                : DBNull.Value);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            {
                // Si fue un reintento del mismo pedido, no es un error.
                if (await ExistsAsync(c, tx, "SELECT 1 FROM distrito.contracts WHERE accept_key = @k", request.RequestId, ct))
                    return "Contrato aceptado.";
                throw new EconomyRejected("contract_already_active", "Ya tenés ese contrato en curso.");
            }
            return template.HasTimeLimit
                ? $"Contrato aceptado: {template.Title}. Tenés {template.TimeLimitSeconds / 60} minutos" +
                  (extraSeconds > 0 ? $" y {extraSeconds} segundos (Logística)." : ".")
                : $"Contrato aceptado: {template.Title}.";
        });

    public Task<EconomyActionResult> BuyAsync(BuyItemRequest request, CancellationToken ct) =>
        RunAsync(request.CharacterId, request.RequestId, ct, async (c, tx) =>
        {
            if (request.Quantity is < 1 or > 100)
                throw new EconomyRejected("invalid_quantity", "Cantidad inválida.");

            var (itemName, unitPrice) = await LoadItemAsync(c, tx, request.ItemCode, ct);
            var buyer = await EnsureCharacterAccountAsync(c, tx, request.CharacterId, ct);
            var seller = await AccountIdByCodeAsync(c, tx, request.NpcCode, ct);
            var sellerLocation = request.NpcCode.Replace("npc:", "");

            await LockAccountsAsync(c, tx, ct, buyer, seller);
            var total = checked(unitPrice * request.Quantity);
            if (await BalanceAsync(c, tx, buyer, ct) < total)
                throw new EconomyRejected("insufficient_funds",
                    $"No te alcanza: cuesta {EconomyCatalog.FormatMoney(total)}.");

            var txId = await NewLedgerTransactionAsync(c, tx, request.RequestId, "purchase",
                $"Compra de {request.Quantity} × {itemName} a {request.NpcCode}", ct);
            await TransferMoneyAsync(c, tx, txId, from: buyer, to: seller, total, ct);
            await MoveItemsAsync(c, tx, txId, request.ItemCode, request.Quantity,
                from: (seller, sellerLocation), to: (buyer, EconomyCatalog.CarriedLocation),
                notEnough: "El depósito no tiene stock suficiente.", ct);

            return $"Compraste {request.Quantity} × {itemName} por {EconomyCatalog.FormatMoney(total)}.";
        });

    public Task<EconomyActionResult> DeliverAsync(DeliverContractRequest request, CancellationToken ct) =>
        RunAsync(request.CharacterId, request.RequestId, ct, async (c, tx) =>
        {
            string templateCode, status;
            await using (var find = new NpgsqlCommand("""
                SELECT template_code, status FROM distrito.contracts
                WHERE id = @id AND character_id = @c
                FOR UPDATE
                """, c, tx))
            {
                find.Parameters.AddWithValue("id", request.ContractId);
                find.Parameters.AddWithValue("c", request.CharacterId);
                await using var reader = await find.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new EconomyRejected("contract_not_active", "No tenés ese contrato en curso.");
                (templateCode, status) = (reader.GetString(0), reader.GetString(1));
            }
            if (status == "failed")
                throw new EconomyRejected("contract_expired", "Se te pasó el plazo: el contrato venció.");
            if (status != "active")
                throw new EconomyRejected("contract_not_active", "No tenés ese contrato en curso.");

            var template = EconomyCatalog.Templates[templateCode];

            // Participantes: quien entrega y, en trabajos de cuadrilla, los compañeros presentes de la misma cuadrilla.
            var participants = new List<Guid> { request.CharacterId };
            if (template.RequiresCrewMate)
            {
                participants.AddRange(await CrewMatesAmongAsync(c, tx, request.CharacterId, request.Companions ?? [], ct));
                if (participants.Count < 2)
                    throw new EconomyRejected("need_crew_mate", "Para este trabajo tiene que estar cerca alguien de tu cuadrilla.");
            }

            var accounts = new List<Guid>();
            foreach (var p in participants)
                accounts.Add(await EnsureCharacterAccountAsync(c, tx, p, ct));
            var payer = await AccountIdByCodeAsync(c, tx, template.Payer, ct);
            var receiver = template.HasGoods ? await AccountIdByCodeAsync(c, tx, template.DestinationNpc, ct) : payer;
            await LockAccountsAsync(c, tx, ct, [.. accounts, payer, receiver]);

            // Pago: partes iguales (el resto de la división queda para quien entrega) y cada uno con su bonificación de Negociación.
            var share = template.RewardCents / participants.Count;
            var amounts = new List<long>();
            for (var i = 0; i < participants.Count; i++)
            {
                var baseAmount = share + (i == 0 ? template.RewardCents - share * participants.Count : 0);
                var negotiation = (await LoadProgressAsync(c, tx, participants[i], ct)).NegotiationLevel;
                amounts.Add(ProgressionRules.WithNegotiationBonus(baseAmount, negotiation));
            }
            if (await BalanceAsync(c, tx, payer, ct) < amounts.Sum())
                throw new EconomyRejected("client_cannot_pay", "El cliente no tiene fondos para pagar.");

            var txId = await NewLedgerTransactionAsync(c, tx, request.RequestId, "contract_delivery",
                $"Entrega del contrato {templateCode} ({request.ContractId})", ct);
            if (template.HasGoods)
                await MoveItemsAsync(c, tx, txId, template.ItemCode, template.Quantity,
                    from: (accounts[0], EconomyCatalog.CarriedLocation), to: (receiver, template.DestinationLocation),
                    notEnough: "No tenés la mercadería que pide el contrato.", ct);

            for (var i = 0; i < accounts.Count; i++)
                await TransferMoneyAsync(c, tx, txId, from: payer, to: accounts[i], amounts[i], ct);

            await using (var complete = new NpgsqlCommand("""
                UPDATE distrito.contracts
                SET status = 'completed', completed_at = now(), completion_transaction_id = @tx
                WHERE id = @id
                """, c, tx))
            {
                complete.Parameters.AddWithValue("tx", txId);
                complete.Parameters.AddWithValue("id", request.ContractId);
                await complete.ExecuteNonQueryAsync(ct);
            }

            foreach (var p in participants)
                foreach (var reward in template.Reputation)
                    await GrantReputationAsync(c, tx, p, reward, $"contrato {templateCode}", txId, ct);

            // Progresión: experiencia y Negociación para todos; Logística para quien llevó la mercadería.
            for (var i = 0; i < participants.Count; i++)
                await GrantProgressAsync(c, tx, participants[i], ProgressionRules.XpPerContract,
                    logistics: i == 0 && template.HasGoods ? 1 : 0, negotiation: 1, administration: 0, ct);

            var reputationText = EconomyCatalog.DescribeReputation(template.Reputation);
            return participants.Count > 1
                ? $"Trabajo hecho entre {participants.Count}. Cobraste {EconomyCatalog.FormatMoney(amounts[0])} y ganaste reputación ({reputationText}) y {ProgressionRules.XpPerContract} de experiencia."
                : $"Contrato cumplido. Cobraste {EconomyCatalog.FormatMoney(amounts[0])} y ganaste reputación ({reputationText}) y {ProgressionRules.XpPerContract} de experiencia.";
        });

    private static async Task<ProgressView> LoadProgressAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT xp, logistics, negotiation, administration FROM distrito.character_progress WHERE character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new ProgressView(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3))
            : ProgressView.Empty;
    }

    /// <summary>Suma experiencia y práctica de oficios, dentro de la transacción que lo causa.</summary>
    private static async Task GrantProgressAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId,
        long xp, int logistics, int negotiation, int administration, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO distrito.character_progress (character_id, xp, logistics, negotiation, administration)
            VALUES (@c, @xp, @l, @n, @a)
            ON CONFLICT (character_id) DO UPDATE SET
                xp = distrito.character_progress.xp + @xp,
                logistics = distrito.character_progress.logistics + @l,
                negotiation = distrito.character_progress.negotiation + @n,
                administration = distrito.character_progress.administration + @a,
                updated_at = now()
            """, c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("xp", xp);
        cmd.Parameters.AddWithValue("l", logistics);
        cmd.Parameters.AddWithValue("n", negotiation);
        cmd.Parameters.AddWithValue("a", administration);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<Guid>> CrewMatesAmongAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId,
        IReadOnlyList<Guid> candidates, CancellationToken ct)
    {
        var mates = new List<Guid>();
        if (candidates.Count == 0)
            return mates;
        await using var cmd = new NpgsqlCommand("""
            SELECT m.character_id FROM distrito.crew_members m
            WHERE m.crew_id = (SELECT crew_id FROM distrito.crew_members WHERE character_id = @c)
              AND m.character_id = ANY(@ids) AND m.character_id <> @c
            ORDER BY m.character_id
            """, c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("ids", candidates.Distinct().ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            mates.Add(reader.GetGuid(0));
        return mates;
    }

    /// <summary>
    /// Vence los contratos con plazo cumplido: pasan a 'failed' y aplican la reputación de la falla.
    /// Con characterId solo los de ese personaje (antes de cada acción); sin él, todos (lo hace el ciclo económico).
    /// </summary>
    public async Task<int> ExpireContractsAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var expired = await ExpireContractsAsync(connection, tx, null, ct);
        await tx.CommitAsync(ct);
        return expired;
    }

    private async Task<int> ExpireContractsAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid? characterId, CancellationToken ct)
    {
        var failed = new List<(Guid Character, string Template)>();
        await using (var cmd = new NpgsqlCommand($"""
            UPDATE distrito.contracts SET status = 'failed', completed_at = @now
            WHERE status = 'active' AND expires_at IS NOT NULL AND expires_at <= @now
              {(characterId is null ? "" : "AND character_id = @c")}
            RETURNING character_id, template_code
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
            if (characterId is { } id)
                cmd.Parameters.AddWithValue("c", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                failed.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        foreach (var (character, templateCode) in failed)
            if (EconomyCatalog.Templates.TryGetValue(templateCode, out var template))
                foreach (var penalty in template.FailureReputation ?? [])
                    await GrantReputationAsync(c, tx, character, penalty, $"contrato {templateCode} vencido", null, ct);
        return failed.Count;
    }

    // ---------- Infraestructura de la transacción ----------

    private async Task<EconomyActionResult> RunAsync(
        Guid characterId, string requestId, CancellationToken ct,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<string>> action)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 100)
            return await FailAsync(characterId, "invalid_request", "Pedido inválido.", ct);

        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            await EnsureCharacterAccountAsync(connection, tx, characterId, ct);
            await ExpireContractsAsync(connection, tx, characterId, ct);

            // Idempotencia: si este pedido ya se aplicó, se devuelve el estado actual sin repetir nada.
            if (await ExistsAsync(connection, tx, "SELECT 1 FROM distrito.ledger_transactions WHERE idempotency_key = @k", requestId, ct))
            {
                var current = await LoadSnapshotAsync(connection, tx, characterId, ct);
                await tx.CommitAsync(ct);
                return new EconomyActionResult(true, null, "Ya estaba hecho.", current);
            }

            var message = await action(connection, tx);
            var snapshot = await LoadSnapshotAsync(connection, tx, characterId, ct);
            await tx.CommitAsync(ct);
            return new EconomyActionResult(true, null, message, snapshot);
        }
        catch (EconomyRejected rejected)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return await FailAsync(characterId, rejected.Code, rejected.Message, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure)
        {
            // Dos pedidos simultáneos con el mismo id: el segundo pierde y se informa el estado actual.
            await tx.RollbackAsync(CancellationToken.None);
            return await FailAsync(characterId, "conflict", "La operación chocó con otra simultánea. Probá de nuevo.", ct);
        }
    }

    private async Task<EconomyActionResult> FailAsync(Guid characterId, string code, string message, CancellationToken ct) =>
        new(false, code, message, await GetSnapshotAsync(characterId, ct));

    private static async Task<bool> ExistsAsync(NpgsqlConnection c, NpgsqlTransaction tx, string sql, string key, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, c, tx);
        cmd.Parameters.AddWithValue("k", key);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    /// <summary>Crea la cuenta del personaje la primera vez, con sus fondos iniciales emitidos por el sistema.</summary>
    private static async Task<Guid> EnsureCharacterAccountAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using (var find = new NpgsqlCommand(
            "SELECT id FROM distrito.economic_accounts WHERE character_id = @c", c, tx))
        {
            find.Parameters.AddWithValue("c", characterId);
            if (await find.ExecuteScalarAsync(ct) is Guid existing)
                return existing;
        }

        var accountId = Guid.CreateVersion7();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO distrito.economic_accounts (id, kind, character_id, display_name)
            SELECT @id, 'character', ch.id, ch.name FROM distrito.characters ch WHERE ch.id = @c
            ON CONFLICT (character_id) DO NOTHING
            """, c, tx))
        {
            insert.Parameters.AddWithValue("id", accountId);
            insert.Parameters.AddWithValue("c", characterId);
            if (await insert.ExecuteNonQueryAsync(ct) == 0)
            {
                // Otro pedido la creó al mismo tiempo, o el personaje no existe.
                await using var again = new NpgsqlCommand(
                    "SELECT id FROM distrito.economic_accounts WHERE character_id = @c", c, tx);
                again.Parameters.AddWithValue("c", characterId);
                return await again.ExecuteScalarAsync(ct) as Guid?
                    ?? throw new EconomyRejected("character_not_found", "El personaje no existe.");
            }
        }

        var mint = await AccountIdByCodeAsync(c, tx, "system:emision", ct);
        await LockAccountsAsync(c, tx, ct, mint, accountId);
        var txId = await NewLedgerTransactionAsync(c, tx, $"starting-funds:{characterId}", "starting_funds",
            "Fondos iniciales del personaje", ct);
        await TransferMoneyAsync(c, tx, txId, from: mint, to: accountId, EconomyCatalog.StartingFundsCents, ct);
        return accountId;
    }

    private static async Task<Guid> AccountIdByCodeAsync(NpgsqlConnection c, NpgsqlTransaction tx, string code, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT id FROM distrito.economic_accounts WHERE code = @code", c, tx);
        cmd.Parameters.AddWithValue("code", code);
        return await cmd.ExecuteScalarAsync(ct) as Guid?
            ?? throw new EconomyRejected("unknown_account", $"No existe la cuenta {code}.");
    }

    private static async Task<(string Name, long UnitPrice)> LoadItemAsync(NpgsqlConnection c, NpgsqlTransaction tx, string code, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT name, reference_price_cents FROM distrito.item_definitions WHERE code = @code", c, tx);
        cmd.Parameters.AddWithValue("code", code);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new EconomyRejected("unknown_item", "Esa mercadería no existe.");
        return (reader.GetString(0), reader.GetInt64(1));
    }

    /// <summary>Bloquea las filas de las cuentas siempre en el mismo orden (por id) para evitar interbloqueos.</summary>
    private static async Task LockAccountsAsync(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken ct, params Guid[] accounts)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM distrito.economic_accounts WHERE id = ANY(@ids) ORDER BY id FOR UPDATE", c, tx);
        cmd.Parameters.AddWithValue("ids", accounts);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { }
    }

    private static async Task<long> BalanceAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid account, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT balance_cents FROM distrito.economic_accounts WHERE id = @id", c, tx);
        cmd.Parameters.AddWithValue("id", account);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task<Guid> NewLedgerTransactionAsync(
        NpgsqlConnection c, NpgsqlTransaction tx, string key, string kind, string description, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO distrito.ledger_transactions (id, idempotency_key, kind, description) VALUES (@id, @k, @kind, @d)", c, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("k", key);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("d", description);
        await cmd.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task TransferMoneyAsync(
        NpgsqlConnection c, NpgsqlTransaction tx, Guid txId, Guid from, Guid to, long amount, CancellationToken ct)
    {
        if (amount <= 0 || from == to)
            throw new InvalidOperationException("Transferencia inválida.");

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO distrito.ledger_entries (transaction_id, account_id, amount_cents) VALUES (@tx, @from, -@amount), (@tx, @to, @amount);
            UPDATE distrito.economic_accounts SET balance_cents = balance_cents - @amount WHERE id = @from;
            UPDATE distrito.economic_accounts SET balance_cents = balance_cents + @amount WHERE id = @to;
            """, c, tx);
        cmd.Parameters.AddWithValue("tx", txId);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.AddWithValue("amount", amount);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            throw new EconomyRejected("insufficient_funds", "Saldo insuficiente.");
        }
    }

    private static async Task MoveItemsAsync(
        NpgsqlConnection c, NpgsqlTransaction tx, Guid txId, string itemCode, int quantity,
        (Guid Account, string Location) from, (Guid Account, string Location) to, string notEnough, CancellationToken ct)
    {
        await using (var take = new NpgsqlCommand("""
            WITH src AS (
                SELECT id, quantity FROM distrito.item_stacks
                WHERE owner_account_id = @fa AND item_code = @item AND location = @fl
                FOR UPDATE
            )
            SELECT id, quantity FROM src
            """, c, tx))
        {
            take.Parameters.AddWithValue("fa", from.Account);
            take.Parameters.AddWithValue("item", itemCode);
            take.Parameters.AddWithValue("fl", from.Location);
            Guid stackId;
            int available;
            await using (var reader = await take.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new EconomyRejected("not_enough_items", notEnough);
                stackId = reader.GetGuid(0);
                available = reader.GetInt32(1);
            }
            if (available < quantity)
                throw new EconomyRejected("not_enough_items", notEnough);

            await using var update = new NpgsqlCommand(available == quantity
                ? "DELETE FROM distrito.item_stacks WHERE id = @id"
                : "UPDATE distrito.item_stacks SET quantity = quantity - @q WHERE id = @id", c, tx);
            update.Parameters.AddWithValue("id", stackId);
            update.Parameters.AddWithValue("q", quantity);
            await update.ExecuteNonQueryAsync(ct);
        }

        await using var give = new NpgsqlCommand("""
            INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity)
            VALUES (@id, @ta, @item, @tl, @q)
            ON CONFLICT (owner_account_id, item_code, location) DO UPDATE SET quantity = distrito.item_stacks.quantity + EXCLUDED.quantity;
            INSERT INTO distrito.item_movements (transaction_id, item_code, from_account_id, from_location, to_account_id, to_location, quantity)
            VALUES (@tx, @item, @fa, @fl, @ta, @tl, @q);
            """, c, tx);
        give.Parameters.AddWithValue("id", Guid.CreateVersion7());
        give.Parameters.AddWithValue("ta", to.Account);
        give.Parameters.AddWithValue("tl", to.Location);
        give.Parameters.AddWithValue("fa", from.Account);
        give.Parameters.AddWithValue("fl", from.Location);
        give.Parameters.AddWithValue("item", itemCode);
        give.Parameters.AddWithValue("q", quantity);
        give.Parameters.AddWithValue("tx", txId);
        await give.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Suma reputación (tope 1000) y registra el evento. Dentro de la misma transacción que lo causa.</summary>
    private static async Task GrantReputationAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId,
        ReputationReward reward, string reason, Guid? transactionId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO distrito.character_reputation (character_id, family_code, value)
            VALUES (@c, @f, LEAST(@max, GREATEST(0, @d)))
            ON CONFLICT (character_id, family_code)
            DO UPDATE SET value = LEAST(@max, GREATEST(0, distrito.character_reputation.value + @d));
            INSERT INTO distrito.reputation_events (character_id, family_code, delta, reason, transaction_id)
            VALUES (@c, @f, @d, @reason, @tx);
            """, c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("f", reward.FamilyCode);
        cmd.Parameters.AddWithValue("d", reward.Delta);
        cmd.Parameters.AddWithValue("max", EconomyCatalog.MaxReputation);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("tx", (object?)transactionId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<EconomySnapshot> LoadSnapshotAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        long balance = 0;
        var inventory = new List<InventoryItem>();
        var active = new List<ContractView>();
        var completed = 0;
        var reputation = new List<ReputationView>();
        BusinessView? business = null;
        var businesses = new List<BusinessView>();

        await using (var cmd = new NpgsqlCommand("""
            SELECT a.balance_cents FROM distrito.economic_accounts a WHERE a.character_id = @c;
            SELECT s.item_code, d.name, s.quantity
              FROM distrito.item_stacks s
              JOIN distrito.economic_accounts a ON a.id = s.owner_account_id
              JOIN distrito.item_definitions d ON d.code = s.item_code
             WHERE a.character_id = @c AND s.location = @carried
             ORDER BY d.name;
            SELECT id, template_code, status, expires_at FROM distrito.contracts WHERE character_id = @c AND status = 'active' ORDER BY accepted_at;
            SELECT count(*) FROM distrito.contracts WHERE character_id = @c AND status = 'completed';
            SELECT f.code, f.name, COALESCE(r.value, 0)
              FROM distrito.families f
              LEFT JOIN distrito.character_reputation r ON r.family_code = f.code AND r.character_id = @c
             ORDER BY f.code;
            SELECT b.id, b.name,
                   COALESCE((SELECT sum(quantity) FROM distrito.item_stacks s WHERE s.owner_account_id = b.account_id AND s.location = b.location), 0)::int,
                   a.balance_cents,
                   COALESCE((SELECT sum(revenue_cents) FROM distrito.business_cycles bc WHERE bc.business_id = b.id), 0)::bigint,
                   COALESCE((SELECT sum(rent_cents) FROM distrito.business_cycles bc WHERE bc.business_id = b.id), 0)::bigint,
                   COALESCE((SELECT sum(units_sold) FROM distrito.business_cycles bc WHERE bc.business_id = b.id), 0)::int,
                   COALESCE((SELECT units_sold FROM distrito.business_cycles bc WHERE bc.business_id = b.id ORDER BY cycle DESC LIMIT 1), 0),
                   b.has_helper,
                   COALESCE((SELECT sum(wages_cents) FROM distrito.business_cycles bc WHERE bc.business_id = b.id), 0)::bigint,
                   b.kind
              FROM distrito.businesses b JOIN distrito.economic_accounts a ON a.id = b.account_id
             WHERE b.owner_character_id = @c ORDER BY b.created_at;
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            cmd.Parameters.AddWithValue("carried", EconomyCatalog.CarriedLocation);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) balance = reader.GetInt64(0);
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct)) inventory.Add(new InventoryItem(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
                active.Add(new ContractView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
            await reader.NextResultAsync(ct);
            if (await reader.ReadAsync(ct)) completed = (int)reader.GetInt64(0);
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct)) reputation.Add(new ReputationView(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
                businesses.Add(new BusinessView(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3),
                    reader.GetInt64(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetBoolean(8), reader.GetInt64(9),
                    reader.GetString(10)));
            business = businesses.FirstOrDefault(b => b.Kind == EconomyCatalog.CoffeeStandLocation);
        }

        var progress = await LoadProgressAsync(c, tx, characterId, ct);
        return new EconomySnapshot(balance, inventory, active, completed, reputation, business, progress, businesses);
    }
}
