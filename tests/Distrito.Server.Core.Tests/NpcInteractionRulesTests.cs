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
        var option = Assert.Single(dialog.Options);
        Assert.Equal($"buy:{Contract.ItemCode}", option.Id);
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
