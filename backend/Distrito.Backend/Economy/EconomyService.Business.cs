using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Negocios administrados por jugadores (primer tipo: puesto de café). Ver ADR-005.
/// Comprar la licencia, reponer y retirar son acciones del jugador; las ventas ocurren solas en cada
/// ciclo económico (<see cref="RunBusinessCyclesAsync"/>), aunque el dueño esté desconectado.
/// </summary>
public sealed partial class EconomyService
{
    public long CycleSeconds => Math.Max(1, config.GetValue("Economy:BusinessCycleSeconds", 300L));

    public long CurrentCycle() => clock.GetUtcNow().ToUnixTimeSeconds() / CycleSeconds;

    public Task<EconomyActionResult> BuyCoffeeStandLicenseAsync(BusinessActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (await BusinessOfAsync(c, tx, r.CharacterId, ct) is not null)
                throw new EconomyRejected("already_owner", "Ya tenés un puesto de café.");

            var buyer = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var valenti = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
            await LockAccountsAsync(c, tx, ct, buyer, valenti);
            if (await BalanceAsync(c, tx, buyer, ct) < EconomyCatalog.CoffeeStandLicenseCents)
                throw new EconomyRejected("insufficient_funds",
                    $"La licencia cuesta {EconomyCatalog.FormatMoney(EconomyCatalog.CoffeeStandLicenseCents)}.");

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "business_license", "Licencia de puesto de café", ct);
            await TransferMoneyAsync(c, tx, txId, from: buyer, to: valenti, EconomyCatalog.CoffeeStandLicenseCents, ct);

            var accountId = Guid.CreateVersion7();
            await using var create = new NpgsqlCommand("""
                INSERT INTO distrito.economic_accounts (id, kind, display_name)
                SELECT @acc, 'business', 'Puesto de café de ' || ch.name FROM distrito.characters ch WHERE ch.id = @c;
                INSERT INTO distrito.businesses (id, kind, name, owner_character_id, account_id, location, last_cycle)
                SELECT @id, 'puesto-cafe', 'Puesto de café de ' || ch.name, ch.id, @acc, @loc, @cycle FROM distrito.characters ch WHERE ch.id = @c;
                """, c, tx);
            create.Parameters.AddWithValue("acc", accountId);
            create.Parameters.AddWithValue("id", Guid.CreateVersion7());
            create.Parameters.AddWithValue("c", r.CharacterId);
            create.Parameters.AddWithValue("loc", EconomyCatalog.CoffeeStandLocation);
            create.Parameters.AddWithValue("cycle", CurrentCycle());
            await create.ExecuteNonQueryAsync(ct);

