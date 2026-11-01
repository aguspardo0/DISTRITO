using Distrito.Shared.Economy;
using Distrito.Shared.Progression;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Negocios administrados por jugadores: puesto de café y club de jazz (ver ADR-005 y EconomyCatalog.BusinessTypes).
/// Comprar la licencia, reponer y retirar son acciones del jugador; las ventas ocurren solas en cada
/// ciclo económico (<see cref="RunBusinessCyclesAsync"/>), aunque el dueño esté desconectado.
/// </summary>
public sealed partial class EconomyService
{
    public long CycleSeconds => Math.Max(1, config.GetValue("Economy:BusinessCycleSeconds", 300L));

    public long CurrentCycle() => clock.GetUtcNow().ToUnixTimeSeconds() / CycleSeconds;

    private static BusinessType TypeOf(BusinessActionRequest r) =>
        EconomyCatalog.BusinessTypes.TryGetValue(r.Kind, out var type)
            ? type
            : throw new EconomyRejected("unknown_business", "Ese negocio no existe.");

    public Task<EconomyActionResult> BuyLicenseAsync(BusinessActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var type = TypeOf(r);
            if (await BusinessOfAsync(c, tx, r.CharacterId, type.Code, ct) is not null)
                throw new EconomyRejected("already_owner", $"Ya tenés un {type.Name.ToLowerInvariant()}.");

            var buyer = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var valenti = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct);
            await LockAccountsAsync(c, tx, ct, buyer, valenti);
            // Los Valenti en rivalidad cobran la licencia +10 %.
            var price = FamilyRules.SellPrice(type.LicenseCents, await MoodOfNpcAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct));
            if (await BalanceAsync(c, tx, buyer, ct) < price)
                throw new EconomyRejected("insufficient_funds",
                    $"La licencia cuesta {EconomyCatalog.FormatMoney(price)}.");

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "business_license", $"Licencia de {type.Name.ToLowerInvariant()}", ct);
            await TransferMoneyAsync(c, tx, txId, from: buyer, to: valenti, price, ct);

            var accountId = Guid.CreateVersion7();
            await using var create = new NpgsqlCommand("""
                INSERT INTO distrito.economic_accounts (id, kind, display_name)
                SELECT @acc, 'business', @name || ' de ' || ch.name FROM distrito.characters ch WHERE ch.id = @c;
                INSERT INTO distrito.businesses (id, kind, name, owner_character_id, account_id, location, last_cycle)
                SELECT @id, @kind, @name || ' de ' || ch.name, ch.id, @acc, @kind, @cycle FROM distrito.characters ch WHERE ch.id = @c;
                """, c, tx);
            create.Parameters.AddWithValue("acc", accountId);
            create.Parameters.AddWithValue("id", Guid.CreateVersion7());
            create.Parameters.AddWithValue("c", r.CharacterId);
            create.Parameters.AddWithValue("kind", type.Code);
            create.Parameters.AddWithValue("name", type.Name);
            create.Parameters.AddWithValue("cycle", CurrentCycle());
            await create.ExecuteNonQueryAsync(ct);

            return $"¡Ya tenés tu {type.Name.ToLowerInvariant()}! Llevale {type.ItemName}s para que empiece a vender.";
        });

    public Task<EconomyActionResult> RestockAsync(BusinessActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var type = TypeOf(r);
            var business = await BusinessOfAsync(c, tx, r.CharacterId, type.Code, ct)
                ?? throw new EconomyRejected("no_business", $"No tenés un {type.Name.ToLowerInvariant()}.");
            var owner = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            await LockAccountsAsync(c, tx, ct, owner, business.AccountId);

            var carried = await QuantityAsync(c, tx, owner, type.ItemCode, EconomyCatalog.CarriedLocation, ct);
            if (carried == 0)
                throw new EconomyRejected("not_enough_items", $"No llevás {type.ItemName}s.");

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "business_restock", $"Reposición de {carried} × {type.ItemName}", ct);
            await MoveItemsAsync(c, tx, txId, type.ItemCode, carried,
                from: (owner, EconomyCatalog.CarriedLocation), to: (business.AccountId, business.Location),
                notEnough: $"No llevás {type.ItemName}s.", ct);
            return $"Repusiste {carried} × {type.ItemName}.";
        });

    public Task<EconomyActionResult> WithdrawAsync(BusinessActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var type = TypeOf(r);
            var business = await BusinessOfAsync(c, tx, r.CharacterId, type.Code, ct)
                ?? throw new EconomyRejected("no_business", $"No tenés un {type.Name.ToLowerInvariant()}.");
            var owner = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            await LockAccountsAsync(c, tx, ct, owner, business.AccountId);

            var cash = await BalanceAsync(c, tx, business.AccountId, ct);
            if (cash <= 0)
                throw new EconomyRejected("no_cash", "La caja del negocio está vacía.");

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "business_withdrawal", $"Retiro de ganancias: {type.Name.ToLowerInvariant()}", ct);
            await TransferMoneyAsync(c, tx, txId, from: business.AccountId, to: owner, cash, ct);
            return $"Retiraste {EconomyCatalog.FormatMoney(cash)} de la caja.";
        });

    /// <summary>Empleado opcional (ayudante del café, músico del club).</summary>
    public Task<EconomyActionResult> SetEmployeeAsync(BusinessActionRequest r, bool hire, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var type = TypeOf(r);
            var business = await BusinessOfAsync(c, tx, r.CharacterId, type.Code, ct)
                ?? throw new EconomyRejected("no_business", $"No tenés un {type.Name.ToLowerInvariant()}.");
            await using var cmd = new NpgsqlCommand(
                "UPDATE distrito.businesses SET has_helper = @h WHERE id = @id AND has_helper <> @h", c, tx);
            cmd.Parameters.AddWithValue("h", hire);
            cmd.Parameters.AddWithValue("id", business.Id);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
                throw new EconomyRejected(hire ? "already_hired" : "no_helper", hire ? $"Ya tenés un {type.EmployeeName}." : $"No tenés {type.EmployeeName}.");

            // La acción queda en el libro (sin movimiento de dinero) para que el reintento sea idempotente.
            await NewLedgerTransactionAsync(c, tx, r.RequestId, hire ? "business_hire" : "business_fire",
                $"{(hire ? "Contratación" : "Despido")} de {type.EmployeeName}: {type.Name.ToLowerInvariant()}", ct);
            return hire
                ? $"Contrataste un {type.EmployeeName}: cobra {EconomyCatalog.FormatMoney(type.EmployeeWageCents)} por ciclo abierto y vende {type.EmployeeExtraDemand} más por ciclo."
                : $"Despediste al {type.EmployeeName}.";
        });

    // Nombres anteriores (puesto de café), usados por pruebas y endpoints existentes.
    public Task<EconomyActionResult> BuyCoffeeStandLicenseAsync(BusinessActionRequest r, CancellationToken ct) => BuyLicenseAsync(r, ct);
    public Task<EconomyActionResult> RestockCoffeeStandAsync(BusinessActionRequest r, CancellationToken ct) => RestockAsync(r, ct);
    public Task<EconomyActionResult> WithdrawCoffeeStandAsync(BusinessActionRequest r, CancellationToken ct) => WithdrawAsync(r, ct);
    public Task<EconomyActionResult> SetCoffeeHelperAsync(BusinessActionRequest r, bool hire, CancellationToken ct) => SetEmployeeAsync(r, hire, ct);

    /// <summary>
    /// Procesa los ciclos pendientes de todos los negocios hasta <paramref name="currentCycle"/>.
    /// Cada ciclo de cada negocio es idempotente (clave business-cycle:id:ciclo), así que puede correr
    /// en varios procesos o repetirse sin duplicar ventas. Devuelve cuántas unidades se vendieron en total.
    /// </summary>
    public async Task<int> RunBusinessCyclesAsync(long currentCycle, CancellationToken ct, Guid? onlyBusinessOf = null)
    {
        var pending = new List<Guid>();
        await using (var cmd = db.CreateCommand(
            "SELECT id FROM distrito.businesses WHERE last_cycle < @cycle AND (@owner::uuid IS NULL OR owner_character_id = @owner)"))
        {
            cmd.Parameters.AddWithValue("cycle", currentCycle);
            cmd.Parameters.AddWithValue("owner", (object?)onlyBusinessOf ?? DBNull.Value);
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
        bool hasHelper;
        Guid owner;
        BusinessType type;
        string location;
        await using (var lockBusiness = new NpgsqlCommand(
            "SELECT account_id, last_cycle, has_helper, owner_character_id, kind, location FROM distrito.businesses WHERE id = @id FOR UPDATE", c, tx))
        {
            lockBusiness.Parameters.AddWithValue("id", businessId);
            await using var reader = await lockBusiness.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return 0;
            account = reader.GetGuid(0);
            lastCycle = reader.GetInt64(1);
            hasHelper = reader.GetBoolean(2);
            owner = reader.GetGuid(3);
            type = EconomyCatalog.BusinessTypes[reader.GetString(4)];
            location = reader.GetString(5);
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
            var stock = await QuantityAsync(c, tx, account, type.ItemCode, location, ct);
            if (stock == 0)
                break; // sin mercadería no hay ventas ni alquiler; se salta al final

            // El empleado trabaja solo si hay caja para pagarle al empezar el ciclo; si no, ese ciclo no trabaja.
            var helperWorks = hasHelper && await BalanceAsync(c, tx, account, ct) >= type.EmployeeWageCents;
            // Administración del dueño (nivel 5: +1, nivel 10: +2), según su práctica al empezar el ciclo.
            var administration = (await LoadProgressAsync(c, tx, owner, ct)).AdministrationLevel;
            // De noche (hora del ciclo) los negocios nocturnos venden uno más.
            var night = type.NightBonus && EconomyCatalog.IsNight(DateTimeOffset.FromUnixTimeSeconds(cycle * CycleSeconds)) ? 1 : 0;
            var demand = type.BaseDemand + (helperWorks ? type.EmployeeExtraDemand : 0) + night
                         + ProgressionRules.ExtraSalesPerCycle(administration);
            var units = Math.Min(stock, demand);
            var revenue = units * type.SalePriceCents;
            var rent = Math.Min(type.RentCents, revenue);
            var wages = helperWorks ? type.EmployeeWageCents : 0;

            var txId = await NewLedgerTransactionAsync(c, tx, $"business-cycle:{businessId}:{cycle}", "business_cycle",
                $"Ventas del ciclo {cycle} ({type.Name.ToLowerInvariant()}): {units} × {type.ItemName}{(helperWorks ? $" con {type.EmployeeName}" : "")}", ct);
            if (wages > 0)
                await TransferMoneyAsync(c, tx, txId, from: account, to: population, wages, ct); // el sueldo vuelve a la población
            await TransferMoneyAsync(c, tx, txId, from: population, to: account, revenue, ct);
            await TransferMoneyAsync(c, tx, txId, from: account, to: valenti, rent, ct);
            await MoveItemsAsync(c, tx, txId, type.ItemCode, units,
                from: (account, location), to: (population, "consumido"),
                notEnough: "Sin stock.", ct);

            await using var record = new NpgsqlCommand("""
                INSERT INTO distrito.business_cycles (business_id, cycle, units_sold, revenue_cents, rent_cents, wages_cents, transaction_id)
                VALUES (@b, @cycle, @u, @rev, @rent, @wages, @tx)
                """, c, tx);
            record.Parameters.AddWithValue("b", businessId);
            record.Parameters.AddWithValue("cycle", cycle);
            record.Parameters.AddWithValue("u", units);
            record.Parameters.AddWithValue("rev", revenue);
            record.Parameters.AddWithValue("rent", rent);
            record.Parameters.AddWithValue("wages", wages);
            record.Parameters.AddWithValue("tx", txId);
            await record.ExecuteNonQueryAsync(ct);
            await GrantProgressAsync(c, tx, owner, ProgressionRules.XpPerSalesCycle, logistics: 0, negotiation: 0, administration: 1, ct);
            if (await CrewOfCharacterAsync(c, tx, owner, ct) is { } ownerCrew && TerritoryRules.ZoneOfNpc(type.SiteNpc) is { } businessZone)
                await GrantInfluenceAsync(c, tx, ownerCrew, businessZone, TerritoryRules.SalesCyclePoints, $"ventas de {type.Name.ToLowerInvariant()}", ct);
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

    private sealed record OwnedBusiness(Guid Id, Guid AccountId, string Location);

    private static async Task<OwnedBusiness?> BusinessOfAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, string kind, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id, account_id, location FROM distrito.businesses WHERE owner_character_id = @c AND kind = @k", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("k", kind);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new OwnedBusiness(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)) : null;
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
                await economy.GrantPactInfluenceAsync(stoppingToken);
                if (await economy.CloseSeasonsAsync(stoppingToken) > 0)
                    logger.LogInformation("Temporada de territorio cerrada");
                var expired = await economy.ExpireContractsAsync(stoppingToken);
                if (expired > 0)
                    logger.LogInformation("Contratos vencidos: {Expired}", expired);
                await economy.RunWarehouseRentAsync(economy.CurrentCycle(), stoppingToken);
                await economy.DeliverSupplierOrdersAsync(economy.CurrentCycle(), stoppingToken);
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
