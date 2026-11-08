using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Negocios que crecen (aprobado por el equipo, 2026-11-02): la clientela sale de la gente de Bellmont (config/negocios.json),
/// la fama sube atendiendo bien y baja de a poco con el local descuidado, y el dueño mejora el local sin tope y elige el precio.
/// Las mejoras las cobra el Taller Caruso (la plata queda en el juego).
/// </summary>
public sealed partial class EconomyService
{
    private static readonly object GrowthLock = new();
    private static GrowthConfig? _growth;
    private static DateTime _growthStamp;
    private static string _growthFile = "";

    private string GrowthFile => config.GetValue<string>("Business:GrowthFile") ?? Path.Combine(AppContext.BaseDirectory, "negocios.json");

    /// <summary>
    /// Cuánta gente más (o menos) que la config: 1 en el juego. Las pruebas usan una ciudad enorme para que mande la capacidad
    /// del local y las ventas sean exactas.
    /// </summary>
    private double GrowthCrowd => Math.Max(0, config.GetValue("Business:Crowd", 1.0));

    /// <summary>Las reglas de crecimiento vigentes (las usa el modo sombra para la capacidad y la calidad reales de cada local).</summary>
    internal GrowthConfig GrowthRules() => Growth();

    /// <summary>Reglas de crecimiento; se releen si cambia el archivo. Con el archivo roto se mantienen las anteriores.</summary>
    private GrowthConfig Growth()
    {
        var file = GrowthFile;
        lock (GrowthLock)
        {
            var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            if (_growth is not null && stamp == _growthStamp && file == _growthFile)
                return _growth;
            var parsed = stamp == DateTime.MinValue ? null : GrowthConfig.Parse(File.ReadAllText(file));
            if (parsed is not null && parsed.Validate() is { Count: > 0 } errors)
            {
                Console.Error.WriteLine($"[DISTRITO] negocios.json con errores: {string.Join(" | ", errors)}");
                parsed = null;
            }
            _growth = parsed ?? _growth ?? throw new InvalidOperationException($"Falta la config de negocios ({file}).");
            _growthStamp = stamp;
            _growthFile = file;
            return _growth;
        }
    }

    private static GrowthState ReadGrowthState(NpgsqlDataReader r, int at, Guid id, string kind) =>
        new(id, kind, r.GetDouble(at), r.IsDBNull(at + 1) ? EconomyCatalog.BusinessTypes[kind].SalePriceCents : r.GetInt64(at + 1),
            r.GetInt32(at + 2), r.GetInt32(at + 3), r.GetInt32(at + 4));

    /// <summary>Los otros locales del mismo rubro (la competencia por la misma gente).</summary>
    private static async Task<List<GrowthState>> GrowthRivalsAsync(NpgsqlConnection c, NpgsqlTransaction tx, string kind, Guid? except, CancellationToken ct)
    {
        var list = new List<GrowthState>();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, fame, price_cents, level_capacity, level_quality, level_attraction FROM distrito.businesses
             WHERE kind = @k AND (@except::uuid IS NULL OR id <> @except)
            """, c, tx);
        cmd.Parameters.AddWithValue("k", kind);
        cmd.Parameters.AddWithValue("except", (object?)except ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ReadGrowthState(r, 1, r.GetGuid(0), kind));
        return list;
    }

    private sealed record OwnedGrowth(Guid Id, string Name, Guid Account, GrowthState State, int LastCustomers);

    private static async Task<OwnedGrowth?> OwnedGrowthAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, string kind, bool forUpdate, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, name, account_id, fame, price_cents, level_capacity, level_quality, level_attraction, last_customers
              FROM distrito.businesses WHERE owner_character_id = @c AND kind = @k {(forUpdate ? "FOR UPDATE" : "")}
            """, c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("k", kind);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        var id = r.GetGuid(0);
        return new OwnedGrowth(id, r.GetString(1), r.GetGuid(2), ReadGrowthState(r, 3, id, kind), r.GetInt32(8));
    }

    private BusinessType KindOf(string kind) =>
        EconomyCatalog.BusinessTypes.TryGetValue(kind, out var type) ? type : throw new EconomyRejected("unknown_business", "Ese negocio no existe.");

    // ---------------- Mejoras ----------------

