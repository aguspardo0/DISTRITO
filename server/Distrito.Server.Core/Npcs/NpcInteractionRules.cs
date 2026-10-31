using System.Numerics;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Npcs;

public sealed record NpcOption(string Id, string Label);

public sealed record NpcDialog(string NpcCode, string Title, string Text, IReadOnlyList<NpcOption> Options);

public enum NpcActionKind { AcceptContract, Buy, Deliver, BuyLicense, Restock, Withdraw, HireHelper, FireHelper }

public sealed record NpcAction(NpcActionKind Kind, string Code, Guid? ContractId = null);

/// <summary>NPC fijo del barrio de prueba.</summary>
public sealed record NpcDefinition(string Code, string Name, Vector3 Position);

/// <summary>
/// Qué ofrece cada NPC según el estado económico del jugador, y qué opciones son válidas.
/// El servidor usa esto para armar el diálogo y para verificar que la opción elegida
/// realmente estaba disponible (el cliente no puede inventar opciones).
/// </summary>
public static class NpcInteractionRules
{
    /// <summary>Distancia máxima (metros) entre el jugador y el NPC para interactuar. La verifica el servidor.</summary>
    public const float InteractionRange = 3.5f;

    public static readonly NpcDefinition Deposit = new(EconomyCatalog.DepositNpc, "Almacén Moretti", new Vector3(-8.5f, 0, -8.5f));
    public static readonly NpcDefinition Workshop = new(EconomyCatalog.WorkshopNpc, "Taller Caruso", new Vector3(10f, 0, 3f));

    public static readonly NpcDefinition ValentiOffice = new(EconomyCatalog.ValentiOfficeNpc, "Oficina Valenti", new Vector3(-11.5f, 0, -1.5f));
    public static readonly NpcDefinition CoffeeCorner = new(EconomyCatalog.CoffeeCornerNpc, "Esquina del café", new Vector3(-3f, 0, 8f));

    public static readonly IReadOnlyList<NpcDefinition> All = [Deposit, Workshop, ValentiOffice, CoffeeCorner];

    public static NpcDefinition? Find(string code) => All.FirstOrDefault(n => n.Code == code);

    public static bool InRange(Vector3 player, NpcDefinition npc)
    {
        var flat = new Vector2(player.X - npc.Position.X, player.Z - npc.Position.Z);
        return flat.Length() <= InteractionRange && MathF.Abs(player.Y - npc.Position.Y) < 3f;
    }

    public static NpcDialog DialogFor(string npcCode, EconomySnapshot s, string? prefix = null)
    {
        var npc = Find(npcCode) ?? throw new ArgumentException($"NPC desconocido: {npcCode}", nameof(npcCode));
        var (text, options) = npcCode switch
        {
            EconomyCatalog.DepositNpc => DepositDialog(s),
            EconomyCatalog.WorkshopNpc => WorkshopDialog(s),
            EconomyCatalog.ValentiOfficeNpc => ValentiDialog(s),
            EconomyCatalog.CoffeeCornerNpc => CornerDialog(s),
            _ => ("…", new List<NpcOption>()),
        };
        return new NpcDialog(npc.Code, npc.Name, prefix is null ? text : $"{prefix}\n\n{text}", options);
    }

    /// <summary>Traduce la opción elegida a una acción, solo si está disponible ahora. Si no, null.</summary>
    public static NpcAction? Resolve(string npcCode, string optionId, EconomySnapshot s)
    {
        if (Find(npcCode) is null || DialogFor(npcCode, s).Options.All(o => o.Id != optionId))
            return null;

        var parts = optionId.Split(':', 2);
        return parts[0] switch
        {
            "accept" => new NpcAction(NpcActionKind.AcceptContract, parts[1]),
            "buy" => new NpcAction(NpcActionKind.Buy, parts[1]),
            "license" => new NpcAction(NpcActionKind.BuyLicense, parts[1]),
            "restock" => new NpcAction(NpcActionKind.Restock, parts[1]),
            "withdraw" => new NpcAction(NpcActionKind.Withdraw, parts[1]),
            "hire" => new NpcAction(NpcActionKind.HireHelper, parts[1]),
            "fire" => new NpcAction(NpcActionKind.FireHelper, parts[1]),
            "deliver" when Guid.TryParse(parts[1], out var id) =>
                new NpcAction(NpcActionKind.Deliver, s.ActiveContracts.First(c => c.Id == id).TemplateCode, id),
            _ => null,
        };
    }

    private static NpcOption CoffeeBagOption() =>
        new($"buy:{EconomyCatalog.CoffeeBagItem}", $"Comprar una bolsa de café ({EconomyCatalog.FormatMoney(EconomyCatalog.CoffeeBagPriceCents)})");

    private static (string, List<NpcOption>) DepositDialog(EconomySnapshot s)
    {
        var (text, options) = DepositContractDialog(s);
        options.Add(CoffeeBagOption());
        return (text, options);
    }

