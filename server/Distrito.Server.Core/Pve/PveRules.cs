using System.Text.Json;
using System.Text.Json.Nodes;

namespace Distrito.Server.Core.Pve;

public sealed record LootLine(string Item, int Quantity);

public sealed record TruckRules(int EveryMinutes, string Family, string Supplier, string Item, int Min, int Max, float Speed,
    IReadOnlyList<(float X, float Z)> Route, float ThreatSeconds, int Reputation);

public sealed record CollectorRules(int EveryMinutes, string Family, string Account, long MinCents, long MaxCents, float Speed,
    IReadOnlyList<(float X, float Z)> Route, float ThreatSeconds, int Reputation);

public sealed record ContrabandRules(IReadOnlyList<int> Hours, int DurationMinutes, (float X, float Z) Place, int Guards, int GuardHealth,
    int GuardDamage, float GuardRange, float GuardCooldown, IReadOnlyList<IReadOnlyList<LootLine>> Loots);

public sealed record ShopRobberyRules(IReadOnlyList<string> Shops, float ThreatSeconds, long MinCents, long MaxCents, int CooldownMinutes, int Reputation);

/// <summary>
/// Asaltos a NPC (eventos PvE), editables en config/eventos-pve.json. Aprobado por el equipo 2026-11-01:
/// camión de reparto, cobrador con maletín, contrabando en Los Muelles con guardias y asalto a la caja de un local.
/// </summary>
public sealed record PveRules(TruckRules? Truck, CollectorRules? Collector, ContrabandRules? Contraband, ShopRobberyRules? ShopRobbery)
{
    /// <summary>Distancia máxima para amenazar con el arma (metros).</summary>
    public const float ThreatRange = 12f;
    /// <summary>Tolerancia de la puntería para amenazar (grados entre la mira y el NPC).</summary>
    public const float ThreatConeDegrees = 14f;

    public static (PveRules? Rules, List<string> Errors) Parse(string json)
    {
        var errors = new List<string>();
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException bad) { return (null, [$"El archivo no es JSON válido: {bad.Message}"]); }
        if (root is not JsonObject obj)
            return (null, ["El archivo tiene que ser un objeto { ... }."]);

        int Int(JsonObject o, string where, string key, int fallback) =>
            o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : Missing(where, key, fallback);
        long Long(JsonObject o, string where, string key, long fallback) =>
            o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? (long)d : Missing(where, key, fallback);
        float Float(JsonObject o, string where, string key, float fallback) =>
            o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? (float)d : Missing(where, key, fallback);
        string Text(JsonObject o, string where, string key) =>
            o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : Missing(where, key, "");
        T Missing<T>(string where, string key, T fallback)
        {
            errors.Add($"{where}: falta «{key}» o tiene un valor inválido.");
            return fallback;
        }
        List<(float, float)> Points(JsonNode? node, string where, string key)
        {
            var list = new List<(float, float)>();
            if (node is not JsonArray arr || arr.Count == 0) { errors.Add($"{where}: «{key}» tiene que ser una lista de puntos [x, z]."); return list; }
            foreach (var p in arr)
                if (p is JsonArray { Count: 2 } xz && xz[0] is JsonValue x && xz[1] is JsonValue z && x.TryGetValue<double>(out var xd) && z.TryGetValue<double>(out var zd))
                    list.Add(((float)xd, (float)zd));
                else
                    errors.Add($"{where}: un punto de «{key}» no es [x, z].");
            return list;
        }

        TruckRules? truck = null;
        if (obj["camion"] is JsonObject t)
            truck = new TruckRules(Int(t, "camion", "cadaMinutos", 20), Text(t, "camion", "familia"), Text(t, "camion", "proveedor"),
                Text(t, "camion", "item"), Int(t, "camion", "minimo", 10), Int(t, "camion", "maximo", 15), Float(t, "camion", "velocidad", 4f),
                Points(t["ruta"], "camion", "ruta"), Float(t, "camion", "segundosAmenaza", 3f), Int(t, "camion", "reputacion", -10));

        CollectorRules? collector = null;
        if (obj["cobrador"] is JsonObject c)
            collector = new CollectorRules(Int(c, "cobrador", "cadaMinutos", 15), Text(c, "cobrador", "familia"), Text(c, "cobrador", "cuenta"),
                Long(c, "cobrador", "centavosMinimo", 3000), Long(c, "cobrador", "centavosMaximo", 6000), Float(c, "cobrador", "velocidad", 1.6f),
                Points(c["ruta"], "cobrador", "ruta"), Float(c, "cobrador", "segundosAmenaza", 3f), Int(c, "cobrador", "reputacion", -10));

