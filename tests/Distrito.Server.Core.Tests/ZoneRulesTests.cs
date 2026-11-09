using Distrito.Server.Core.Combat;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Tests;

public class ZoneRulesTests
{
    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relative)))
                return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void El_archivo_del_repo_es_valido_y_aplica_excepciones()
    {
        var (rules, errors) = ZoneRules.Parse(File.ReadAllText(RepoFile("config/zonas.json")));
        Assert.Empty(errors);
        var quiet = rules!.For(TerritoryRules.Corner, default);
        Assert.False(quiet.Pvp);
        Assert.True(rules.For(TerritoryRules.Corner, new ZoneContext(false, true, false, false)).Pvp);   // disputa
        Assert.True(rules.For(TerritoryRules.Corner, new ZoneContext(false, false, true, false)).Pvp);   // evento
        Assert.True(rules.For(TerritoryRules.Corner, new ZoneContext(false, false, false, true)).Pvp);   // guerra
        Assert.False(rules.For(TerritoryRules.Corner, new ZoneContext(true, true, true, true)).Pvp);     // la zona segura gana
        Assert.False(rules.For(TerritoryRules.Office, default).LoseGoodsOnDeath);                       // regla propia de zona
        Assert.True(rules.For(TerritoryRules.Workshops, default).LoseGoodsOnDeath);
        Assert.True(rules.For(TerritoryRules.Docks, default).Pvp);                                      // Los Muelles: zona libre
        Assert.True(rules.For(TerritoryRules.Docks, default).Robbery);
    }

    [Fact]
    public void Los_muelles_son_zona_libre_lejos_de_los_locales()
    {
        var docks = new System.Numerics.Vector3(270, 0, 0); // Los Muelles, en el borde este
        Assert.Equal(TerritoryRules.Docks, CombatRules.ZoneAt(docks));
        var (rules, _) = ZoneRules.Parse(File.ReadAllText(RepoFile("config/zonas.json")));
        Assert.True(CombatRules.PermissionsAt(docks, rules!, new CombatState(new HashSet<string>(), new HashSet<string>(), rules)).Pvp);
        Assert.False(CombatRules.PermissionsAt(new System.Numerics.Vector3(0, 0, 0), rules!, new CombatState(new HashSet<string>(), new HashSet<string>(), rules)).Pvp);
    }

    [Fact]
    public void Errores_claros_al_editar_mal()
    {
        var (rules, errors) = ZoneRules.Parse("""{ "base": { "pvp": "si" }, "zonas": { "luna": {} }, "excepciones": { "fiesta": {} } }""");
        Assert.Null(rules);
        Assert.Contains(errors, e => e.Contains("pvp"));
        Assert.Contains(errors, e => e.Contains("luna"));
        Assert.Contains(errors, e => e.Contains("fiesta"));
        Assert.Contains(errors, e => e.Contains("falta"));
    }

    [Fact]
    public void Las_reglas_mandan_en_el_combate()
    {
        var (rules, _) = ZoneRules.Parse(File.ReadAllText(RepoFile("config/zonas.json")));
        var open = new System.Numerics.Vector3(3, 0, -1);
        var zone = CombatRules.ZoneAt(open);
        var calm = new CombatState(new HashSet<string>(), new HashSet<string>(), rules);
        var disputed = new CombatState(new HashSet<string> { zone }, new HashSet<string>(), rules);
        Assert.NotNull(CombatRules.WhyNot(open, open, 5, 5, calm, kind: AttackKind.Weapon));
        Assert.Null(CombatRules.WhyNot(open, open, 5, 5, disputed, kind: AttackKind.Weapon));
    }
}
