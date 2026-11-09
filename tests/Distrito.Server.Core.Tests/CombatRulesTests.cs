using System.Numerics;
using Distrito.Server.Core.Combat;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Tests;

public class CombatRulesTests
{
    private static readonly Vector3 Open = new(3, 0, -1); // lejos de los NPC
    private static CombatState Disputed(string zone) => new(new HashSet<string> { zone }, new HashSet<string>());

    [Fact]
    public void El_golpe_llega_solo_de_frente_y_cerca()
    {
        var target = Open + new Vector3(0, 0, -1.5f); // adelante (−Z) con yaw 0
        Assert.True(CombatRules.InReach(Open, 0, target));
        Assert.False(CombatRules.InReach(Open, MathF.PI, target)); // de espaldas
        Assert.False(CombatRules.InReach(Open, 0, Open + new Vector3(0, 0, -2.5f))); // lejos
    }

    [Fact]
    public void Zona_segura_y_niveles()
    {
        var nearShop = NpcInteractionRules.Deposit.Position + new Vector3(1.5f, 0, 1f);
        Assert.True(CombatRules.InSafeZone(nearShop));
        Assert.NotNull(CombatRules.WhyNot(nearShop, Open, null, null, CombatState.None));
        Assert.Null(CombatRules.WhyNot(Open, Open, null, null, CombatState.None)); // partida local sin cuentas

        var zone = CombatRules.ZoneAt(Open);
        Assert.NotNull(CombatRules.WhyNot(Open, Open, 2, 5, Disputed(zone)));
        Assert.NotNull(CombatRules.WhyNot(Open, Open, 5, 2, Disputed(zone)));
        Assert.NotNull(CombatRules.WhyNot(Open, Open, 5, 5, CombatState.None)); // zona tranquila
        Assert.Null(CombatRules.WhyNot(Open, Open, 5, 5, Disputed(zone)));
        Assert.Null(CombatRules.WhyNot(Open, Open, 5, 5, new CombatState(new HashSet<string>(), new HashSet<string> { zone })));
        Assert.Null(CombatRules.WhyNot(Open, Open, 5, 5, CombatState.None, atWar: true));
    }

    [Fact]
    public void Cada_punto_tiene_zona()
    {
        Assert.Equal(TerritoryRules.Warehouse, CombatRules.ZoneAt(new Vector3(-9, 0, -9)));
        Assert.Equal(TerritoryRules.Workshops, CombatRules.ZoneAt(new Vector3(11, 0, 4)));
    }
}
