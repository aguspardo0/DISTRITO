using System.Text.Json;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Lo que el jugador ve del mercado (aprobado por el equipo, 2026-11-02): pizarras, libreta de precios, diario, informante y
/// libro de cuentas. Solo lee la economía real: no cambia precios ni reglas. El diario y el informante se pagan en el libro
/// (la plata va a sus cuentas).
/// </summary>
public sealed partial class EconomyService
{
    // ---------------- Pizarras ----------------

    public async Task<List<PriceBoardView>> MarketBoardsAsync(CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var boards = await BoardsAsync(c, tx, ct);
        var edition = await EnsureEditionAsync(c, tx, boards, ct);
        boards.Add(new PriceBoardView(MarketInfoRules.KioskNpc, MarketInfoRules.KioskName,
            [new PriceLine(MarketInfoRules.Newspaper, MarketInfoRules.PaperName, MarketInfoRules.NewspaperPriceCents, true)],
            clock.GetUtcNow(), $"«{edition.Headline}»"));
        await tx.CommitAsync(ct);
        return boards;
    }

    /// <summary>Las pizarras de los locales, con la flecha contra los precios de la edición anterior del diario.</summary>
    private async Task<List<PriceBoardView>> BoardsAsync(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var before = await PreviousPricesAsync(c, tx, MarketInfoRules.DayOf(now), ct);
        int TrendOf(string npc, string item, bool buying, long price) =>
            MarketInfoRules.Trend(price, before.TryGetValue(MarketInfoRules.Key(npc, item, buying), out var p) ? p : null);

        var boards = new List<PriceBoardView>();

        // Almacén Moretti: lo que vende en el mostrador y lo que compra (al 60 %).
        var deposit = EconomyCatalog.DepositNpc;
        var depositMood = await MoodOfNpcAsync(c, tx, deposit, ct);
        var depositAccount = await AccountIdByCodeAsync(c, tx, deposit, ct);
        var lines = new List<PriceLine>();
        foreach (var (item, basePrice) in new[] { (EconomyCatalog.CoffeeBagItem, EconomyCatalog.CoffeeBagPriceCents), (EconomyCatalog.DrinksCrateItem, EconomyCatalog.JazzClub.ItemPriceCents) })
        {
            var (name, _) = await LoadItemAsync(c, tx, item, ct);
            var price = FamilyRules.SellPrice(basePrice, depositMood);
            var stock = await QuantityAsync(c, tx, depositAccount, item, StockLocation(deposit), ct);
            lines.Add(new PriceLine(item, name, price, stock > 0, TrendOf(deposit, item, false, price)));
        }
        foreach (var item in NpcSaleRules.Buyers[deposit])
        {
            var (name, reference) = await LoadItemAsync(c, tx, item, ct);
            var price = NpcSaleRules.Price(reference, 1);
            lines.Add(new PriceLine(item, name, price, true, TrendOf(deposit, item, true, price), Buying: true));
        }
        boards.Add(new PriceBoardView(deposit, "Almacén Moretti", lines, now));

        // Taller Caruso: los vehículos a la vista. Los fierros van por la puerta de atrás (eso lo sabe el informante).
        var workshop = EconomyCatalog.WorkshopNpc;
        boards.Add(new PriceBoardView(workshop, "Taller Caruso",
            Vehicles.All.Select(v => new PriceLine($"vehiculo:{v.Code}", v.Name, v.PriceCents, true, TrendOf(workshop, $"vehiculo:{v.Code}", false, v.PriceCents))).ToList(), now));

        // Locales de jugadores: cada uno con su precio y su categoría; la gente que hay se ve en el lugar.
        var cfg = Growth();
        foreach (var type in EconomyCatalog.BusinessTypes.Values)
        {
            var site = type.Code == EconomyCatalog.CoffeeStandLocation ? "Esquina del café" : "Club de jazz";
            var locals = new List<PriceLine>();
            var crowd = 0;
            var famous = (Name: "", Fame: 0.0);
            await using (var cmd = new NpgsqlCommand("""
                SELECT b.id, b.name, b.fame, b.price_cents, b.level_capacity, b.level_quality, b.level_attraction, b.last_customers,
                       COALESCE((SELECT sum(quantity) FROM distrito.item_stacks s WHERE s.owner_account_id = b.account_id AND s.location = b.location), 0) > 0
                  FROM distrito.businesses b WHERE b.kind = @k ORDER BY b.fame DESC, b.created_at
                """, c, tx))
            {
                cmd.Parameters.AddWithValue("k", type.Code);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var state = ReadGrowthState(r, 2, r.GetGuid(0), type.Code);
                    var open = r.GetBoolean(8);
                    crowd += open ? r.GetInt32(7) : 0;
                    if (state.Fame > famous.Fame) famous = (r.GetString(1), state.Fame);
                    locals.Add(new PriceLine(r.GetGuid(0).ToString(), $"«{r.GetString(1)}» ({BusinessGrowth.Category(cfg, state)})", state.PriceCents, open,
                        TrendOf(type.SiteNpc, r.GetGuid(0).ToString(), false, state.PriceCents)));
                }
            }
            var opened = locals.Count(l => l.InStock);
            var note = locals.Count == 0 ? "Local libre: se alquila (Oficina Valenti)"
                : opened == 0 ? "Todos cerrados: sin mercadería"
                : famous.Fame >= cfg.Fame.Levels[1] ? $"De moda: «{famous.Name}» ({BusinessGrowth.FameName(cfg, famous.Fame)})"
                : opened == 1 ? "1 local abierto" : $"{opened} locales abiertos";
            if (locals.Count == 0)
                locals.Add(new PriceLine(type.ItemCode, type.Code == EconomyCatalog.CoffeeStandLocation ? "Café en taza" : "Trago en el club", type.SalePriceCents, false));
            boards.Add(new PriceBoardView(type.SiteNpc, site, locals.Take(6).ToList(), now, note, crowd));
        }
        return boards;
    }

    private static async Task<Dictionary<string, long>> PreviousPricesAsync(NpgsqlConnection c, NpgsqlTransaction tx, DateOnly today, CancellationToken ct)
    {
        var prices = new Dictionary<string, long>();
        await using var cmd = new NpgsqlCommand("""
            SELECT npc_code, item_code, buying, price_cents FROM distrito.market_prices
             WHERE day = (SELECT max(day) FROM distrito.market_prices WHERE day < @today)
            """, c, tx);
        cmd.Parameters.AddWithValue("today", today);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            prices[MarketInfoRules.Key(r.GetString(0), r.GetString(1), r.GetBoolean(2))] = r.GetInt64(3);
        return prices;
    }

    // ---------------- Libreta ----------------

    /// <summary>El jugador pasó frente a una pizarra (lo decide el servidor de juego): se anota lo que dice ahora.</summary>
    public async Task LookAtBoardAsync(MarketLookRequest r, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var board = (await BoardsAsync(c, tx, ct)).FirstOrDefault(b => b.NpcCode == r.NpcCode);
        if (board is null)
            return;
        foreach (var l in board.Lines)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.price_notes (character_id, npc_code, place, item_code, name, buying, price_cents, in_stock, seen_at)
                VALUES (@c, @n, @p, @i, @name, @b, @price, @stock, @now)
                ON CONFLICT (character_id, npc_code, item_code, buying)
                DO UPDATE SET place = EXCLUDED.place, name = EXCLUDED.name, price_cents = EXCLUDED.price_cents, in_stock = EXCLUDED.in_stock, seen_at = EXCLUDED.seen_at
                """, c, tx);
            cmd.Parameters.AddWithValue("c", r.CharacterId);
            cmd.Parameters.AddWithValue("n", board.NpcCode);
            cmd.Parameters.AddWithValue("p", board.Place);
            cmd.Parameters.AddWithValue("i", l.ItemCode);
            cmd.Parameters.AddWithValue("name", l.Name);
            cmd.Parameters.AddWithValue("b", l.Buying);
            cmd.Parameters.AddWithValue("price", l.PriceCents);
            cmd.Parameters.AddWithValue("stock", l.InStock);
            cmd.Parameters.AddWithValue("now", board.At);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    // ---------------- Todo lo de un personaje ----------------

    public async Task<MarketInfoView> MarketInfoAsync(Guid characterId, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();

        var notes = new List<PriceNote>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT npc_code, place, item_code, name, price_cents, in_stock, buying, seen_at FROM distrito.price_notes
             WHERE character_id = @c ORDER BY place, buying, name
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                notes.Add(new PriceNote(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetBoolean(5), r.GetBoolean(6),
                    r.GetFieldValue<DateTimeOffset>(7)));
        }

        var edition = await EnsureEditionAsync(c, tx, null, ct);
        var owned = await NewspaperOwnedAsync(c, tx, characterId, MarketInfoRules.DayOf(now), ct);
        var teaser = edition with { News = [], Quotes = [], Classifieds = [] };

        InformantReport? report = null;
        if (await InformantUntilAsync(c, tx, characterId, ct) is { } until && until > now)
            report = new InformantReport(until, await InformantLinesAsync(c, tx, ct));

        var ledgers = await LedgersAsync(c, tx, characterId, ct);
        await tx.CommitAsync(ct);
        return new MarketInfoView(notes, owned ? edition : null, teaser, report, ledgers);
    }

    private static async Task<bool> NewspaperOwnedAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, DateOnly day, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM distrito.newspaper_purchases WHERE character_id = @c AND day = @d", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("d", day);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task<DateTimeOffset?> InformantUntilAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT valid_until FROM distrito.informant_reports WHERE character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? r.GetFieldValue<DateTimeOffset>(0) : null;
    }

    // ---------------- Comprar el diario o la información ----------------

    public Task<EconomyActionResult> BuyMarketInfoAsync(MarketBuyRequest request, CancellationToken ct) =>
        RunAsync(request.CharacterId, request.RequestId, ct, async (c, tx) =>
        {
            var now = clock.GetUtcNow();
            var (seller, price, kind) = request.What switch
            {
                MarketInfoRules.Newspaper => (MarketInfoRules.KioskNpc, MarketInfoRules.NewspaperPriceCents, "newspaper"),
                MarketInfoRules.Informant => (MarketInfoRules.InformantNpc, MarketInfoRules.InformantPriceCents, "informant"),
                _ => throw new EconomyRejected("unknown", "Eso no se vende acá."),
            };
            var day = MarketInfoRules.DayOf(now);
            if (kind == "newspaper" && await NewspaperOwnedAsync(c, tx, request.CharacterId, day, ct))
                throw new EconomyRejected("already_owned", "Ya tenés el diario de hoy. Leelo con P.");
            if (kind == "informant" && await InformantUntilAsync(c, tx, request.CharacterId, ct) is { } until && until > now)
                throw new EconomyRejected("already_owned", $"Ya te estoy pasando lo que sé, hasta las {until.ToOffset(TerritoryRules.ArgentinaOffset):HH:mm}. Mirá con P.");

            var me = await EnsureCharacterAccountAsync(c, tx, request.CharacterId, ct);
            var to = await AccountIdByCodeAsync(c, tx, seller, ct);
            await LockAccountsAsync(c, tx, ct, me, to);
            if (await BalanceAsync(c, tx, me, ct) < price)
                throw new EconomyRejected("insufficient_funds", $"No te alcanza: cuesta {EconomyCatalog.FormatMoney(price)}.");
            var txId = await NewLedgerTransactionAsync(c, tx, request.RequestId, kind,
                kind == "newspaper" ? $"{MarketInfoRules.PaperName}, edición del {day:yyyy-MM-dd}" : "Información del informante", ct);
            await TransferMoneyAsync(c, tx, txId, from: me, to: to, price, ct);

            if (kind == "newspaper")
            {
                await EnsureEditionAsync(c, tx, null, ct);
                await using var cmd = new NpgsqlCommand("INSERT INTO distrito.newspaper_purchases (character_id, day) VALUES (@c, @d)", c, tx);
                cmd.Parameters.AddWithValue("c", request.CharacterId);
                cmd.Parameters.AddWithValue("d", day);
                await cmd.ExecuteNonQueryAsync(ct);
                return $"Compraste {MarketInfoRules.PaperName} por {EconomyCatalog.FormatMoney(price)}. Leelo cuando quieras con P.";
            }
            var validUntil = now + MarketInfoRules.ReportDuration;
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.informant_reports (character_id, valid_until) VALUES (@c, @u)
                ON CONFLICT (character_id) DO UPDATE SET valid_until = EXCLUDED.valid_until
                """, c, tx))
            {
                cmd.Parameters.AddWithValue("c", request.CharacterId);
                cmd.Parameters.AddWithValue("u", validUntil);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return $"Nino se guarda los {EconomyCatalog.FormatMoney(price)}. «Durante una hora, lo que yo sé lo sabés vos.» Mirá con P.";
        });

    // ---------------- El diario ----------------

    /// <summary>La edición de hoy: si no existe, se escribe con lo que pasó en las últimas 24 horas y queda fija.</summary>
    private async Task<NewspaperView> EnsureEditionAsync(NpgsqlConnection c, NpgsqlTransaction tx, List<PriceBoardView>? boards, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var day = MarketInfoRules.DayOf(now);
        if (await LoadEditionAsync(c, tx, day, ct) is { } existing)
            return existing;

        boards ??= await BoardsAsync(c, tx, ct);
        var since = now.AddHours(-24);
        async Task<int> CountAsync(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c, tx);
            cmd.Parameters.AddWithValue("since", since);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }
        var robberies = await CountAsync("SELECT count(*) FROM distrito.ledger_transactions WHERE kind = 'pve_robbery' AND created_at >= @since");
        var forced = await CountAsync("SELECT count(*) FROM distrito.ledger_transactions WHERE kind = 'vehicle_forced' AND created_at >= @since");
        var deliveries = await CountAsync("SELECT count(*) FROM distrito.ledger_transactions WHERE kind IN ('contract_delivery', 'encargo') AND created_at >= @since");
        var orders = await CountAsync("SELECT count(*) FROM distrito.supplier_orders WHERE status = 'preparando'");

        var newBusinesses = new List<string>();
        var open = new List<OpenBusinessFact>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT b.name, b.kind, b.has_helper, b.created_at >= @since,
                   COALESCE((SELECT sum(quantity) FROM distrito.item_stacks s WHERE s.owner_account_id = b.account_id AND s.location = b.location), 0) > 0
              FROM distrito.businesses b ORDER BY b.created_at
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("since", since);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var type = EconomyCatalog.BusinessTypes[r.GetString(1)];
                var place = type.Code == EconomyCatalog.CoffeeStandLocation ? "la Esquina del café" : "el Club de jazz";
                if (r.GetBoolean(3))
                    newBusinesses.Add($"«{r.GetString(0)}», {type.Name.ToLowerInvariant()}, en {place}");
                open.Add(new OpenBusinessFact(r.GetString(0), type.Name, place, r.GetBoolean(2), r.GetBoolean(4)));
            }
        }

        var reviews = new List<string>();
        string? best = null;
        await using (var cmd = new NpgsqlCommand("""
            SELECT b.name, r.stars, r.text FROM distrito.business_reviews r JOIN distrito.businesses b ON b.id = r.business_id
             WHERE r.created_at >= @since ORDER BY r.stars DESC, r.created_at DESC LIMIT 4
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("since", since);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                reviews.Add($"{new string('★', r.GetInt32(1))} {r.GetString(2)}");
                if (r.GetInt32(1) == 5) best ??= r.GetString(0);
            }
        }
        // Vecinos que alcanzaron al auto de una persecución (escenas de la ciudad).
        var heroes = new List<string>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT description FROM distrito.ledger_transactions WHERE kind = 'scene_persecucion' AND created_at >= @since ORDER BY created_at DESC LIMIT 2
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("since", since);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                heroes.Add(r.GetString(0));
        }
        var growthCfg = Growth();
        var famous = new List<string>();
        await using (var cmd = new NpgsqlCommand("SELECT name, fame FROM distrito.businesses WHERE fame >= @min ORDER BY fame DESC LIMIT 3", c, tx))
        {
            cmd.Parameters.AddWithValue("min", growthCfg.Fame.Levels[2]);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                famous.Add($"«{r.GetString(0)}» ({BusinessGrowth.FameName(growthCfg, r.GetDouble(1)).ToLowerInvariant()})");
        }
        var agenda = EconomyCatalog.BusinessTypes.Values.Select(t =>
            $"{BusinessGrowth.KindOf(growthCfg, t.Code).EventName} en {(t.Code == EconomyCatalog.CoffeeStandLocation ? "la Esquina del café" : "el Club de jazz")}, "
            + $"desde las {BusinessGrowth.EventStart(growthCfg, t.Code, day).ToOffset(TerritoryRules.ArgentinaOffset):HH:mm}: el doble de gente").ToList();

        var facts = new NewspaperFacts(day, boards, await PreviousPricesAsync(c, tx, day, ct), await LoadFamilyMoodsAsync(c, tx, ct),
            robberies, forced, newBusinesses, open, (await LoadTerritoryAsync(c, tx, Guid.Empty, ct)).Zones,
            UpcomingEvents(TimeSpan.FromHours(24)).Where(e => MarketInfoRules.DayOf(e.StartsAt) == day).ToList(), deliveries, orders,
            reviews, agenda, famous, best, heroes);
        var edition = MarketInfoRules.Write(facts);

        await using (var insert = new NpgsqlCommand(
            "INSERT INTO distrito.newspaper_editions (day, content) VALUES (@d, @content::jsonb) ON CONFLICT (day) DO NOTHING", c, tx))
        {
            insert.Parameters.AddWithValue("d", day);
            insert.Parameters.AddWithValue("content", JsonSerializer.Serialize(edition, DistritoApiClient.Json));
            await insert.ExecuteNonQueryAsync(ct);
        }
        foreach (var b in boards)
            foreach (var l in b.Lines)
            {
                await using var price = new NpgsqlCommand("""
                    INSERT INTO distrito.market_prices (day, npc_code, item_code, buying, price_cents, in_stock)
                    VALUES (@d, @n, @i, @b, @p, @s) ON CONFLICT DO NOTHING
                    """, c, tx);
                price.Parameters.AddWithValue("d", day);
                price.Parameters.AddWithValue("n", b.NpcCode);
                price.Parameters.AddWithValue("i", l.ItemCode);
                price.Parameters.AddWithValue("b", l.Buying);
                price.Parameters.AddWithValue("p", l.PriceCents);
                price.Parameters.AddWithValue("s", l.InStock);
                await price.ExecuteNonQueryAsync(ct);
            }
        // Si otro pedido la escribió al mismo tiempo, vale la que quedó guardada.
        return await LoadEditionAsync(c, tx, day, ct) ?? edition;
    }

    private static async Task<NewspaperView?> LoadEditionAsync(NpgsqlConnection c, NpgsqlTransaction tx, DateOnly day, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT content::text FROM distrito.newspaper_editions WHERE day = @d", c, tx);
        cmd.Parameters.AddWithValue("d", day);
        return await cmd.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<NewspaperView>(json, DistritoApiClient.Json) : null;
    }

    // ---------------- El informante ----------------

    /// <summary>Lo que no sale en el diario: existencias, pedidos, la puerta de atrás del Taller, los otros locales y los fletes en la calle.</summary>
    private async Task<List<InformantLine>> InformantLinesAsync(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken ct)
    {
        var m = EconomyCatalog.FormatMoney;
        var lines = new List<InformantLine>();

        var deposit = EconomyCatalog.DepositNpc;
        var depositAccount = await AccountIdByCodeAsync(c, tx, deposit, ct);
        var stock = new List<string>();
        foreach (var item in SupplierRules.Catalog[deposit])
        {
            var (name, _) = await LoadItemAsync(c, tx, item, ct);
            stock.Add($"{await QuantityAsync(c, tx, depositAccount, item, StockLocation(deposit), ct)} × {name.ToLowerInvariant()}");
        }
        lines.Add(new InformantLine("Almacén Moretti", $"En el depósito quedan {string.Join(", ", stock)}."));
        await using (var cmd = new NpgsqlCommand("""
            SELECT count(*)::int, COALESCE(sum(quantity), 0)::int FROM distrito.supplier_orders WHERE supplier_code = @s AND status = 'preparando'
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("s", deposit);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
                lines.Add(new InformantLine("Almacén Moretti", r.GetInt32(0) == 0
                    ? "No tiene pedidos por mayor en preparación: si pedís ahora, sale rápido."
                    : $"Tiene {r.GetInt32(0)} pedidos por mayor en preparación ({r.GetInt32(1)} unidades). Hay cola."));
        }

        var workshopMood = await MoodOfNpcAsync(c, tx, EconomyCatalog.WorkshopNpc, ct);
        lines.Add(new InformantLine("Taller Caruso",
            "Por la puerta de atrás vende fierros: " + string.Join(", ", Weapons.All.Select(w =>
                $"{w.Name.ToLowerInvariant()} {m(FamilyRules.SellPrice(w.PriceCents, workshopMood))} (nivel {w.MinLevel})"))
            + $". Y te los compra al {NpcSaleRules.PricePercent} %."));

        foreach (var f in await LoadFamilyMoodsAsync(c, tx, ct))
            if (f.Mood == FamilyRules.Rivalry)
                lines.Add(new InformantLine(f.Name, "Están con la sangre en el ojo: cobran 10 % más y pagan menos los trabajos. Mejor no hacerlos enojar más."));
            else if (f.Mood == FamilyRules.Cooperation)
                lines.Add(new InformantLine(f.Name, "Están contentos con la calle: los trabajos para ellos pagan mejor."));

        await using (var cmd = new NpgsqlCommand("""
            SELECT b.name, b.kind, b.has_helper,
                   COALESCE((SELECT sum(quantity) FROM distrito.item_stacks s WHERE s.owner_account_id = b.account_id AND s.location = b.location), 0)::int
              FROM distrito.businesses b ORDER BY b.kind, b.name
            """, c, tx))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var type = EconomyCatalog.BusinessTypes[r.GetString(1)];
                var units = r.GetInt32(3);
                lines.Add(new InformantLine($"«{r.GetString(0)}»", units == 0
                    ? $"{type.Name}: cerrado, sin mercadería. Su gente se va a otro lado."
                    : $"{type.Name}: le quedan {units} × {type.ItemName}{(r.GetBoolean(2) ? $", con {type.EmployeeName}" : "")}."));
            }
        }

        var now = clock.GetUtcNow();
        await using (var cmd = new NpgsqlCommand("""
            SELECT template_code, expires_at FROM distrito.contracts WHERE status = 'active' AND expires_at > @now ORDER BY expires_at
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("now", now);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var freights = new List<string>();
            while (await r.ReadAsync(ct))
                if (EconomyCatalog.Templates.TryGetValue(r.GetString(0), out var t) && t.Consigned)
                    freights.Add($"{t.Title.Replace("Flete: ", "")} (quedan {Math.Max(1, (int)(r.GetFieldValue<DateTimeOffset>(1) - now).TotalMinutes)} min)");
            lines.Add(new InformantLine("Fletes en la calle", freights.Count == 0
                ? "Ahora no hay mercadería ajena viajando."
                : $"Hay {freights.Count} con carga: {string.Join("; ", freights.Take(5))}."));
        }
        return lines;
    }

    // ---------------- Libro de cuentas ----------------

    private async Task<List<LedgerView>> LedgersAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var dayStart = TerritoryRules.DayStart(now);
        var firstToday = dayStart.ToUnixTimeSeconds() / CycleSeconds;
        var owned = new List<(Guid Id, string Name, string Kind, Guid Account, string Location)>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT id, name, kind, account_id, location FROM distrito.businesses WHERE owner_character_id = @c ORDER BY created_at", c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                owned.Add((r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetGuid(3), r.GetString(4)));
        }

        var ledgers = new List<LedgerView>();
        foreach (var b in owned)
        {
            var type = EconomyCatalog.BusinessTypes[b.Kind];
            int sold = 0, wanted = 0;
            long revenue = 0, rent = 0, wages = 0;
            await using (var cmd = new NpgsqlCommand("""
                SELECT COALESCE(sum(units_sold), 0)::int, COALESCE(sum(COALESCE(units_wanted, units_sold)), 0)::int,
                       COALESCE(sum(revenue_cents), 0)::bigint, COALESCE(sum(rent_cents), 0)::bigint, COALESCE(sum(wages_cents), 0)::bigint
                  FROM distrito.business_cycles WHERE business_id = @b AND cycle >= @first
                """, c, tx))
            {
                cmd.Parameters.AddWithValue("b", b.Id);
                cmd.Parameters.AddWithValue("first", firstToday);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct)) { sold = r.GetInt32(0); wanted = r.GetInt32(1); revenue = r.GetInt64(2); rent = r.GetInt64(3); wages = r.GetInt64(4); }
            }
            var recent = new List<LedgerCycle>();
            await using (var cmd = new NpgsqlCommand("""
                SELECT cycle, units_sold, COALESCE(units_wanted, units_sold), revenue_cents, rent_cents + wages_cents
                  FROM distrito.business_cycles WHERE business_id = @b ORDER BY cycle DESC LIMIT 12
                """, c, tx))
            {
                cmd.Parameters.AddWithValue("b", b.Id);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    recent.Add(new LedgerCycle(DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0) * CycleSeconds), r.GetInt32(1), r.GetInt32(2), r.GetInt64(3),
                        r.GetInt64(4) + r.GetInt32(1) * type.ItemPriceCents));
            }
            var stock = await QuantityAsync(c, tx, b.Account, type.ItemCode, b.Location, ct);
            var cash = await BalanceAsync(c, tx, b.Account, ct);
            var growth = await GrowthViewAsync(c, tx, characterId, b.Kind, ct);
            long average = type.SalePriceCents;
            await using (var avg = new NpgsqlCommand("SELECT avg(COALESCE(price_cents, @ref))::bigint FROM distrito.businesses WHERE kind = @k", c, tx))
            {
                avg.Parameters.AddWithValue("ref", type.SalePriceCents);
                avg.Parameters.AddWithValue("k", b.Kind);
                if (await avg.ExecuteScalarAsync(ct) is long a) average = a;
            }
            ledgers.Add(new LedgerView(b.Id, b.Name, b.Kind, stock, cash, growth?.PriceCents ?? type.SalePriceCents, type.ItemPriceCents, average,
                sold, wanted, revenue, rent, wages, recent, growth));
        }
        return ledgers;
    }
}
