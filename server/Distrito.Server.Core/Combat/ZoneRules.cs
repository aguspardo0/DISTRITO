using System.Text.Json;
using System.Text.Json.Nodes;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Combat;

/// <summary>Permisos efectivos de un lugar en un momento (ya aplicadas las excepciones).</summary>
public sealed record ZonePermissions(
    bool Pvp, bool Weapons, bool Melee, bool Looting, bool Robbery, bool Pursuits, bool Vehicles, bool Pickup,
    bool Activities, bool LoseGoodsOnDeath, int DropSeconds);

/// <summary>Qué está pasando en el lugar (lo decide el servidor).</summary>
public readonly record struct ZoneContext(bool Safe, bool Disputed, bool Event, bool War);

/// <summary>
/// Reglas de zona configurables (config/zonas.json). El combate pregunta acá en vez de tener reglas fijas:
/// permisos base, por zona, excepciones temporales (disputa, evento, guerra) y la zona segura, que gana siempre.
/// </summary>
public sealed class ZoneRules
{
    private static readonly Dictionary<string, string> Keys = new()
    {
        ["pvp"] = nameof(ZonePermissions.Pvp), ["armas"] = nameof(ZonePermissions.Weapons), ["cuerpoACuerpo"] = nameof(ZonePermissions.Melee),
        ["saqueo"] = nameof(ZonePermissions.Looting), ["robos"] = nameof(ZonePermissions.Robbery), ["persecuciones"] = nameof(ZonePermissions.Pursuits),
        ["vehiculos"] = nameof(ZonePermissions.Vehicles), ["recogida"] = nameof(ZonePermissions.Pickup), ["actividades"] = nameof(ZonePermissions.Activities),
        ["perderMercaderiaAlMorir"] = nameof(ZonePermissions.LoseGoodsOnDeath), ["segundosBulto"] = nameof(ZonePermissions.DropSeconds),
    };

    private readonly Dictionary<string, object> _base;
    private readonly Dictionary<string, Dictionary<string, object>> _zones;
    private readonly Dictionary<string, Dictionary<string, object>> _exceptions;
    private readonly Dictionary<string, object> _safe;

    public float SafeRadius { get; }

    private ZoneRules(Dictionary<string, object> @base, Dictionary<string, Dictionary<string, object>> zones,
        Dictionary<string, Dictionary<string, object>> exceptions, Dictionary<string, object> safe, float safeRadius)
        => (_base, _zones, _exceptions, _safe, SafeRadius) = (@base, zones, exceptions, safe, safeRadius);

    /// <summary>Reglas para partidas locales sin cuentas: se pelea en todos lados salvo la zona segura.</summary>
    public static ZoneRules LocalDefault { get; } = Parse("""
        { "base": { "pvp": true, "armas": true, "cuerpoACuerpo": true, "saqueo": true, "robos": true, "persecuciones": true,
                    "vehiculos": true, "recogida": true, "actividades": true, "perderMercaderiaAlMorir": true, "segundosBulto": 300 },
          "zonas": {}, "excepciones": {}, "zonaSegura": { "radio": 5, "pvp": false, "armas": false, "cuerpoACuerpo": false, "robos": false, "saqueo": false } }
        """).Rules!;

    /// <summary>Reglas del servidor con cuentas si todavía no llegó el archivo del backend: PvP solo con excepciones.</summary>
    public static ZoneRules ServerDefault { get; } = Parse("""
        { "base": { "pvp": false, "armas": true, "cuerpoACuerpo": true, "saqueo": true, "robos": false, "persecuciones": false,
                    "vehiculos": true, "recogida": true, "actividades": true, "perderMercaderiaAlMorir": true, "segundosBulto": 300 },
          "zonas": {}, "excepciones": { "disputa": { "pvp": true }, "evento": { "pvp": true }, "guerra": { "pvp": true } },
          "zonaSegura": { "radio": 5, "pvp": false, "armas": false, "cuerpoACuerpo": false, "robos": false, "saqueo": false } }
        """).Rules!;

