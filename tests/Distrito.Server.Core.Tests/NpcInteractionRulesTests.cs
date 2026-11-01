using System.Numerics;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Tests;

public class NpcInteractionRulesTests
{
    private static readonly ContractTemplate Contract = EconomyCatalog.SparePartsDelivery;

    /// <summary>Opciones sin la armería del Taller Caruso (compras).</summary>
    private static List<NpcOption> NonShop(NpcDialog d) => d.Options.Where(o => !o.Id.StartsWith("buy:") && !o.Id.StartsWith("ui:") && !o.Id.StartsWith("sell")).ToList();

    [Fact]
    public void La_armeria_vende_segun_el_nivel_y_siempre_municion()
    {
        var low = NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State());
        Assert.DoesNotContain(low.Options, o => o.Id == "buy:pistola");
        Assert.Contains(low.Options, o => o.Id == "buy:caja-balas-pistola");
        var lvl5 = new EconomySnapshot(2000, [], [], 0, Progress: new Distrito.Shared.Progression.ProgressView(1000, 0, 0, 0));
        var opts = NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, lvl5).Options.Select(o => o.Id).ToList();
        Assert.Contains("buy:pistola", opts);
        Assert.Contains("buy:escopeta", opts);
        Assert.DoesNotContain("buy:thompson", opts);
        Assert.Equal(0, Weapons.Shotgun.DamageAt(12f)); // a 12 m ya no hace daño
        Assert.Equal(60, Weapons.Shotgun.DamageAt(5f));
        Assert.Equal(30, Weapons.Shotgun.DamageAt(9f));
        Assert.Equal(0, Weapons.Pistol.DamageAt(21f));
    }

    private static EconomySnapshot State(bool contract = false, int boxes = 0, long balance = 2000) => new(
        balance,
        boxes > 0 ? [new InventoryItem(Contract.ItemCode, "Caja de repuestos", boxes)] : [],
        contract ? [new ContractView(Guid.Parse("11111111-1111-1111-1111-111111111111"), Contract.Code, "active")] : [],
        0);

    [Fact]
    public void Deposito_ofrece_el_contrato_a_quien_no_lo_tiene()
    {
        var dialog = NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, State());
        Assert.Contains(dialog.Options, o => o.Id == $"accept:{Contract.Code}");
    }

    [Fact]
    public void Con_el_contrato_y_sin_caja_el_deposito_ofrece_comprar()
    {
        var dialog = NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, State(contract: true));
        Assert.Contains(dialog.Options, o => o.Id == $"buy:{Contract.ItemCode}");
        Assert.DoesNotContain(dialog.Options, o => o.Id == $"accept:{Contract.Code}");
    }

    [Fact]
    public void El_taller_solo_ofrece_entregar_con_contrato_y_caja()
    {
        Assert.Empty(NonShop(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State())));
        Assert.Empty(NonShop(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State(contract: true))));
        Assert.Empty(NonShop(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State(boxes: 1))));

        var dialog = NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State(contract: true, boxes: 1));
        Assert.StartsWith("deliver:", Assert.Single(NonShop(dialog)).Id);
    }

    [Fact]
    public void Resolver_traduce_opciones_disponibles_a_acciones()
    {
        var state = State(contract: true, boxes: 1);
        var deliverId = NonShop(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, state))[0].Id;

        var action = NpcInteractionRules.Resolve(EconomyCatalog.WorkshopNpc, deliverId, state);

        Assert.NotNull(action);
        Assert.Equal(NpcActionKind.Deliver, action.Kind);
        Assert.Equal(state.ActiveContracts[0].Id, action.ContractId);
    }

    [Theory]
    [InlineData("deliver:11111111-1111-1111-1111-111111111111")] // entregar sin caja
    [InlineData("buy:caja-de-oro")]                               // mercadería inventada
    [InlineData("pagame:1000000")]                                // acción inventada
    public void Resolver_rechaza_opciones_que_el_npc_no_ofrecio(string optionId)
    {
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.WorkshopNpc, optionId, State(contract: true)));
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.DepositNpc, optionId, State(contract: true)));
    }

    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static EconomySnapshot Contracts(string? active = null, int bags = 0, int carusoRep = 0) => new(
        2000,
        bags > 0 ? [new InventoryItem(EconomyCatalog.CoffeeBagItem, "Bolsa de café", bags)] : [],
        active is null ? [] : [new ContractView(OrderId, active, "active", DateTimeOffset.UtcNow.AddMinutes(2))],
        0,
        [new ReputationView(EconomyCatalog.Caruso, "Familia Caruso", carusoRep)]);

    [Fact]
    public void Pedido_urgente_se_acepta_en_Moretti_y_se_entrega_en_Valenti_con_las_dos_bolsas()
    {
        var urgent = EconomyCatalog.UrgentCoffeeOrder;
        Assert.Contains(NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, Contracts()).Options, o => o.Id == $"accept:{urgent.Code}");
        Assert.DoesNotContain(NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, Contracts(urgent.Code)).Options, o => o.Id == $"accept:{urgent.Code}");

        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"deliver:{OrderId}", Contracts(urgent.Code, bags: 1)));
        var action = NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"deliver:{OrderId}", Contracts(urgent.Code, bags: 2));
        Assert.Equal(NpcActionKind.Deliver, action!.Kind);
        Assert.Equal(urgent.Code, action.Code);
    }

    [Fact]
    public void Cobranza_requiere_reputacion_con_los_Caruso_y_se_cobra_en_el_taller()
    {
        var collection = EconomyCatalog.CarusoCollection;
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"accept:{collection.Code}", Contracts(carusoRep: 4)));
        Assert.NotNull(NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"accept:{collection.Code}", Contracts(carusoRep: 5)));

        Assert.Empty(NonShop(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, Contracts(carusoRep: 5))));
        var deliver = NpcInteractionRules.Resolve(EconomyCatalog.WorkshopNpc, $"deliver:{OrderId}", Contracts(collection.Code, carusoRep: 5));
        Assert.Equal(collection.Code, deliver!.Code);
    }

    private static EconomySnapshot Leveled(long xp, int logistics = 0, int negotiation = 0, int caruso = 0, string? active = null, int crates = 0) => new(
        2000,
        crates > 0 ? [new InventoryItem(EconomyCatalog.DrinksCrateItem, "Cajón de bebidas", crates)] : [],
        active is null ? [] : [new ContractView(OrderId, active, "active")],
        0,
        [new ReputationView(EconomyCatalog.Caruso, "Familia Caruso", caruso)],
        Progress: new Distrito.Shared.Progression.ProgressView(xp, logistics, negotiation, 0));

    [Fact]
    public void Los_contratos_por_nivel_se_ofrecen_solo_con_los_requisitos()
    {
        var party = EconomyCatalog.PartyDelivery;
        var low = NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, Leveled(xp: 0));
        Assert.DoesNotContain(low.Options, o => o.Id == $"accept:{party.Code}");
        Assert.Contains("nivel 3", low.Text);
        // Nivel 3 (300 de experiencia) y Logística 2 (5 prácticas).
        Assert.NotNull(NpcInteractionRules.Resolve(EconomyCatalog.DepositNpc, $"accept:{party.Code}", Leveled(xp: 300, logistics: 5)));
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.DepositNpc, $"accept:{party.Code}", Leveled(xp: 300)));

        // Se entrega en el Taller Caruso con los 3 cajones.
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.WorkshopNpc, $"deliver:{OrderId}", Leveled(300, 5, active: party.Code, crates: 2)));
        Assert.Equal(party.Code, NpcInteractionRules.Resolve(EconomyCatalog.WorkshopNpc, $"deliver:{OrderId}", Leveled(300, 5, active: party.Code, crates: 3))!.Code);

        // Revisar las cuentas: nivel 5 y Negociación 3; se hace en el Almacén Moretti sin mercadería.
        var audit = EconomyCatalog.AuditBooks;
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"accept:{audit.Code}", Leveled(xp: 1000, negotiation: 14)));
        Assert.NotNull(NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"accept:{audit.Code}", Leveled(xp: 1000, negotiation: 15)));
        Assert.NotNull(NpcInteractionRules.Resolve(EconomyCatalog.DepositNpc, $"deliver:{OrderId}", Leveled(1000, negotiation: 15, active: audit.Code)));

        // Cobranza grande: nivel 8 y 15 con los Caruso.
        var big = EconomyCatalog.BigCollection;
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"accept:{big.Code}", Leveled(xp: 2800, caruso: 14)));
        Assert.NotNull(NpcInteractionRules.Resolve(EconomyCatalog.ValentiOfficeNpc, $"accept:{big.Code}", Leveled(xp: 2800, caruso: 15)));
        Assert.Equal(2, big.MatesNeeded);
    }

    [Fact]
    public void Resolver_rechaza_npcs_desconocidos()
    {
        Assert.Null(NpcInteractionRules.Resolve("npc:inventado", $"accept:{Contract.Code}", State()));
    }

    private static EconomySnapshot WithBusiness(int stock = 0, long cash = 0, int carriedBags = 0, bool helper = false) => new(
        2000,
        carriedBags > 0 ? [new InventoryItem(EconomyCatalog.CoffeeBagItem, "Bolsa de café", carriedBags)] : [],
        [], 0, null,
        new BusinessView(Guid.NewGuid(), "Puesto de prueba", stock, cash, 0, 0, 0, 0, helper));

    [Fact]
    public void Valenti_vende_licencia_solo_a_quien_no_tiene_negocio()
    {
        Assert.Contains(NpcInteractionRules.DialogFor(EconomyCatalog.ValentiOfficeNpc, State()).Options, o => o.Id == "license:puesto-cafe");
        // Con el puesto de café, la oficina solo ofrece la licencia del club de jazz.
        Assert.Equal(new[] { $"license:{EconomyCatalog.JazzClubKind}" },
            NpcInteractionRules.DialogFor(EconomyCatalog.ValentiOfficeNpc, WithBusiness()).Options.Select(o => o.Id));
    }

    [Fact]
    public void La_esquina_ofrece_reponer_y_retirar_segun_corresponda()
    {
        Assert.Empty(NpcInteractionRules.DialogFor(EconomyCatalog.CoffeeCornerNpc, State()).Options);
        Assert.Equal(new[] { "hire:puesto-cafe" },
            NpcInteractionRules.DialogFor(EconomyCatalog.CoffeeCornerNpc, WithBusiness()).Options.Select(o => o.Id));

        var both = NpcInteractionRules.DialogFor(EconomyCatalog.CoffeeCornerNpc, WithBusiness(cash: 400, carriedBags: 2)).Options;
        Assert.Contains(both, o => o.Id == "restock:puesto-cafe");
        Assert.Contains(both, o => o.Id == "withdraw:puesto-cafe");

        Assert.Equal(NpcActionKind.Withdraw,
            NpcInteractionRules.Resolve(EconomyCatalog.CoffeeCornerNpc, "withdraw:puesto-cafe", WithBusiness(cash: 400))!.Kind);
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.CoffeeCornerNpc, "withdraw:puesto-cafe", WithBusiness(cash: 0)));
    }

    [Fact]
    public void La_esquina_permite_contratar_o_despedir_al_ayudante()
    {
        Assert.Equal(NpcActionKind.HireHelper,
            NpcInteractionRules.Resolve(EconomyCatalog.CoffeeCornerNpc, "hire:puesto-cafe", WithBusiness())!.Kind);
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.CoffeeCornerNpc, "fire:puesto-cafe", WithBusiness()));

        var withHelper = WithBusiness(helper: true);
        Assert.Equal(NpcActionKind.FireHelper,
            NpcInteractionRules.Resolve(EconomyCatalog.CoffeeCornerNpc, "fire:puesto-cafe", withHelper)!.Kind);
        Assert.Null(NpcInteractionRules.Resolve(EconomyCatalog.CoffeeCornerNpc, "hire:puesto-cafe", withHelper));
        Assert.Contains("vende hasta 3", NpcInteractionRules.DialogFor(EconomyCatalog.CoffeeCornerNpc, withHelper).Text);
    }

    [Fact]
    public void Club_de_jazz_se_administra_en_su_local_con_musico_opcional()
    {
        var club = new BusinessView(Guid.NewGuid(), "Club de prueba", 0, 0, 0, 0, 0, 0, Kind: EconomyCatalog.JazzClubKind);
        var crates = new EconomySnapshot(2000, [new InventoryItem(EconomyCatalog.DrinksCrateItem, "Cajón de bebidas", 3)], [], 0,
            Businesses: [club]);

        Assert.Empty(NpcInteractionRules.DialogFor(EconomyCatalog.JazzClubNpc, State()).Options);
        var options = NpcInteractionRules.DialogFor(EconomyCatalog.JazzClubNpc, crates).Options.Select(o => o.Id).ToList();
        Assert.Contains($"restock:{EconomyCatalog.JazzClubKind}", options);
        Assert.Contains($"hire:{EconomyCatalog.JazzClubKind}", options);
        Assert.Equal(EconomyCatalog.JazzClubKind,
            NpcInteractionRules.Resolve(EconomyCatalog.JazzClubNpc, $"restock:{EconomyCatalog.JazzClubKind}", crates)!.Code);
        // El club no aparece en la esquina del café.
        Assert.Empty(NpcInteractionRules.DialogFor(EconomyCatalog.CoffeeCornerNpc, crates).Options);
        Assert.Contains(NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, State()).Options, o => o.Id == $"buy:{EconomyCatalog.DrinksCrateItem}");
    }

    [Theory]
    [InlineData("2026-10-31T23:30:00Z", true)]  // 20:30 en Argentina
    [InlineData("2026-11-01T06:59:00Z", true)]  // 03:59
    [InlineData("2026-11-01T07:00:00Z", false)] // 04:00
    [InlineData("2026-10-31T15:00:00Z", false)] // 12:00
    public void Noche_de_20_a_4_hora_argentina(string utc, bool night) =>
        Assert.Equal(night, EconomyCatalog.IsNight(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void El_almacen_siempre_vende_bolsas_de_cafe()
    {
        Assert.Contains(NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, State()).Options, o => o.Id == $"buy:{EconomyCatalog.CoffeeBagItem}");
        Assert.Contains(NpcInteractionRules.DialogFor(EconomyCatalog.DepositNpc, State(contract: true, boxes: 1)).Options, o => o.Id == $"buy:{EconomyCatalog.CoffeeBagItem}");
    }

    [Fact]
    public void Rango_de_interaccion()
    {
        var npc = NpcInteractionRules.Deposit;
        Assert.True(NpcInteractionRules.InRange(npc.Position + new Vector3(2, 1, 0), npc));
        Assert.False(NpcInteractionRules.InRange(npc.Position + new Vector3(5, 0, 0), npc));
    }

    [Fact]
    public void Los_npcs_no_quedan_encima_de_los_puntos_de_aparicion()
    {
        foreach (var npc in NpcInteractionRules.All)
            for (var slot = 0; slot < 16; slot++)
                Assert.True(Vector3.Distance(npc.Position, Sessions.ZoneSessions.SpawnPointFor(slot)) > 2f);
    }
}
