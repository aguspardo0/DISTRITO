using Distrito.Server.Core.World;
using Distrito.Shared.Life;

namespace Distrito.Server.Core.Tests;

/// <summary>Vida de los NPC, etapa 5: caminos por la ciudad (por las veredas, sin atravesar edificios).</summary>
public class CityNavigationTests
{
    private static LifeConfig Config()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config/vida.json");
            if (File.Exists(candidate))
                return LifeConfig.Parse(File.ReadAllText(candidate));
        }
        throw new FileNotFoundException("config/vida.json");
    }

    private static bool Inside(double x, double z) => MapPresence.Buildings.Any(b =>
        Math.Abs(x - b.X) < b.Width / 2 && Math.Abs(z - b.Z) < b.Depth / 2);

    /// <summary>
    /// Recorre el camino de a 25 cm: ningún punto adentro de un edificio ni fuera de la ciudad. Desde el primer punto
    /// del camino: la salida puede ser la puerta de una casa (sale a la vereda y de ahí camina).
    /// </summary>
    private static void AssertWalkable(IReadOnlyList<(double X, double Z)> points)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var (ax, az) = points[i - 1];
            var (bx, bz) = points[i];
            var steps = Math.Max(1, (int)(Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) / 0.25));
            for (var k = 0; k <= steps; k++)
            {
                var x = ax + (bx - ax) * k / steps;
                var z = az + (bz - az) * k / steps;
                Assert.False(Inside(x, z), $"el camino atraviesa un edificio en ({x:0.0}, {z:0.0})");
                Assert.True(CityLayout.IsWalkable((float)x, (float)z), $"el camino sale de la ciudad en ({x:0.0}, {z:0.0})");
            }
        }
    }

    [Fact]
    public void Hay_veredas_para_caminar_en_toda_la_ciudad()
    {
        Assert.True(CityNavigation.WalkableCells > 5000, $"solo {CityNavigation.WalkableCells} celdas caminables");
        foreach (var b in CityLayout.Barrios)
            Assert.True(CityNavigation.IsFree(b.Avenue.CenterX, b.Avenue.CenterZ), $"{b.Avenue.Code} no se puede caminar");
    }

    [Theory]
    [InlineData(-104, 0, 13, 104)]    // de Moretti a Caruso: Avenida Oeste, Mercado, Avenida Sur
    [InlineData(24, -104, 70, 0)]     // de Valenti a Los Muelles
    [InlineData(-20, -20, 20, 20)]    // cruzar el Mercado en diagonal (hay edificios en el medio)
    [InlineData(0, 14.5, -8.5, -8.5)] // del diner al almacén
    public void El_camino_va_por_las_veredas_y_llega(double fx, double fz, double tx, double tz)
    {
        var route = CityNavigation.Route(fx, fz, tx, tz);
        Assert.NotEmpty(route);
        var (ex, ez) = route[^1];
        Assert.True(Math.Sqrt((ex - tx) * (ex - tx) + (ez - tz) * (ez - tz)) < 10, $"termina lejos del destino: ({ex:0.0}, {ez:0.0})");
        AssertWalkable(route);
        Assert.True(route.Count < 40, $"demasiadas esquinas: {route.Count}");
    }

    [Fact]
    public void Todos_los_lugares_y_casas_de_la_vida_se_alcanzan()
    {
        var cfg = Config();
        foreach (var place in cfg.Places)
        {
            var route = CityNavigation.Route(0, 0, place.X, place.Z);
            var (ex, ez) = route[^1];
            Assert.True(Math.Sqrt((ex - place.X) * (ex - place.X) + (ez - place.Z) * (ez - place.Z)) < 6, $"{place.Code} queda inalcanzable");
        }
        foreach (var r in ResidentGenerator.Generate(cfg))
        {
            var route = CityNavigation.Route(0, 0, r.HomeX, r.HomeZ);
            var (ex, ez) = route[^1];
            // En el Centro la casa puede estar en el medio de la manzana: se llega hasta la vereda más cercana.
            var limit = r.District.StartsWith("centro-") ? 35 : 8;
            Assert.True(Math.Sqrt((ex - r.HomeX) * (ex - r.HomeX) + (ez - r.HomeZ) * (ez - r.HomeZ)) < limit, $"la casa de {r.FullName} ({r.HomeX:0.0}, {r.HomeZ:0.0}, {r.District}) queda inalcanzable: el camino termina en ({ex:0.0}, {ez:0.0})");
        }
    }

    [Fact]
    public void Con_caminos_los_habitantes_no_atraviesan_paredes_y_el_cliente_los_ve_igual()
    {
        var cfg = Config();
        var sim = new LifeSimulation(cfg, CityNavigation.Route);
        var people = ResidentGenerator.Generate(cfg).Take(60).ToList(); // los 30 de la prueba y algunos del Centro
        var start = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
        var states = people.ToDictionary(p => p.Id, p => ResidentGenerator.InitialState(p, start));
        var trips = 0;
        for (var minute = 0; minute <= 6 * 60; minute += 2)
        {
            var now = start.AddMinutes(minute);
            foreach (var p in people)
            {
                var before = states[p.Id];
                var after = sim.Advance(p, before, now);
                if (after.Decisions != before.Decisions && after.Path is not null)
                {
                    trips++;
                    AssertWalkable(after.Waypoints().Skip(1).ToList());
                    // El cliente calcula la misma posición con lo que le llega por la red.
                    var view = ResidentSyncCodec.Decode(ResidentSyncCodec.Encode(0, [ResidentView.Of(p, after)], [])).Views[0];
                    var mid = after.Since + (after.ArrivesAt - after.Since) / 2;
                    var (sx, sz) = after.PositionAt(mid);
                    var (cx, cz, _, _, walking) = view.At(mid.ToUnixTimeMilliseconds());
                    Assert.True(walking);
                    Assert.True(Math.Abs(sx - cx) < 0.1 && Math.Abs(sz - cz) < 0.1, $"servidor ({sx:0.00}, {sz:0.00}) ≠ cliente ({cx:0.00}, {cz:0.00})");
                }
                states[p.Id] = after;
            }
        }
        Assert.True(trips > 20, $"solo {trips} viajes con camino");
    }
}
