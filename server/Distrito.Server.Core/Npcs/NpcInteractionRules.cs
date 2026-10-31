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

    public static readonly NpcDefinition JazzClub = new(EconomyCatalog.JazzClubNpc, "Club de jazz", new Vector3(8f, 0, -7f));

    public static readonly IReadOnlyList<NpcDefinition> All = [Deposit, Workshop, ValentiOffice, CoffeeCorner, JazzClub];

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
            EconomyCatalog.CoffeeCornerNpc => BusinessDialog(EconomyCatalog.CoffeeStand, s, "Acá se instalan los puestos de café. La licencia la da la Oficina Valenti."),
            EconomyCatalog.JazzClubNpc => BusinessDialog(EconomyCatalog.JazzClub, s,
                "Un local vacío con escenario. Con licencia de la Oficina Valenti puede ser un club de jazz; las bebidas las vende el Almacén Moretti."),
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
        var urgent = EconomyCatalog.UrgentCoffeeOrder;
        if (s.ActiveContract(urgent.Code) is null)
        {
            options.Add(new NpcOption($"accept:{urgent.Code}",
                $"Aceptar: {urgent.Title} (paga {EconomyCatalog.FormatMoney(urgent.RewardCents)}, {urgent.TimeLimitSeconds / 60} minutos)"));
            text += $"\n\nTambién tengo un pedido urgente: {urgent.Quantity} bolsas de café para la Oficina Valenti, " +
                    $"en menos de {urgent.TimeLimitSeconds / 60} minutos. Si llegás tarde, quedo mal y me lo voy a acordar.";
        }
        else
        {
            text += $"\n\n¡Apurate con el pedido! Llevá {urgent.Quantity} bolsas de café a la Oficina Valenti.";
        }
        options.Add(CoffeeBagOption());
        options.Add(new($"buy:{EconomyCatalog.DrinksCrateItem}",
            $"Comprar un cajón de bebidas ({EconomyCatalog.FormatMoney(EconomyCatalog.JazzClub.ItemPriceCents)})"));
        return (text, options);
    }

    private static (string, List<NpcOption>) ValentiDialog(EconomySnapshot s)
    {
        var options = new List<NpcOption>();
        var offers = new List<string>();
        foreach (var type in EconomyCatalog.BusinessTypes.Values)
        {
            if (s.BusinessOf(type.Code) is not null)
                continue;
            var license = EconomyCatalog.FormatMoney(type.LicenseCents);
            offers.Add($"{type.Name.ToLowerInvariant()} sale {license} y {EconomyCatalog.FormatMoney(type.RentCents)} de alquiler por ciclo con ventas");
            options.Add(new($"license:{type.Code}", $"Comprar licencia de {type.Name.ToLowerInvariant()} ({license})"));
        }
        var text = offers.Count > 0
            ? $"Los Valenti otorgamos las licencias de comercio en Bellmont. Un {string.Join("; un ", offers)}."
            : "Tus licencias están en regla. Que los negocios prosperen… y el alquiler llegue a tiempo.";

        // Pedido urgente de los Moretti: se entrega acá.
        var urgent = EconomyCatalog.UrgentCoffeeOrder;
        if (s.ActiveContract(urgent.Code) is { } order)
        {
            if (s.QuantityOf(urgent.ItemCode) >= urgent.Quantity)
            {
                options.Add(new NpcOption($"deliver:{order.Id}",
                    $"Entregar {urgent.Quantity} bolsas de café de los Moretti (cobrar {EconomyCatalog.FormatMoney(urgent.RewardCents)})"));
                text += "\n\n¿El café de los Moretti? Justo a tiempo.";
            }
            else
            {
                text += $"\n\nEstoy esperando {urgent.Quantity} bolsas de café de los Moretti. ¿Las traés?";
            }
        }

        // Cobranza: trabajo de cuadrilla, solo para quien ya es conocido en el Taller Caruso.
        var collection = EconomyCatalog.CarusoCollection;
        var required = collection.RequiredReputation!;
        if (s.ActiveContract(collection.Code) is not null)
        {
            text += "\n\nAndá con tu cuadrilla al Taller Caruso y cobrá lo que nos deben.";
        }
        else if (s.ReputationWith(required.FamilyCode) >= required.Delta)
        {
            options.Add(new NpcOption($"accept:{collection.Code}",
                $"Aceptar: {collection.Title} (paga {EconomyCatalog.FormatMoney(collection.RewardCents)} a repartir)"));
            text += "\n\nLos Caruso nos deben dinero y a vos te conocen. Andá con alguien de tu cuadrilla y cobrales.";
        }
        else
        {
            text += $"\n\nTengo trabajos para gente conocida en el Taller Caruso (hace falta {required.Delta} de reputación con los Caruso).";
        }
        return (text, options);
    }

    private static (string, List<NpcOption>) BusinessDialog(BusinessType type, EconomySnapshot s, string noBusinessText)
    {
        if (s.BusinessOf(type.Code) is not { } b)
            return (noBusinessText, new List<NpcOption>());

        var m = EconomyCatalog.FormatMoney;
        var maxSales = type.BaseDemand + (b.HasHelper ? type.EmployeeExtraDemand : 0)
                       + Distrito.Shared.Progression.ProgressionRules.ExtraSalesPerCycle(s.ProgressOrEmpty.AdministrationLevel);
        var night = type.NightBonus ? " (+1 de noche, de 20 a 4 hs)" : "";
        var text = $"{b.Name} — {b.OperatingState}\n" +
                   $"Stock: {b.Stock} × {type.ItemName} · vende hasta {maxSales} por ciclo{night} a {m(type.SalePriceCents)}\n" +
                   $"Caja: {m(b.CashCents)} · Patrimonio: {m(b.EquityCents)}\n" +
                   $"Vendidos: {b.UnitsSold} · Facturación: {m(b.RevenueCents)} · Alquiler: {m(b.RentCents)} · Sueldos: {m(b.WagesCents)} · Ganancia: {m(b.ProfitCents)}\n" +
                   $"{char.ToUpperInvariant(type.EmployeeName[0])}{type.EmployeeName[1..]}: {(b.HasHelper ? "contratado" : "no")}";
        var options = new List<NpcOption>();
        var carrying = s.QuantityOf(type.ItemCode);
        if (carrying > 0)
            options.Add(new NpcOption($"restock:{type.Code}", $"Reponer ({carrying} × {type.ItemName} que llevás)"));
        if (b.CashCents > 0)
            options.Add(new NpcOption($"withdraw:{type.Code}", $"Retirar ganancias ({m(b.CashCents)})"));
        options.Add(b.HasHelper
            ? new NpcOption($"fire:{type.Code}", $"Despedir al {type.EmployeeName}")
            : new NpcOption($"hire:{type.Code}",
                $"Contratar {type.EmployeeName} ({m(type.EmployeeWageCents)} por ciclo abierto, vende {type.EmployeeExtraDemand} más)"));
        if (b.Stock == 0 && carrying == 0)
            text += $"\n\nSin mercadería no hay ventas: comprá {type.ItemName}s en el Almacén Moretti.";
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
        string text;

        if (active is null)
            text = "Estamos cortos de repuestos. Hablá con el Almacén Moretti si querés hacer una changa.";
        else if (s.QuantityOf(template.ItemCode) < template.Quantity)
            text = "¿Y los repuestos? Conseguilos en el Almacén Moretti y volvé.";
        else
        {
            options.Add(new NpcOption($"deliver:{active.Id}", $"Entregar la caja de repuestos (cobrar {EconomyCatalog.FormatMoney(template.RewardCents)})"));
            text = "¡Justo lo que necesitaba! ¿Me la dejás?";
        }

        var collection = EconomyCatalog.CarusoCollection;
        if (s.ActiveContract(collection.Code) is { } debt)
        {
            options.Add(new NpcOption($"deliver:{debt.Id}",
                $"Cobrar la deuda de los Valenti ({EconomyCatalog.FormatMoney(collection.RewardCents)}, con tu cuadrilla cerca)"));
            text += "\n\n¿Vienen de parte de los Valenti? …Está bien, está bien. Pero no vengas solo.";
        }
        return (text, options);
    }
}