    public Task<EconomyActionResult> UpgradeBusinessAsync(BusinessUpgradeRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var type = KindOf(r.Kind);
            if (!BusinessGrowth.Branches.Contains(r.Branch))
                throw new EconomyRejected("unknown_branch", "Esa mejora no existe.");
            var owned = await OwnedGrowthAsync(c, tx, r.CharacterId, type.Code, forUpdate: true, ct)
                        ?? throw new EconomyRejected("no_business", $"No tenés un {type.Name.ToLowerInvariant()}.");
            var cfg = Growth();
            var level = BusinessGrowth.LevelOf(owned.State, r.Branch);
            var cost = BusinessGrowth.UpgradeCost(cfg, type.Code, r.Branch, level);
            var next = BusinessGrowth.UpgradeName(cfg, type.Code, r.Branch, level + 1);

            // Se paga primero con la caja del negocio; si no alcanza, el resto sale del bolsillo del dueño.
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var caruso = await AccountIdByCodeAsync(c, tx, EconomyCatalog.WorkshopNpc, ct);
            await LockAccountsAsync(c, tx, ct, owned.Account, me, caruso);
            var fromBusiness = Math.Min(cost, await BalanceAsync(c, tx, owned.Account, ct));
            var fromPocket = cost - fromBusiness;
            if (fromPocket > 0 && await BalanceAsync(c, tx, me, ct) < fromPocket)
                throw new EconomyRejected("insufficient_funds",
                    $"{next} cuesta {EconomyCatalog.FormatMoney(cost)}: entre la caja del negocio y tu bolsillo no alcanza.");

            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "business_upgrade", $"Mejora de «{owned.Name}»: {next} (Taller Caruso)", ct);
            if (fromBusiness > 0)
                await TransferMoneyAsync(c, tx, txId, from: owned.Account, to: caruso, fromBusiness, ct);
            if (fromPocket > 0)
                await TransferMoneyAsync(c, tx, txId, from: me, to: caruso, fromPocket, ct);
            var column = r.Branch switch { BusinessGrowth.Capacity => "level_capacity", BusinessGrowth.Quality => "level_quality", _ => "level_attraction" };
            await using (var cmd = new NpgsqlCommand($"UPDATE distrito.businesses SET {column} = {column} + 1 WHERE id = @id", c, tx))
            {
                cmd.Parameters.AddWithValue("id", owned.Id);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.business_upgrades (business_id, branch, level, cost_cents, transaction_id) VALUES (@b, @branch, @l, @cost, @tx)
                """, c, tx))
            {
                cmd.Parameters.AddWithValue("b", owned.Id);
                cmd.Parameters.AddWithValue("branch", r.Branch);
                cmd.Parameters.AddWithValue("l", level + 1);
                cmd.Parameters.AddWithValue("cost", cost);
                cmd.Parameters.AddWithValue("tx", txId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            var upgraded = r.Branch switch
            {
                BusinessGrowth.Capacity => owned.State with { Capacity = level + 1 },
                BusinessGrowth.Quality => owned.State with { Quality = level + 1 },
                _ => owned.State with { Attraction = level + 1 },
            };
            var category = BusinessGrowth.Category(cfg, upgraded);
            var promoted = category != BusinessGrowth.Category(cfg, owned.State) ? $" ¡«{owned.Name}» ahora es {category}!" : "";
            var priceTip = r.Branch == BusinessGrowth.Quality
                ? $" Con más calidad, la gente acepta pagar hasta {EconomyCatalog.FormatMoney(BusinessGrowth.FairPriceCents(cfg, upgraded))} sin espantarse."
                : "";
            return $"El Taller Caruso terminó la obra: {next} ({EconomyCatalog.FormatMoney(cost)}).{promoted}{priceTip}";
        });

    // ---------------- Precio ----------------

    public Task<EconomyActionResult> SetBusinessPriceAsync(BusinessPriceRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var type = KindOf(r.Kind);
            var owned = await OwnedGrowthAsync(c, tx, r.CharacterId, type.Code, forUpdate: true, ct)
                        ?? throw new EconomyRejected("no_business", $"No tenés un {type.Name.ToLowerInvariant()}.");
            var cfg = Growth();
            var (min, max) = BusinessGrowth.PriceRange(cfg, type.Code);
            if (r.PriceCents < min || r.PriceCents > max)
                throw new EconomyRejected("price_range", $"El precio tiene que estar entre {EconomyCatalog.FormatMoney(min)} y {EconomyCatalog.FormatMoney(max)}.");
            await using (var cmd = new NpgsqlCommand("UPDATE distrito.businesses SET price_cents = @p WHERE id = @id", c, tx))
            {
                cmd.Parameters.AddWithValue("p", r.PriceCents);
                cmd.Parameters.AddWithValue("id", owned.Id);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            var fair = BusinessGrowth.FairPriceCents(cfg, owned.State);
            var opinion = r.PriceCents > fair * 1.15 ? "Para tu calidad es caro: va a venir menos gente y la fama sube menos."
                : r.PriceCents < fair * 0.85 ? "Barato para lo que das: viene más gente, pero ganás menos por venta."
                : "Un precio justo para tu calidad.";
            return $"«{owned.Name}» ahora cobra {EconomyCatalog.FormatMoney(r.PriceCents)}. {opinion}";
        });

    // ---------------- Vista del dueño ----------------

    private async Task<BusinessGrowthView?> GrowthViewAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, string kind, CancellationToken ct)
    {
        if (await OwnedGrowthAsync(c, tx, characterId, kind, forUpdate: false, ct) is not { } owned)
            return null;
        var cfg = Growth();
        var today = MarketInfoRules.DayOf(clock.GetUtcNow());
        int? stars = null;
        string? text = null;
        await using (var cmd = new NpgsqlCommand("SELECT stars, text FROM distrito.business_reviews WHERE business_id = @b ORDER BY day DESC LIMIT 1", c, tx))
        {
            cmd.Parameters.AddWithValue("b", owned.Id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) { stars = r.GetInt32(0); text = r.GetString(1); }
        }
        return BusinessGrowthViews.Build(cfg, owned.State, owned.LastCustomers, today, stars, text);
    }
}