    public static (ZoneRules? Rules, List<string> Errors) Parse(string json)
    {
        var errors = new List<string>();
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException bad) { return (null, [$"El archivo no es JSON válido: {bad.Message}"]); }
        if (root is not JsonObject obj)
            return (null, ["El archivo tiene que ser un objeto { ... }."]);

        Dictionary<string, object> Section(JsonNode? node, string where, bool requireAll)
        {
            var result = new Dictionary<string, object>();
            if (node is null)
                return result;
            if (node is not JsonObject o) { errors.Add($"{where}: tiene que ser un objeto."); return result; }
            foreach (var (key, value) in o)
            {
                if (key == "radio")
                    continue;
                if (!Keys.ContainsKey(key)) { errors.Add($"{where}: '{key}' no es un permiso conocido ({string.Join(", ", Keys.Keys)})."); continue; }
                if (key == "segundosBulto")
                {
                    if (value is JsonValue v && v.TryGetValue<int>(out var n) && n is >= 10 and <= 86400) result[key] = n;
                    else errors.Add($"{where}: segundosBulto va de 10 a 86400.");
                }
                else if (value is JsonValue v && v.TryGetValue<bool>(out var b)) result[key] = b;
                else errors.Add($"{where}: '{key}' tiene que ser true o false.");
            }
            if (requireAll)
                foreach (var k in Keys.Keys.Where(k => !result.ContainsKey(k)))
                    errors.Add($"{where}: falta '{k}'.");
            return result;
        }

        var @base = Section(obj["base"], "base", requireAll: true);
        var zones = new Dictionary<string, Dictionary<string, object>>();
        if (obj["zonas"] is JsonObject zs)
            foreach (var (zone, node) in zs)
            {
                if (TerritoryRules.Zones.All(z => z.Code != zone))
                    errors.Add($"zonas: '{zone}' no existe (usá {string.Join(", ", TerritoryRules.Zones.Select(z => z.Code))}).");
                zones[zone] = Section(node, $"zonas.{zone}", false);
            }
        var exceptions = new Dictionary<string, Dictionary<string, object>>();
        if (obj["excepciones"] is JsonObject ex)
            foreach (var (name, node) in ex)
            {
                if (name is not ("disputa" or "evento" or "guerra"))
                    errors.Add($"excepciones: '{name}' no existe (usá disputa, evento o guerra).");
                exceptions[name] = Section(node, $"excepciones.{name}", false);
            }
        var safe = Section(obj["zonaSegura"], "zonaSegura", false);
        var radius = 5f;
        if (obj["zonaSegura"]?["radio"] is JsonValue r)
        {
            if (r.TryGetValue<double>(out var rr) && rr is >= 0 and <= 30) radius = (float)rr;
            else errors.Add("zonaSegura.radio va de 0 a 30.");
        }
        return errors.Count == 0 ? (new ZoneRules(@base, zones, exceptions, safe, radius), errors) : (null, errors);
    }

    public ZonePermissions For(string zone, ZoneContext context)
    {
        var merged = new Dictionary<string, object>(_base);
        void Apply(Dictionary<string, object>? layer)
        {
            if (layer is null) return;
            foreach (var (k, v) in layer) merged[k] = v;
        }
        Apply(_zones.GetValueOrDefault(zone));
        if (context.Disputed) Apply(_exceptions.GetValueOrDefault("disputa"));
        if (context.Event) Apply(_exceptions.GetValueOrDefault("evento"));
        if (context.War) Apply(_exceptions.GetValueOrDefault("guerra"));
        if (context.Safe) Apply(_safe);
        bool B(string k) => merged.TryGetValue(k, out var v) && v is bool b && b;
        return new ZonePermissions(B("pvp"), B("armas"), B("cuerpoACuerpo"), B("saqueo"), B("robos"), B("persecuciones"),
            B("vehiculos"), B("recogida"), B("actividades"), B("perderMercaderiaAlMorir"),
            merged.TryGetValue("segundosBulto", out var s) && s is int n ? n : 300);
    }
}
