using System.Numerics;
using Distrito.Server.Core.Combat;
using Distrito.Server.Core.Npcs;
using Distrito.Server.Core.World;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Tests;

public class CityLayoutTests
{
    private static bool Overlap(District a, District b) =>
        a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinZ < b.MaxZ && b.MinZ < a.MaxZ;

    private static bool Touch(District a, District b) =>
        !Overlap(a, b) && a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinZ <= b.MaxZ && b.MinZ <= a.MaxZ;

    [Fact]
    public void Los_distritos_no_se_pisan()
    {
        var all = CityLayout.Districts;
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
                Assert.False(Overlap(all[i], all[j]), $"{all[i].Code} pisa a {all[j].Code}");
    }

    [Fact]
    public void Las_familias_no_se_juntan_cada_barrio_solo_toca_su_avenida()
    {
        foreach (var b in CityLayout.Barrios)
        {
            var neighbours = CityLayout.Districts.Where(d => d != b.Area && Touch(d, b.Area)).ToList();
            Assert.Equal(new[] { b.Avenue }, neighbours);
            Assert.True(Touch(b.Avenue, CityLayout.Market), $"{b.Avenue.Code} no llega al Mercado");
        }
    }

    [Theory]
    [InlineData(-30.5f, 0f)] [InlineData(-29.5f, 0f)] [InlineData(-59.5f, 0f)] [InlineData(-60.5f, 0f)] // Avenida Oeste
    [InlineData(24.5f, -29.5f)] [InlineData(24.5f, -30.5f)] [InlineData(24.5f, -60.5f)]           // Avenida Norte
    [InlineData(13f, 29.5f)] [InlineData(13f, 30.5f)] [InlineData(13f, 60.5f)]                    // Avenida Sur
    [InlineData(30.5f, 0f)]                                                                       // Mercado → Muelles
    public void Las_uniones_quedan_abiertas(float x, float z) => Assert.True(CityLayout.IsWalkable(x, z, 0.4f));

    [Theory]
    [InlineData(-45f, 10f)]   // al costado de la Avenida Oeste
    [InlineData(-31f, -40f)]  // entre el Mercado y Valenti, fuera de la avenida
    [InlineData(-55f, 55f)]   // entre Moretti y Caruso
    [InlineData(80f, -45f)]   // al norte de Los Muelles
    [InlineData(500f, 0f)]
    public void Afuera_de_la_ciudad_no_se_camina(float x, float z) => Assert.False(CityLayout.IsWalkable(x, z));

    [Fact]
    public void Cada_pared_del_borde_tiene_ciudad_de_un_lado_y_nada_del_otro()
    {
        var edges = CityLayout.Edges();
        Assert.NotEmpty(edges);
        foreach (var e in edges)
        {
            Assert.True(e.Length > 0);
            var (mx, mz) = ((e.X1 + e.X2) / 2, (e.Z1 + e.Z2) / 2);
            var (a, b) = e.Horizontal
                ? (CityLayout.IsWalkable(mx, mz - 0.5f), CityLayout.IsWalkable(mx, mz + 0.5f))
                : (CityLayout.IsWalkable(mx - 0.5f, mz), CityLayout.IsWalkable(mx + 0.5f, mz));
            Assert.True(a ^ b, $"pared en ({mx}, {mz}) no separa adentro de afuera");
        }
    }

    [Fact]
    public void Los_edificios_quedan_adentro_de_su_barrio_y_no_tapan_el_bulevar_ni_la_plaza()
    {
        foreach (var b in CityLayout.Barrios)
        {
            var boulevard = b.Rect(0, CityLayout.HqFront, -5, 5);
            foreach (var building in CityLayout.BuildingsOf(b))
            {
                var p = building.Plan;
                Assert.True(b.Area.Contains(p.X - p.Width / 2, p.Z - p.Depth / 2) && b.Area.Contains(p.X + p.Width / 2, p.Z + p.Depth / 2),
                    $"edificio de {b.Family} fuera del barrio en ({p.X}, {p.Z})");
                Assert.False(Intersects(p, boulevard), $"edificio de {b.Family} tapa el bulevar en ({p.X}, {p.Z})");
            }
            Assert.Single(CityLayout.BuildingsOf(b), x => x.Headquarters);
        }
    }

    [Fact]
    public void Cada_npc_esta_en_un_lugar_caminable_y_libre()
    {
        foreach (var npc in NpcInteractionRules.All)
        {
            Assert.True(CityLayout.IsWalkable(npc.Position.X, npc.Position.Z, 1f), $"{npc.Name} quedó contra un borde");
            Assert.All(MapPresence.Buildings, p =>
                Assert.False(MathF.Abs(npc.Position.X - p.X) < p.Width / 2 + 1f && MathF.Abs(npc.Position.Z - p.Z) < p.Depth / 2 + 1f,
                    $"{npc.Name} está pegado o adentro de un edificio en ({p.X}, {p.Z})"));
        }
    }

    [Fact]
    public void Cada_familia_tiene_capo_y_dos_de_su_gente_en_su_barrio()
    {
        foreach (var b in CityLayout.Barrios)
        {
            var boss = NpcInteractionRules.Find(FamilyPeople.BossOf(b.Family))!;
            Assert.Same(b, CityLayout.BarrioAt(boss.Position.X, boss.Position.Z));
            var people = NpcInteractionRules.FamilyContacts.Where(c => FamilyRules.FamilyOfNpc(c.Code) == b.Family).ToList();
            Assert.Equal(2, people.Count);
            Assert.All(people, c => Assert.Same(b, CityLayout.BarrioAt(c.Position.X, c.Position.Z)));
        }
    }

    [Fact]
    public void Las_zonas_de_los_barrios_incluyen_su_avenida_y_el_mercado_sigue_igual()
    {
        Assert.Equal(TerritoryRules.BarrioMoretti, CombatRules.ZoneAt(new Vector3(-100, 0, 0)));
        Assert.Equal(TerritoryRules.BarrioMoretti, CombatRules.ZoneAt(new Vector3(-45, 0, 0)));
        Assert.Equal(TerritoryRules.BarrioValenti, CombatRules.ZoneAt(new Vector3(24.5f, 0, -100)));
        Assert.Equal(TerritoryRules.BarrioCaruso, CombatRules.ZoneAt(new Vector3(13, 0, 100)));
        Assert.Equal(TerritoryRules.Docks, CombatRules.ZoneAt(new Vector3(60, 0, 0)));
        Assert.Equal(TerritoryRules.Warehouse, CombatRules.ZoneAt(new Vector3(-9, 0, -9)));
        Assert.Equal("Barrio Caruso", TerritoryRules.NameOf(TerritoryRules.BarrioCaruso));
    }

    private static bool Intersects(MapBuilding a, MapBuilding b) =>
        MathF.Abs(a.X - b.X) < (a.Width + b.Width) / 2 && MathF.Abs(a.Z - b.Z) < (a.Depth + b.Depth) / 2;
}
