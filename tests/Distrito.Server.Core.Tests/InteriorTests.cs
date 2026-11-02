using System.Numerics;
using Distrito.Server.Core.Combat;
using Distrito.Server.Core.Npcs;
using Distrito.Server.Core.World;
using Distrito.Shared.Networking;

namespace Distrito.Server.Core.Tests;

/// <summary>Interiores del Mercado: puertas, salones lejos de la ciudad, mostradores y reglas adentro.</summary>
public class InteriorTests
{
    [Fact]
    public void Los_salones_estan_lejos_de_la_ciudad_y_entre_si()
    {
        Assert.True(InteriorLayout.All.Count >= 7);
        Assert.Equal(InteriorLayout.All.Count, InteriorLayout.All.Select(p => p.Code).Distinct().Count());
        Assert.Equal(InteriorLayout.All.Count, InteriorLayout.All.Select(p => p.Slot).Distinct().Count());
        foreach (var p in InteriorLayout.All)
        {
            Assert.True(p.Origin.X - p.Width > CityLayout.MaxX + NetworkDefaults.InterestRadius * 2, $"{p.Code} muy cerca de la ciudad");
            foreach (var q in InteriorLayout.All.Where(q => q != p))
                Assert.True(Vector2.Distance(p.Origin, q.Origin) > NetworkDefaults.InterestRadius * 2 + p.Width, $"{p.Code} y {q.Code} se ven entre sí");
        }
    }

    [Fact]
    public void Entrar_y_salir_deja_en_lugares_caminables_y_las_puertas_no_se_pisan()
    {
        foreach (var p in InteriorLayout.All)
        {
            Assert.True(CityLayout.IsWalkable(p.Door.X, p.Door.Y), $"puerta de {p.Code} fuera de la ciudad");
            Assert.True(CityLayout.IsWalkable(p.ExitPoint.X, p.ExitPoint.Y, 0.5f), $"salida de {p.Code}");
            Assert.True(CityLayout.IsWalkable(p.EntryPoint.X, p.EntryPoint.Y, 0.5f), $"entrada de {p.Code}");
            Assert.Same(p, InteriorLayout.At(p.EntryPoint.X, p.EntryPoint.Y));
            Assert.Null(InteriorLayout.At(p.ExitPoint.X, p.ExitPoint.Y));
            Assert.Equal((p, false), InteriorLayout.DoorNear(p.Door.X + p.Outward.X, p.Door.Y + p.Outward.Y));
            Assert.Equal((p, true), InteriorLayout.DoorNear(p.EntryPoint.X, p.EntryPoint.Y));
            // Salir no deja parado sobre otra puerta (no se rebota de un local a otro).
            var after = InteriorLayout.DoorNear(p.ExitPoint.X, p.ExitPoint.Y);
            Assert.True(after is null || after.Value.Plan.Code == p.Code);
            foreach (var q in InteriorLayout.All.Where(q => q != p))
                Assert.True(Vector2.Distance(p.Door, q.Door) > InteriorLayout.DoorRange * 2, $"{p.Code} y {q.Code} tienen las puertas pegadas");
        }
    }

    [Fact]
    public void Los_muebles_y_objetos_estan_adentro_del_salon()
    {
        foreach (var p in InteriorLayout.All)
        {
            foreach (var item in p.Items)
                Assert.True(MathF.Abs(item.X) <= p.Width / 2 && MathF.Abs(item.Z) <= p.Depth / 2, $"{p.Code}: {item.Type} afuera");
            foreach (var prop in p.Props)
                Assert.True(MathF.Abs(prop.X) < p.Width / 2 && MathF.Abs(prop.Z) < p.Depth / 2, $"{p.Code}: {prop.Type} afuera");
            // Nada tapa la puerta: un metro y medio libre del lado de adentro.
            Assert.DoesNotContain(p.Items, i => i.Type != "cartel" && i.Type != "lampara-colgante" && MathF.Abs(i.X - p.DoorX) < 1.2f && i.Z > p.Depth / 2 - 1.6f);
        }
        foreach (var prop in InteriorLayout.MarketProps)
            Assert.True(CityLayout.IsWalkable(prop.X, prop.Z), $"objeto de la vereda fuera de la ciudad: {prop.Type}");
    }

    [Fact]
    public void Los_mostradores_de_adentro_atienden_lo_mismo()
    {
        foreach (var (npc, interior) in new[] { (NpcInteractionRules.Deposit, "galpon-moretti"), (NpcInteractionRules.Workshop, "taller-caruso"), (NpcInteractionRules.ValentiOffice, "oficina-valenti") })
        {
            var counter = InteriorLayout.CounterOf(interior);
            Assert.True(NpcInteractionRules.InRange(new Vector3(counter.X, 0, counter.Y + 1f), npc), $"{npc.Name} no atiende adentro");
            Assert.True(NpcInteractionRules.InRange(npc.Position, npc), "sigue atendiendo en la vereda");
            Assert.Equal(npc.Position.X, npc.MapPosition.X); // en el mapa sigue en la vereda
        }
        foreach (var shop in NpcInteractionRules.Shops)
        {
            var plan = InteriorLayout.At(shop.Position.X, shop.Position.Z);
            Assert.NotNull(plan);
            Assert.Equal(plan!.Door, shop.MapPosition); // en el mapa, en la puerta de su tienda
            Assert.NotEmpty(NpcInteractionRules.DialogFor(shop.Code, new Distrito.Shared.Economy.EconomySnapshot(0, [], [], 0)).Text);
        }
    }

    [Fact]
    public void Adentro_no_se_pelea_y_vale_la_zona_de_la_puerta()
    {
        foreach (var p in InteriorLayout.All)
        {
            var inside = new Vector3(p.Origin.X, 0, p.Origin.Y);
            Assert.True(CombatRules.InSafeZone(inside));
            Assert.Equal(CombatRules.ZoneAt(new Vector3(p.Door.X, 0, p.Door.Y)), CombatRules.ZoneAt(inside));
            Assert.Equal(p.Door, InteriorLayout.MapPoint(inside.X, inside.Z));
        }
        Assert.Null(InteriorLayout.At(0, 0));
    }
}