            return "¡Ya tenés tu puesto de café! Llevale bolsas de café para que empiece a vender.";
        });

    public Task<EconomyActionResult> RestockCoffeeStandAsync(BusinessActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var business = await BusinessOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new EconomyRejected("no_business", "No tenés un puesto de café.");
            var owner = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            await LockAccountsAsync(c, tx, ct, owner, business.AccountId);

            var carried = await QuantityAsync(c, tx, owner, EconomyCatalog.CoffeeBagItem, EconomyCatalog.CarriedLocation, ct);
            if (carried == 0)
                throw new EconomyRejected("not_enough_items", "No llevás bolsas de café.");

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "business_restock", $"Reposición de {carried} bolsas de café", ct);
            await MoveItemsAsync(c, tx, txId, EconomyCatalog.CoffeeBagItem, carried,
                from: (owner, EconomyCatalog.CarriedLocation), to: (business.AccountId, EconomyCatalog.CoffeeStandLocation),
                notEnough: "No llevás bolsas de café.", ct);
            return $"Repusiste {carried} bolsa(s) de café.";
        });

    public Task<EconomyActionResult> WithdrawCoffeeStandAsync(BusinessActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var business = await BusinessOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new EconomyRejected("no_business", "No tenés un puesto de café.");
            var owner = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            await LockAccountsAsync(c, tx, ct, owner, business.AccountId);

            var cash = await BalanceAsync(c, tx, business.AccountId, ct);
            if (cash <= 0)
                throw new EconomyRejected("no_cash", "La caja del puesto está vacía.");

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "business_withdrawal", "Retiro de ganancias del puesto de café", ct);
            await TransferMoneyAsync(c, tx, txId, from: business.AccountId, to: owner, cash, ct);
            return $"Retiraste {EconomyCatalog.FormatMoney(cash)} de la caja del puesto.";
        });

    /// <summary>
    /// Procesa los ciclos pendientes de todos los negocios hasta <paramref name="currentCycle"/>.
    /// Cada ciclo de cada negocio es idempotente (clave business-cycle:id:ciclo), así que puede correr
    /// en varios procesos o repetirse sin duplicar ventas. Devuelve cuántas unidades se vendieron en total.
    /// </summary>
    public async Task<int> RunBusinessCyclesAsync(long currentCycle, CancellationToken ct)
    {
        var pending = new List<Guid>();
        await using (var cmd = db.CreateCommand("SELECT id FROM distrito.businesses WHERE last_cycle < @cycle"))
        {
            cmd.Parameters.AddWithValue("cycle", currentCycle);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) pending.Add(reader.GetGuid(0));
        }

        var sold = 0;
        foreach (var businessId in pending)
            sold += await RunCyclesForBusinessAsync(businessId, currentCycle, ct);
        return sold;
    }

    private async Task<int> RunCyclesForBusinessAsync(Guid businessId, long currentCycle, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        Guid account;
        long lastCycle;
        await using (var lockBusiness = new NpgsqlCommand(
            "SELECT account_id, last_cycle FROM distrito.businesses WHERE id = @id FOR UPDATE", c, tx))
        {
            lockBusiness.Parameters.AddWithValue("id", businessId);
            await using var reader = await lockBusiness.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return 0;
            account = reader.GetGuid(0);
            lastCycle = reader.GetInt64(1);
        }
        if (lastCycle >= currentCycle)
            return 0;

        var population = await AccountIdByCodeAsync(c, tx, EconomyCatalog.PopulationAccount, ct);
        var valenti = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
        await LockAccountsAsync(c, tx, ct, account, population, valenti);

        // Si el servidor estuvo apagado mucho tiempo, no se simulan más de MaxCatchUpCycles ciclos de golpe.
        var first = Math.Max(lastCycle + 1, currentCycle - EconomyCatalog.MaxCatchUpCycles + 1);
        var totalSold = 0;
        for (var cycle = first; cycle <= currentCycle; cycle++)
        {
            var stock = await QuantityAsync(c, tx, account, EconomyCatalog.CoffeeBagItem, EconomyCatalog.CoffeeStandLocation, ct);
            if (stock == 0)
                break; // sin mercadería no hay ventas ni alquiler; se salta al final

            var units = Math.Min(stock, EconomyCatalog.CoffeeDemandPerCycle);
            var revenue = units * EconomyCatalog.CoffeeSalePriceCents;
            var rent = Math.Min(EconomyCatalog.CoffeeStandRentCents, revenue);

            var txId = await NewLedgerTransactionAsync(c, tx, $"business-cycle:{businessId}:{cycle}", "business_cycle",
                $"Ventas del ciclo {cycle}: {units} café(s)", ct);
            await TransferMoneyAsync(c, tx, txId, from: population, to: account, revenue, ct);
            await TransferMoneyAsync(c, tx, txId, from: account, to: valenti, rent, ct);
            await MoveItemsAsync(c, tx, txId, EconomyCatalog.CoffeeBagItem, units,
                from: (account, EconomyCatalog.CoffeeStandLocation), to: (population, "consumido"),
                notEnough: "Sin stock.", ct);

            await using var record = new NpgsqlCommand("""
                INSERT INTO distrito.business_cycles (business_id, cycle, units_sold, revenue_cents, rent_cents, transaction_id)
                VALUES (@b, @cycle, @u, @rev, @rent, @tx)
                """, c, tx);
            record.Parameters.AddWithValue("b", businessId);
            record.Parameters.AddWithValue("cycle", cycle);
            record.Parameters.AddWithValue("u", units);
            record.Parameters.AddWithValue("rev", revenue);
            record.Parameters.AddWithValue("rent", rent);
            record.Parameters.AddWithValue("tx", txId);
            await record.ExecuteNonQueryAsync(ct);
            totalSold += units;
        }

        await using (var advance = new NpgsqlCommand("UPDATE distrito.businesses SET last_cycle = @cycle WHERE id = @id", c, tx))
        {
            advance.Parameters.AddWithValue("cycle", currentCycle);
            advance.Parameters.AddWithValue("id", businessId);
            await advance.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return totalSold;
    }

    private sealed record OwnedBusiness(Guid Id, Guid AccountId);

    private static async Task<OwnedBusiness?> BusinessOfAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id, account_id FROM distrito.businesses WHERE owner_character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new OwnedBusiness(reader.GetGuid(0), reader.GetGuid(1)) : null;
    }

    private static async Task<int> QuantityAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid account, string item, string location, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COALESCE(sum(quantity), 0)::int FROM distrito.item_stacks WHERE owner_account_id = @a AND item_code = @i AND location = @l", c, tx);
        cmd.Parameters.AddWithValue("a", account);
        cmd.Parameters.AddWithValue("i", item);
        cmd.Parameters.AddWithValue("l", location);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }
}

/// <summary>Corre los ciclos económicos de los negocios en segundo plano: la economía sigue sin jugadores conectados.</summary>
public sealed class BusinessCycleWorker(EconomyService economy, IConfiguration config, ILogger<BusinessCycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Economy:BusinessWorkerEnabled", true))
            return;

        var interval = TimeSpan.FromSeconds(Math.Clamp(economy.CycleSeconds / 2.0, 1, 30));
        logger.LogInformation("Ciclo económico de negocios: {Seconds} s", economy.CycleSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var sold = await economy.RunBusinessCyclesAsync(economy.CurrentCycle(), stoppingToken);
                if (sold > 0)
                    logger.LogInformation("Ciclo económico: {Sold} unidades vendidas", sold);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falló el ciclo económico de negocios");
            }
        }
    }
}
