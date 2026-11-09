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
    [InlineData(104.5f, 0f)] [InlineData(106f, 0f)] [InlineData(180f, 0f)] [InlineData(247f, 0f)] // Calle Principal → Avenida del Puerto → Muelles
    public void Las_uniones_quedan_abiertas(float x, float z) => Assert.True(CityLayout.IsWalkable(x, z, 0.4f));

    [Theory]
    [InlineData(301f, 0f)]    // pasando el borde este del Centro
    [InlineData(0f, -301f)]
    [InlineData(-305f, 120f)]
    [InlineData(500f, 0f)]
    public void Afuera_de_la_ciudad_no_se_camina(float x, float z) => Assert.False(CityLayout.IsWalkable(x, z));

    [Theory]
    [InlineData(-45f, 10f)]   // al costado de la Avenida Oeste (antes era afuera)
    [InlineData(-55f, 55f)]   // entre Moretti y Caruso
    [InlineData(200f, 200f)]
    [InlineData(-290f, -290f)]
    public void El_centro_rodea_a_todo_y_se_camina(float x, float z)
    {
        Assert.True(CityLayout.IsWalkable(x, z));
        Assert.Equal(DistrictKind.Downtown, CityLayout.DistrictAt(x, z)!.Kind);
        Assert.Equal(TerritoryRules.Downtown, CombatRules.ZoneAt(new Vector3(x, 0, z)));
    }

    [Fact]
    public void Los_barrios_quedan_cerrados_por_un_muro_salvo_por_su_avenida()
    {
        foreach (var b in CityLayout.Barrios)
        {
            var a = b.Area;
            var sides = new (float X, float Z)[] { (a.CenterX, a.MinZ), (a.CenterX, a.MaxZ), (a.MinX, a.CenterZ), (a.MaxX, a.CenterZ) };
            var entrance = (b.Entrance.X, b.Entrance.Y);
            foreach (var p in sides)
            {
                if (MathF.Abs(p.X - entrance.X) < 0.01f && MathF.Abs(p.Z - entrance.Y) < 0.01f)
                    Assert.False(CityLayout.InsideWall(p.X, p.Z), $"la entrada de {a.Code} quedó tapada");
                else
                    Assert.True(CityLayout.InsideWall(p.X, p.Z), $"{a.Code} está abierto en ({p.X}, {p.Z})");
            }
            // Las esquinas también están cerradas.
            Assert.True(CityLayout.InsideWall(a.MinX, a.MinZ) && CityLayout.InsideWall(a.MaxX, a.MaxZ));
        }
    }

    [Fact]
    public void Las_manzanas_del_centro_dejan_calle_alrededor_de_lo_de_antes_y_no_se_pisan()
    {
        var blocks = CityLayout.DowntownBlocks;
        Assert.True(blocks.Count >= 30, $"solo {blocks.Count} manzanas");
        foreach (var k in blocks)
        {
            Assert.True(k.MinX >= -CityLayout.DowntownHalf && k.MaxX <= CityLayout.DowntownHalf && k.MinZ >= -CityLayout.DowntownHalf && k.MaxZ <= CityLayout.DowntownHalf);
            foreach (var d in CityLayout.Districts)
                Assert.False(k.MinX < d.MaxX + CityLayout.StreetWidth - 0.01f && d.MinX - CityLayout.StreetWidth + 0.01f < k.MaxX &&
                             k.MinZ < d.MaxZ + CityLayout.StreetWidth - 0.01f && d.MinZ - CityLayout.StreetWidth + 0.01f < k.MaxZ,
                    $"la manzana ({k.MinX}, {k.MinZ}) no deja calle con {d.Code}");
        }
        for (var i = 0; i < blocks.Count; i++)
            for (var j = i + 1; j < blocks.Count; j++)
                Assert.False(blocks[i].MinX < blocks[j].MaxX && blocks[j].MinX < blocks[i].MaxX && blocks[i].MinZ < blocks[j].MaxZ && blocks[j].MinZ < blocks[i].MaxZ);
    }

    [Fact]
    public void Los_edificios_del_centro_estan_en_su_manzana_sin_pisarse_y_hay_locales_vacios_en_la_vereda()
    {
        var all = CityLayout.DowntownBuildings;
        Assert.True(all.Count >= 300, $"solo {all.Count} edificios");
        foreach (var b in all)
        {
            var p = b.Plan;
            Assert.Contains(CityLayout.DowntownBlocks, k => k.Contains(p.X - p.Width / 2, p.Z - p.Depth / 2, 0.01f) && k.Contains(p.X + p.Width / 2, p.Z + p.Depth / 2, 0.01f));
            Assert.InRange(b.Stories, 2, 15);
            Assert.True(MathF.Abs(b.FrontX) + MathF.Abs(b.FrontZ) == 1);
        }
        var sorted = all.OrderBy(b => b.Plan.X).ToList();
        for (var i = 0; i < sorted.Count; i++)
            for (var j = i + 1; j < sorted.Count && sorted[j].Plan.X - sorted[i].Plan.X < 40; j++)
                Assert.False(Intersects(Shrink(sorted[i].Plan), Shrink(sorted[j].Plan)), $"se pisan dos edificios en ({sorted[i].Plan.X}, {sorted[i].Plan.Z})");

        var shops = CityLayout.EmptyShops;
        Assert.True(shops.Count >= 30, $"solo {shops.Count} locales vacíos");
        Assert.Equal(shops.Count, shops.Select(s => s.Code).Distinct().Count());
        foreach (var s in shops)
        {
            Assert.True(CityLayout.IsWalkable(s.X, s.Z, 0.4f), $"{s.Code} no da a la calle");
            Assert.True(CityLayout.NpcArea(s.X, s.Z), $"{s.Code} no está sobre la vereda");
            Assert.DoesNotContain(MapPresence.Buildings, p => MathF.Abs(s.X - p.X) < p.Width / 2 && MathF.Abs(s.Z - p.Z) < p.Depth / 2);
        }
    }

    [Fact]
    public void Los_habitantes_van_por_la_vereda_y_no_por_la_calzada()
    {
        var k = CityLayout.DowntownBlocks.First(b => b.MinX > 100 && b.MinZ > 100);
        Assert.True(CityLayout.NpcArea(k.MinX - 1.5f, (k.MinZ + k.MaxZ) / 2));  // vereda
        Assert.False(CityLayout.NpcArea(k.MinX - 9f, (k.MinZ + k.MaxZ) / 2));   // calzada
        Assert.True(CityLayout.IsWalkable(k.MinX - 9f, (k.MinZ + k.MaxZ) / 2)); // pero los jugadores y los autos sí
    }

    private static MapBuilding Shrink(MapBuilding b) => b with { Width = b.Width - 0.02f, Depth = b.Depth - 0.02f };

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
        // Los que atienden adentro de un local (detrás del mostrador) quedan afuera de esta cuenta: el salón es su lugar.
        foreach (var npc in NpcInteractionRules.All.Where(n => InteriorLayout.At(n.Position.X, n.Position.Z) is null))
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
        Assert.Equal(TerritoryRules.Docks, CombatRules.ZoneAt(new Vector3(270, 0, 0)));
        Assert.Equal(TerritoryRules.Downtown, CombatRules.ZoneAt(new Vector3(180, 0, 0))); // Avenida del Puerto
        Assert.Equal(TerritoryRules.Warehouse, CombatRules.ZoneAt(new Vector3(-9, 0, -9)));
        Assert.Equal("Barrio Caruso", TerritoryRules.NameOf(TerritoryRules.BarrioCaruso));
    }

    [Fact]
    public void La_calle_principal_tiene_cada_negocio_en_su_lote_y_las_avenidas_la_cruzan()
    {
        var lots = MainStreet.Lots;
        Assert.Equal(lots.Count, lots.Select(l => l.Code).Distinct().Count());
        foreach (var side in new[] { true, false })
        {
            var row = lots.Where(l => l.North == side).OrderBy(l => l.MinX).ToList();
            for (var i = 0; i + 1 < row.Count; i++)
                Assert.True(row[i + 1].MinX - row[i].MaxX >= 2f, $"{row[i].Code} y {row[i + 1].Code} están pegados");
            Assert.All(row, l => Assert.True(l.MinX >= CityLayout.Market.MinX && l.MaxX <= CityLayout.Market.MaxX));
        }
        foreach (var l in MainStreet.Buildings)
        {
            var p = l.Plan;
            Assert.True(CityLayout.Market.Contains(p.X - p.Width / 2, p.Z - p.Depth / 2) && CityLayout.Market.Contains(p.X + p.Width / 2, p.Z + p.Depth / 2), $"{l.Code} se sale de la calle");
            Assert.True(MathF.Abs(p.Z) - p.Depth / 2 >= MainStreet.Front - 0.01f, $"{l.Code} pisa la vereda");
        }
        // Las avenidas de las familias desembocan en la calle sin edificios en el medio.
        foreach (var (av, side) in new[] { (CityLayout.NorthAvenue, -1f), (CityLayout.SouthAvenue, 1f) })
            for (var d = 0f; d <= 29f; d += 1f)
                Assert.True(CityNavigation.IsFree(av.CenterX, side * d), $"{av.Code} tapada en z = {side * d}");
        for (var x = CityLayout.Market.MinX + 1; x < CityLayout.Market.MaxX; x += 2)
            Assert.True(CityNavigation.IsFree(x, 0), $"la calzada está tapada en x = {x}");
        // Cada familia tiene su local en la boca de su avenida.
        Assert.True(InteriorLayout.Find("galpon-moretti")!.Door.X < CityLayout.Market.MinX + 15);
        Assert.InRange(MainStreet.Lot("oficina-valenti").MaxX, CityLayout.NorthAvenue.MinX - 1, CityLayout.NorthAvenue.MinX);
        Assert.InRange(MainStreet.Lot("taller-caruso").MaxX, CityLayout.SouthAvenue.MinX - 1, CityLayout.SouthAvenue.MinX);
    }

    [Fact]
    public void La_avenida_del_puerto_va_derecho_de_la_calle_principal_a_los_muelles()
    {
        Assert.Equal(CityLayout.Market.MaxX, CityLayout.PortAvenue.MinX);
        Assert.Equal(CityLayout.Docks.MinX, CityLayout.PortAvenue.MaxX);
        Assert.Equal(CityLayout.Downtown.MaxX, CityLayout.Docks.MaxX); // los muelles dan contra el agua
        Assert.DoesNotContain(CityLayout.DowntownBlocks, k => k.MinX < CityLayout.PortAvenue.MaxX && CityLayout.PortAvenue.MinX < k.MaxX
                                                               && k.MinZ < CityLayout.PortAvenue.MaxZ && CityLayout.PortAvenue.MinZ < k.MaxZ);
        for (var x = CityLayout.PortAvenue.MinX; x <= CityLayout.PortAvenue.MaxX; x += 3)
        {
            Assert.True(CityLayout.IsWalkable(x, 0, 0.4f), $"la avenida está cortada en x = {x}");
            Assert.DoesNotContain(MapPresence.Buildings, p => MathF.Abs(x - p.X) < p.Width / 2 && MathF.Abs(p.Z) < p.Depth / 2);
            Assert.True(CityLayout.NpcArea(x, -7) && CityLayout.NpcArea(x, 7), $"sin vereda en x = {x}"); // los habitantes van por las veredas
        }
        Assert.All(CityLayout.DowntownBuildings, b => Assert.False(MathF.Abs(b.Plan.Z) < b.Plan.Depth / 2 + 9 && b.Plan.X > 105 && b.Plan.X < 246, $"edificio en la avenida en ({b.Plan.X}, {b.Plan.Z})"));
    }

    private static bool Intersects(MapBuilding a, MapBuilding b) =>
        MathF.Abs(a.X - b.X) < (a.Width + b.Width) / 2 && MathF.Abs(a.Z - b.Z) < (a.Depth + b.Depth) / 2;

    [Fact]
    public void La_estacion_esta_al_norte_y_se_baja_derecho_al_centro()
    {
        var a = Station.Area;
        Assert.Equal(-300, a.MaxZ);
        // La avenida de la estación sigue en la calle del Centro x = 114, que baja derecho hasta la Calle Principal.
        Assert.Contains(CityLayout.StreetLines, l => MathF.Abs(l - Station.AxisX) < 0.01f);
        for (var z = -350f; z < -10f; z += 2f)
        {
            Assert.True(CityLayout.IsWalkable(Station.AxisX, z, 0.4f), $"se corta la bajada en z = {z}");
            Assert.DoesNotContain(MapPresence.Buildings, b => MathF.Abs(Station.AxisX - b.X) < b.Width / 2 + 0.5f && MathF.Abs(z - b.Z) < b.Depth / 2 + 0.5f);
        }
        Assert.Equal(TerritoryRules.Downtown, CombatRules.ZoneAt(new Vector3(Station.AxisX, 0, -380)));
        // Del otro lado del andén (las vías) no se camina.
        Assert.False(CityLayout.IsWalkable(Station.AxisX, Station.TrackZ));
    }

    [Fact]
    public void El_hall_tiene_sus_puertas_y_los_que_llegan_bajan_en_el_anden()
    {
        bool Blocked(float x, float z) => Station.Buildings.Any(b => MathF.Abs(x - b.X) < b.Width / 2 + 0.3f && MathF.Abs(z - b.Z) < b.Depth / 2 + 0.3f);
        foreach (var door in Station.PlatformDoors)
            Assert.False(Blocked(door, Station.HallBack + 0.25f), $"puerta del andén en x = {door} tapada");
        Assert.False(Blocked(Station.AxisX, Station.HallFront - 0.25f));
        Assert.True(Blocked(Station.Hall.X - 10, Station.HallFront - 0.25f)); // el resto de la pared, cerrada
        for (var slot = 0; slot < 40; slot++)
        {
            var p = Station.ArrivalSpot(slot);
            Assert.True(Station.OnPlatform(p.X, p.Z) && CityLayout.IsWalkable(p.X, p.Z, 0.4f) && !Blocked(p.X, p.Z), $"lugar {slot} ({p.X}, {p.Z})");
        }
        Assert.Equal(40, Enumerable.Range(0, 40).Select(Station.ArrivalSpot).Distinct().Count());
        Assert.True(Station.IsNewcomer(Distrito.Shared.Api.ZoneIds.NewCharacterSpawn.X, Distrito.Shared.Api.ZoneIds.NewCharacterSpawn.Y, Distrito.Shared.Api.ZoneIds.NewCharacterSpawn.Z));
        Assert.False(Station.IsNewcomer(3.2, 1, 0));
        // Las casas quedan en el barrio, fuera de la avenida y de la plaza.
        foreach (var h in Station.Houses)
        {
            var p = h.Plan;
            Assert.True(Station.Area.Contains(p.X - p.Width / 2, p.Z - p.Depth / 2) && Station.Area.Contains(p.X + p.Width / 2, p.Z + p.Depth / 2));
            Assert.True(MathF.Abs(p.X - Station.AxisX) >= p.Width / 2 + Station.AvenueHalf);
            Assert.True(p.Z - p.Depth / 2 >= Station.PlazaMaxZ);
        }
    }

    [Fact]
    public void Los_pasajeros_del_tren_no_atraviesan_paredes()
    {
        var cfg = CityScenesTests.Config();
        var def = cfg.Escenas.Single(e => e.Codigo == "tren");
        for (var seed = 1; seed < 30; seed++)
        {
            var scene = new ActiveScene(1, "tren", def.Lugares[0][0], def.Lugares[0][1], seed, 0, (long)(def.DuracionMinutos * 60_000));
            var tl = SceneScripts.Build(def, scene);
            Assert.Single(tl.Tracks, t => t.Kind == "tren");
            Assert.True(SceneScripts.Pose(tl.Tracks[0], SceneScripts.TrainStops + 10) is { Speed: 0, X: var stopX } && MathF.Abs(stopX - Station.AxisX) < 0.01f);
            foreach (var track in tl.Tracks.Where(t => t.Kind == "persona"))
                for (var i = 1; i < track.Keys.Count; i++)
                {
                    var (a, b) = (track.Keys[i - 1], track.Keys[i]);
                    if (i == 1 && a.Act == "")
                        continue;
                    for (var f = 0f; f <= 1f; f += 0.05f)
                    {
                        var (x, z) = (a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f);
                        Assert.DoesNotContain(MapPresence.Buildings, w => MathF.Abs(x - w.X) < w.Width / 2 + 0.2f && MathF.Abs(z - w.Z) < w.Depth / 2 + 0.2f);
                    }
                }
            Assert.All(tl.Tracks, t => Assert.True(t.Keys[^1].T <= scene.DurationMs / 1000.0, $"{t.Look} sigue cuando termina la escena"));
        }
    }
}