    private static (string, List<NpcOption>) ValentiDialog(EconomySnapshot s)
    {
        var license = EconomyCatalog.FormatMoney(EconomyCatalog.CoffeeStandLicenseCents);
        if (s.Business is null)
            return ($"Los Valenti otorgamos las licencias de comercio en Bellmont. Un puesto de café en la esquina sale {license}, " +
                    $"y nos pagás {EconomyCatalog.FormatMoney(EconomyCatalog.CoffeeStandRentCents)} de alquiler por cada ciclo con ventas.",
                new List<NpcOption> { new("license:puesto-cafe", $"Comprar licencia de puesto de café ({license})") });
        return ("Tu licencia está en regla. Que el negocio prospere… y el alquiler llegue a tiempo.", new List<NpcOption>());
    }

    private static (string, List<NpcOption>) CornerDialog(EconomySnapshot s)
    {
        if (s.Business is not { } b)
            return ("Acá se instalan los puestos de café. La licencia la da la Oficina Valenti.", new List<NpcOption>());

        var m = EconomyCatalog.FormatMoney;
        var text = $"{b.Name} — {b.OperatingState}\n" +
                   $"Stock: {b.Stock} bolsa(s) · vende hasta {EconomyCatalog.CoffeeDemandPerCycle + (b.HasHelper ? EconomyCatalog.CoffeeHelperExtraDemand : 0)} por ciclo a {m(EconomyCatalog.CoffeeSalePriceCents)}\n" +
                   $"Caja: {m(b.CashCents)} · Patrimonio: {m(b.EquityCents)}\n" +
                   $"Vendidos: {b.UnitsSold} · Facturación: {m(b.RevenueCents)} · Alquiler: {m(b.RentCents)} · Sueldos: {m(b.WagesCents)} · Ganancia: {m(b.ProfitCents)}\n" +
                   (b.HasHelper ? "Ayudante: contratado" : "Ayudante: no");
        var options = new List<NpcOption>();
        var carrying = s.QuantityOf(EconomyCatalog.CoffeeBagItem);
        if (carrying > 0)
            options.Add(new NpcOption("restock:puesto-cafe", $"Reponer ({carrying} bolsa(s) que llevás)"));
        if (b.CashCents > 0)
            options.Add(new NpcOption("withdraw:puesto-cafe", $"Retirar ganancias ({m(b.CashCents)})"));
        options.Add(b.HasHelper
            ? new NpcOption("fire:puesto-cafe", "Despedir al ayudante")
            : new NpcOption("hire:puesto-cafe",
                $"Contratar ayudante ({m(EconomyCatalog.CoffeeHelperWageCents)} por ciclo abierto, vende hasta {EconomyCatalog.CoffeeDemandPerCycle + EconomyCatalog.CoffeeHelperExtraDemand})"));
        if (b.Stock == 0 && carrying == 0)
            text += "\n\nSin café no hay ventas: comprá bolsas en el Almacén Moretti.";
        return (text, options);
    }

    private static (string, List<NpcOption>) DepositContractDialog(EconomySnapshot s)
    {
        var template = EconomyCatalog.SparePartsDelivery;
        var active = s.ActiveContract(template.Code);
        var carrying = s.QuantityOf(template.ItemCode);
        var price = EconomyCatalog.FormatMoney(EconomyCatalog.SparePartsPriceCents);
        var reward = EconomyCatalog.FormatMoney(template.RewardCents);
        var options = new List<NpcOption>();

        if (active is null)
        {
            options.Add(new NpcOption($"accept:{template.Code}", $"Aceptar: {template.Title} (paga {reward})"));
            if (carrying == 0)
                options.Add(new NpcOption($"buy:{template.ItemCode}", $"Comprar una caja de repuestos ({price})"));
            return ($"El Taller Caruso necesita repuestos. Si se los llevás, te pagan {reward}. La caja te la vendo a {price}.", options);
        }

        if (carrying < template.Quantity)
        {
            options.Add(new NpcOption($"buy:{template.ItemCode}", $"Comprar una caja de repuestos ({price})"));
            return ("Para cumplir el contrato vas a necesitar una caja. ¿Te la llevás?", options);
        }

        return ("Ya tenés la caja. Llevásela al Taller Caruso, del otro lado de la calle.", options);
    }

    private static (string, List<NpcOption>) WorkshopDialog(EconomySnapshot s)
    {
        var template = EconomyCatalog.SparePartsDelivery;
        var active = s.ActiveContract(template.Code);
        var options = new List<NpcOption>();

        if (active is null)
            return ("Estamos cortos de repuestos. Hablá con el Almacén Moretti si querés hacer una changa.", options);

        if (s.QuantityOf(template.ItemCode) < template.Quantity)
            return ("¿Y los repuestos? Conseguilos en el Almacén Moretti y volvé.", options);

        options.Add(new NpcOption($"deliver:{active.Id}", $"Entregar la caja de repuestos (cobrar {EconomyCatalog.FormatMoney(template.RewardCents)})"));
        return ("¡Justo lo que necesitaba! ¿Me la dejás?", options);
    }
}
