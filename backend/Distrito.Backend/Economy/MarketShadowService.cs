using System.Text.Json;
using Distrito.Shared.Economy;
using Distrito.Shared.Market;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Modo sombra de la economía de mercado (etapa 2 de la hoja de ruta). Después de cada ciclo económico real, le pasa al
/// mercado nuevo (<see cref="MarketShadow"/>) los locales reales y anota lo que habría pasado. Solo lee del juego: no
/// mueve plata ni mercadería, y si algo falla acá la economía real sigue igual.
/// Se apaga con <c>Market:Shadow = false</c>. El informe: <c>GET /internal/market/shadow</c> (ver docs/MODO-SOMBRA.md).
/// </summary>
public sealed class MarketShadowService(NpgsqlDataSource db, EconomyService economy, IConfiguration config, ILogger<MarketShadowService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MarketShadow? _shadow;
    private MarketConfig? _config;
    private DateTime _stamp;
    private long _lastCycle = -1;
    private bool _warnedMissing;

    public bool Enabled => config.GetValue("Market:Shadow", true);

    private string ConfigFile => config.GetValue<string>("Market:ConfigFile") ?? Path.Combine(AppContext.BaseDirectory, "mercado.json");

    /// <summary>Observa un ciclo (una sola vez por ciclo, aunque lo llamen varias veces o varios procesos). Devuelve si lo anotó.</summary>
    public async Task<bool> ObserveAsync(long cycle, CancellationToken ct)
    {
        if (!Enabled)
            return false;
        await _gate.WaitAsync(ct);
        try
        {
            if (cycle <= _lastCycle)
                return false;
            await using var c = await db.OpenConnectionAsync(ct);
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM distrito.market_shadow_cycles WHERE cycle = @c", c))
            {
                exists.Parameters.AddWithValue("c", cycle);
                if (await exists.ExecuteScalarAsync(ct) is not null)
                {
                    _lastCycle = cycle;
                    return false;
                }
            }
            if (await ShadowAsync(c, ct) is not { } shadow)
                return false;

            var result = shadow.Observe(cycle, await ShopsAsync(c, cycle, _config!, ct));
            await using var tx = await c.BeginTransactionAsync(ct);
            await using (var save = new NpgsqlCommand("""
                INSERT INTO distrito.market_shadow_cycles (cycle, data) VALUES (@c, @d::jsonb) ON CONFLICT DO NOTHING;
                DELETE FROM distrito.market_shadow_cycles WHERE cycle < @c - @keep;
                """, c, tx))
            {
                save.Parameters.AddWithValue("c", cycle);
                save.Parameters.AddWithValue("d", JsonSerializer.Serialize(result, Json));
                save.Parameters.AddWithValue("keep", 7L * _config!.CyclesPerDay);
                await save.ExecuteNonQueryAsync(ct);
            }
            if (shadow.ClosedDay is { } day)
            {
                await using var saveDay = new NpgsqlCommand(
                    "INSERT INTO distrito.market_shadow_days (last_cycle, data) VALUES (@c, @d::jsonb) ON CONFLICT DO NOTHING", c, tx);
                saveDay.Parameters.AddWithValue("c", cycle);
                saveDay.Parameters.AddWithValue("d", JsonSerializer.Serialize(day, Json));
                await saveDay.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);

            foreach (var alert in result.Alerts)
                logger.LogError("Modo sombra: {Alert}", alert);
            if (shadow.ClosedDay is { } closed)
            {
                logger.LogInformation("Modo sombra, día {Day}: se vendieron {Real}; con el mercado nuevo, {Shadow} ({Diff:+0;-0} %)",
                    closed.Day, closed.RealSold, closed.ShadowSold, closed.DifferencePct);
                foreach (var alert in closed.Alerts)
                    logger.LogWarning("Modo sombra, día {Day}: {Alert}", closed.Day, alert);
            }
            _lastCycle = cycle;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>El mercado simulado en memoria; al arrancar (o si cambia config/mercado.json) retoma las zonas del último ciclo.</summary>
    private async Task<MarketShadow?> ShadowAsync(NpgsqlConnection c, CancellationToken ct)
    {
        var file = ConfigFile;
        if (!File.Exists(file))
        {
            if (!_warnedMissing)
                logger.LogWarning("Modo sombra en pausa: falta {File}", file);
            _warnedMissing = true; // no volver a avisar en cada ciclo
            return null;
        }
        var stamp = File.GetLastWriteTimeUtc(file);
        if (_shadow is not null && stamp == _stamp)
            return _shadow;

        var parsed = MarketConfig.Parse(await File.ReadAllTextAsync(file, ct));
        if (parsed.Validate() is { Count: > 0 } errors)
        {
            logger.LogError("Modo sombra: mercado.json con errores: {Errors}", string.Join(" | ", errors));
            return _shadow; // sigue con la config anterior, si había
        }
        var shadow = new MarketShadow(parsed);
        await using (var last = new NpgsqlCommand("SELECT data FROM distrito.market_shadow_cycles ORDER BY cycle DESC LIMIT 1", c))
            if (await last.ExecuteScalarAsync(ct) is string json && JsonSerializer.Deserialize<ShadowCycle>(json, Json) is { } previous)
                shadow.Restore(previous.Zones);
        _shadow = shadow;
        _config = parsed;
        _stamp = stamp;
        logger.LogInformation("Modo sombra encendido: el mercado nuevo calcula al lado de la economía real, sin decidir nada");
        return shadow;
    }

    /// <summary>Los locales reales de los productos del mercado, como estaban al empezar el ciclo, y lo que vendieron en él.</summary>
    private async Task<List<ShadowShopInput>> ShopsAsync(NpgsqlConnection c, long cycle, MarketConfig market, CancellationToken ct)
    {
        var growth = economy.GrowthRules();
        var shops = new List<ShadowShopInput>();
        foreach (var type in EconomyCatalog.BusinessTypes.Values.Where(t => market.Products.Any(p => p.Code == t.ItemCode)))
        {
            await using var cmd = new NpgsqlCommand("""
                SELECT b.id, b.name, b.price_cents, b.level_capacity, b.level_quality, b.level_attraction, b.fame, b.has_helper,
                       COALESCE((SELECT sum(s.quantity) FROM distrito.item_stacks s
                                  WHERE s.owner_account_id = b.account_id AND s.item_code = @item AND s.location = b.location), 0)::int,
                       COALESCE(bc.units_sold, 0), COALESCE(bc.units_wanted, 0)
                  FROM distrito.businesses b
                  LEFT JOIN distrito.business_cycles bc ON bc.business_id = b.id AND bc.cycle = @cycle
                 WHERE b.kind = @kind
                 ORDER BY b.id
                """, c);
            cmd.Parameters.AddWithValue("item", type.ItemCode);
            cmd.Parameters.AddWithValue("cycle", cycle);
            cmd.Parameters.AddWithValue("kind", type.Code);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var kind = BusinessGrowth.KindOf(growth, type.Code);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var price = reader.IsDBNull(2) ? type.SalePriceCents : reader.GetInt64(2);
                var state = new GrowthState(id, type.Code, reader.GetDouble(6), price, reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                    HasHelper: reader.GetBoolean(7));
                var sold = reader.GetInt32(9);
                shops.Add(new ShadowShopInput(id.ToString(), reader.GetString(1), type.ItemCode, price,
                    Stock: reader.GetInt32(8) + sold, // lo que tenía al empezar el ciclo
                    BusinessGrowth.CapacityOf(growth, state), BusinessGrowth.QualityOf(growth, state), kind.X, kind.Z, state.HasHelper,
                    sold, reader.GetInt32(10)));
            }
        }
        return shops;
    }

    /// <summary>El informe para el equipo: los últimos días, el último ciclo y un resumen en palabras.</summary>
    public async Task<ShadowReport> ReportAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 60);
        await using var c = await db.OpenConnectionAsync(ct);
        var list = new List<ShadowDay>();
        await using (var cmd = new NpgsqlCommand("SELECT data FROM distrito.market_shadow_days ORDER BY last_cycle DESC LIMIT @n", c))
        {
            cmd.Parameters.AddWithValue("n", days);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (JsonSerializer.Deserialize<ShadowDay>(reader.GetString(0), Json) is { } d)
                    list.Add(d);
        }
        ShadowCycle? last = null;
        await using (var cmd = new NpgsqlCommand("SELECT data FROM distrito.market_shadow_cycles ORDER BY cycle DESC LIMIT 1", c))
            if (await cmd.ExecuteScalarAsync(ct) is string json)
                last = JsonSerializer.Deserialize<ShadowCycle>(json, Json);

        var summary = new List<string>();
        if (!Enabled)
            summary.Add("El modo sombra está apagado (Market:Shadow = false).");
        if (last is null)
            summary.Add("Todavía no se observó ningún ciclo.");
        else
            summary.Add($"Último ciclo observado: {last.Cycle} ({last.Shops.Count} locales).");
        if (list.Count > 0)
        {
            var real = list.Sum(d => d.RealSold);
            var shadow = list.Sum(d => d.ShadowSold);
            summary.Add($"Últimos {list.Count} días: se vendieron {real}; con el mercado nuevo se habrían vendido {shadow} ({MarketShadow.Difference(real, shadow):+0;-0} %).");
            var alerts = list.Sum(d => d.Alerts.Count);
            summary.Add(alerts == 0 ? "Sin avisos." : $"{alerts} avisos para mirar (ver cada día).");
        }
        return new ShadowReport(Enabled, last?.Cycle, last, list, summary);
    }
}

/// <summary>Lo que devuelve <c>GET /internal/market/shadow</c>.</summary>
public sealed record ShadowReport(bool Enabled, long? LastCycle, ShadowCycle? Last, List<ShadowDay> Days, List<string> Summary);
