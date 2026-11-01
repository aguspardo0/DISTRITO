namespace Distrito.Shared.Progression;

/// <summary>Un nodo del árbol de habilidades. Value: efecto por rango (porcentaje, puntos o segundos, según el nodo).</summary>
public sealed record SkillNode(string Code, string Branch, int Tier, string Name, string Description, int MaxRank, double Value, string? Requires = null);

/// <summary>
/// Árbol de habilidades (aprobado por el equipo, 2026-11-01): un punto por nivel desde el 2, tres ramas, nodos con rangos
/// y requisitos. Convive con los oficios (Logística, Negociación, Administración), que siguen subiendo con la práctica.
/// Para sumar habilidades alcanza con agregar nodos acá (y su efecto donde corresponda).
/// </summary>
public static class SkillTree
{
    public const string Street = "calle";
    public const string Business = "negocios";
    public const string Logistics = "logistica";

    public static readonly IReadOnlyList<(string Code, string Name, string Blurb)> Branches =
    [
        (Street, "Calle", "Combate, aguante y asaltos."),
        (Business, "Negocios", "Precios, pagas y casino."),
        (Logistics, "Logística", "Carga, vehículos, plazos y pedidos."),
    ];

    /// <summary>Nivel de personaje que pide cada escalón del árbol.</summary>
    public static int TierLevel(int tier) => tier switch { <= 1 => 1, 2 => 5, _ => 10 };

    // ---- Calle ----
    public const string ToughSkin = "piel-dura";
    public const string IronFists = "punos-de-hierro";
    public const string SteadyHand = "pulso-firme";
    public const string Intimidation = "intimidacion";
    public const string QuickReload = "recarga-rapida";
    public const string ColdBlood = "sangre-fria";

    // ---- Negocios ----
    public const string Haggling = "regateo";
    public const string Salesman = "buen-vendedor";
    public const string Collector = "cobrador";
    public const string Contacts = "contactos";
    public const string HighStakes = "mesa-alta";

    // ---- Logística ----
    public const string OnTime = "puntual";
    public const string Driver = "conductor";
    public const string Stowage = "estiba";
    public const string SupplierEye = "ojo-de-proveedor";
    public const string Locksmith = "cerrajero";

    public static readonly IReadOnlyList<SkillNode> Nodes =
    [
        new(ToughSkin, Street, 1, "Piel dura", "+10 de vida máxima por rango.", 2, 10),
        new(IronFists, Street, 1, "Puños de hierro", "+5 de daño en las piñas por rango.", 2, 5),
        new(SteadyHand, Street, 2, "Pulso firme", "+10 % de daño con armas de fuego por rango.", 2, 10, ToughSkin),
        new(Intimidation, Street, 2, "Intimidación", "Los NPC se rinden 25 % más rápido cuando los amenazás.", 1, 25, IronFists),
        new(QuickReload, Street, 3, "Recarga rápida", "Recargás 30 % más rápido.", 1, 30, SteadyHand),
        new(ColdBlood, Street, 3, "Sangre fría", "Herido, tardás 15 s más en desangrarte.", 1, 15, SteadyHand),

        new(Haggling, Business, 1, "Regateo", "5 % menos en las compras a NPC por rango.", 2, 5),
        new(Salesman, Business, 1, "Buen vendedor", "Los NPC te pagan 10 % más cuando les vendés.", 1, 10),
        new(Collector, Business, 2, "Cobrador", "+5 % en la paga de los contratos por rango.", 2, 5, Haggling),
        new(Contacts, Business, 2, "Contactos", "5 % menos en los pedidos por mayor.", 1, 5, Salesman),
        new(HighStakes, Business, 3, "Mesa alta", "En el casino podés apostar hasta $100 (sin ser VIP).", 1, 10_000, Collector),

        new(OnTime, Logistics, 1, "Puntual", "+60 s en los contratos con plazo por rango.", 2, 60),
        new(Driver, Logistics, 1, "Buen conductor", "Tus vehículos van 10 % más rápido.", 1, 10),
        new(Stowage, Logistics, 2, "Estiba", "Cargás 15 % más en los vehículos por rango.", 2, 15, Driver),
        new(SupplierEye, Logistics, 2, "Ojo de proveedor", "Tus pedidos se preparan un ciclo antes.", 1, 1, OnTime),
        new(Locksmith, Logistics, 3, "Cerrajero", "Forzás la caja de un vehículo en 2 s menos.", 1, 2, Stowage),
    ];

    public static SkillNode? Find(string code) => Nodes.FirstOrDefault(n => n.Code == code);

    /// <summary>Puntos ganados: uno por nivel desde el 2.</summary>
    public static int PointsFor(int level) => Math.Max(0, level - 1);

    public static int Spent(IReadOnlyDictionary<string, int> ranks) => ranks.Sum(r => Find(r.Key) is null ? 0 : r.Value);

    /// <summary>Por qué no se puede aprender un rango más (null = se puede).</summary>
    public static string? WhyNot(string code, int level, IReadOnlyDictionary<string, int> ranks)
    {
        if (Find(code) is not { } node)
            return "Esa habilidad no existe.";
        var rank = ranks.GetValueOrDefault(code);
        if (rank >= node.MaxRank)
            return $"{node.Name} ya está al máximo.";
        if (level < TierLevel(node.Tier))
            return $"{node.Name} pide nivel {TierLevel(node.Tier)}.";
        if (node.Requires is { } req && ranks.GetValueOrDefault(req) <= 0)
            return $"Primero aprendé {Find(req)!.Name}.";
        if (Spent(ranks) >= PointsFor(level))
            return "No te quedan puntos de habilidad: subí de nivel.";
        return null;
    }

    /// <summary>Efecto total de un nodo con los rangos que tiene.</summary>
    public static double Effect(IReadOnlyDictionary<string, int>? ranks, string code) =>
        ranks is not null && Find(code) is { } node ? node.Value * Math.Min(ranks.GetValueOrDefault(code), node.MaxRank) : 0;
}

public sealed record LearnSkillRequest(Guid CharacterId, string Node, string RequestId);
