using System.Numerics;
using Distrito.Server.Core.Pve;

namespace Distrito.Server.Core.Tests;

public class PveRulesTests
{
    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relative)))
                return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void El_archivo_del_repo_es_valido_con_los_numeros_aprobados()
    {
        var (rules, errors) = PveRules.Parse(File.ReadAllText(RepoFile("config/eventos-pve.json")));
        Assert.Empty(errors);
        Assert.Equal(20, rules!.Truck!.EveryMinutes);
        Assert.Equal((10, 15), (rules.Truck.Min, rules.Truck.Max));
        Assert.Equal(15, rules.Collector!.EveryMinutes);
        Assert.Equal((3000L, 6000L), (rules.Collector.MinCents, rules.Collector.MaxCents));
        Assert.Equal(3, rules.Contraband!.Hours.Count);
        Assert.Equal(60, rules.Contraband.GuardHealth);
        Assert.Equal(30, rules.ShopRobbery!.CooldownMinutes);
        Assert.Equal((2000L, 4000L), (rules.ShopRobbery.MinCents, rules.ShopRobbery.MaxCents));
        // Los locales tienen que existir (código interno del NPC).
        Assert.All(rules.ShopRobbery.Shops, code => Assert.NotNull(Distrito.Server.Core.Npcs.NpcInteractionRules.Find(code)));
    }

    [Fact]
    public void Errores_claros()
    {
        var (rules, errors) = PveRules.Parse("""{ "camion": { "cadaMinutos": 20 }, "contrabando": { "horas": [25] } }""");
        Assert.Null(rules);
        Assert.Contains(errors, e => e.Contains("ruta"));
        Assert.Contains(errors, e => e.Contains("horas"));
    }

    [Fact]
    public void Apuntar_es_mirar_al_npc_de_cerca()
    {
        var eye = new Vector3(0, 1.5f, 0);
        Assert.True(PveRules.Aims(eye, 0, 0, new Vector3(0, 1.4f, -8)));     // de frente (−Z)
        Assert.False(PveRules.Aims(eye, MathF.PI / 2, 0, new Vector3(0, 1.4f, -8))); // mirando a otro lado
        Assert.False(PveRules.Aims(eye, 0, 0, new Vector3(0, 1.4f, -30)));    // muy lejos
    }

    [Fact]
    public void Turnos_de_los_eventos()
    {
        var t = new DateTimeOffset(2026, 11, 1, 16, 5, 0, TimeSpan.Zero); // 13:05 en Argentina
        Assert.Equal("2026-11-01:13", PveRules.ContrabandSlot(t, [13, 19, 23]));
        Assert.Null(PveRules.ContrabandSlot(t.AddHours(1), [13, 19, 23]));
        Assert.Equal(PveRules.SlotOf(t, 20), PveRules.SlotOf(t.AddMinutes(10), 20));
        Assert.NotEqual(PveRules.SlotOf(t, 20), PveRules.SlotOf(t.AddMinutes(20), 20));
    }
}
