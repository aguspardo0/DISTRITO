using System.Numerics;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Tests;

public class NpcInteractionRulesTests
{
    private static readonly ContractTemplate Contract = EconomyCatalog.SparePartsDelivery;

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
        Assert.DoesNotContain(dialog.Options, o => o.Id.StartsWith("accept:"));
    }

    [Fact]
    public void El_taller_solo_ofrece_entregar_con_contrato_y_caja()
    {
        Assert.Empty(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State()).Options);
        Assert.Empty(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State(contract: true)).Options);
        Assert.Empty(NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State(boxes: 1)).Options);

        var dialog = NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, State(contract: true, boxes: 1));
        Assert.StartsWith("deliver:", Assert.Single(dialog.Options).Id);
    }

    [Fact]
    public void Resolver_traduce_opciones_disponibles_a_acciones()
    {
        var state = State(contract: true, boxes: 1);
        var deliverId = NpcInteractionRules.DialogFor(EconomyCatalog.WorkshopNpc, state).Options[0].Id;

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
        Assert.Empty(NpcInteractionRules.DialogFor(EconomyCatalog.ValentiOfficeNpc, WithBusiness()).Options);
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