        ContrabandRules? contraband = null;
        if (obj["contrabando"] is JsonObject k)
        {
            var hours = (k["horas"] as JsonArray)?.Select(h => h is JsonValue hv && hv.TryGetValue<double>(out var hd) ? (int)hd : -1).ToList() ?? [];
            if (hours.Count == 0 || hours.Any(h => h is < 0 or > 23))
                errors.Add("contrabando: «horas» tiene que ser una lista de horas 0–23 (hora de Argentina).");
            var place = Points(new JsonArray(k["lugar"]?.DeepClone()), "contrabando", "lugar");
            var loots = new List<IReadOnlyList<LootLine>>();
            if (k["botines"] is JsonArray bs)
                foreach (var b in bs.OfType<JsonArray>())
                    loots.Add(b.OfType<JsonObject>().Select(l => new LootLine(Text(l, "contrabando.botines", "item"), Int(l, "contrabando.botines", "cantidad", 1))).ToList());
            if (loots.Count == 0)
                errors.Add("contrabando: «botines» tiene que tener al menos un botín.");
            contraband = new ContrabandRules(hours, Int(k, "contrabando", "duracionMinutos", 10), place.FirstOrDefault(), Int(k, "contrabando", "guardias", 2),
                Int(k, "contrabando", "vidaGuardia", 60), Int(k, "contrabando", "danoGuardia", 25), Float(k, "contrabando", "alcanceGuardia", 15f),
                Float(k, "contrabando", "cadenciaGuardia", 1.2f), loots);
        }

        ShopRobberyRules? shop = null;
        if (obj["cajaLocal"] is JsonObject s)
            shop = new ShopRobberyRules(
                (s["locales"] as JsonArray)?.Select(x => x is JsonValue xv && xv.TryGetValue<string>(out var code) ? code : "").Where(x => x.Length > 0).ToList() ?? [],
                Float(s, "cajaLocal", "segundosAmenaza", 8f), Long(s, "cajaLocal", "centavosMinimo", 2000), Long(s, "cajaLocal", "centavosMaximo", 4000),
                Int(s, "cajaLocal", "esperaMinutos", 30), Int(s, "cajaLocal", "reputacion", -10));

        if (truck is not null && (truck.Min < 1 || truck.Max < truck.Min)) errors.Add("camion: «minimo» y «maximo» inválidos.");
        if (truck is not null && truck.Route.Count < 2) errors.Add("camion: la ruta necesita al menos dos puntos.");
        if (collector is not null && collector.MaxCents < collector.MinCents) errors.Add("cobrador: el máximo es menor que el mínimo.");
        if (collector is not null && collector.Route.Count < 2) errors.Add("cobrador: la ruta necesita al menos dos puntos.");
        if (shop is not null && shop.MaxCents < shop.MinCents) errors.Add("cajaLocal: el máximo es menor que el mínimo.");
        return errors.Count > 0 ? (null, errors) : (new PveRules(truck, collector, contraband, shop), errors);
    }

    /// <summary>¿El arma apunta a ese punto? (desde los ojos, dentro del cono y del alcance)</summary>
    public static bool Aims(System.Numerics.Vector3 eye, float yaw, float pitch, System.Numerics.Vector3 target, float range = ThreatRange)
    {
        var to = target - eye;
        var dist = to.Length();
        if (dist > range || dist < 0.1f)
            return false;
        var dir = new System.Numerics.Vector3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
        var cos = System.Numerics.Vector3.Dot(dir, to / dist);
        // Cono un poco más generoso de cerca (el cuerpo ocupa más ángulo).
        var cone = ThreatConeDegrees + MathF.Max(0, 6f - dist) * 3f;
        return cos >= MathF.Cos(cone * MathF.PI / 180f);
    }

    /// <summary>Turno de un evento que sale cada N minutos (para no repetirlo en el mismo turno).</summary>
    public static long SlotOf(DateTimeOffset now, int everyMinutes) => now.ToUnixTimeSeconds() / 60 / Math.Max(1, everyMinutes);

    /// <summary>Turno del contrabando: fecha y hora de Argentina si es una de las horas configuradas; si no, null.</summary>
    public static string? ContrabandSlot(DateTimeOffset now, IReadOnlyList<int> hours)
    {
        var local = now.ToOffset(TimeSpan.FromHours(-3));
        return hours.Contains(local.Hour) ? $"{local:yyyy-MM-dd}:{local.Hour}" : null;
    }
}
