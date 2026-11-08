using Distrito.Shared.Life;

namespace Distrito.Shared.Tests;

/// <summary>Vida autónoma de los NPC, etapa 1: los 30 habitantes de la prueba y el reloj de Bellmont.</summary>
public class LifeTests
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

    [Fact]
    public void La_config_del_repo_no_tiene_errores()
    {
        Assert.Empty(Config().Validate());
        Assert.Equal(30, Config().BasePopulation);
        Assert.Equal(278, Config().Population); // los 30 de la prueba y los 248 del Centro (etapa 9)
    }

    [Fact]
    public void Treinta_habitantes_con_la_composicion_de_la_prueba()
    {
        var all = ResidentGenerator.Generate(Config());
        Assert.Equal(278, all.Count);
        Assert.Equal(278, all.Select(p => p.Id).Distinct().Count());
        var people = all.Take(30).ToList();

        Assert.Equal(8, people.Count(p => p.Profession == "obrero"));
        Assert.Equal(6, people.Count(p => p.Profession == "comerciante"));
        Assert.Equal(4, people.Count(p => p.Profession == "transportista"));
        Assert.Equal(8, people.Count(p => p.Profession == "vecino"));
        Assert.Equal(4, people.Count(p => p.Profession == "policia"));
        Assert.Equal(30, people.Select(p => p.Id).Distinct().Count());
        Assert.Equal(30, people.Select(p => p.FullName).Distinct().Count());
        Assert.All(people.Where(p => p.Profession == "vecino"), p => Assert.Null(p.Workplace));
        Assert.All(people.Where(p => p.Profession != "vecino"), p => Assert.NotNull(p.Workplace));
    }

    [Fact]
    public void La_gente_del_Centro_se_suma_sin_cambiar_a_los_que_ya_vivian()
    {
        var cfg = Config();
        var with = ResidentGenerator.Generate(cfg);
        cfg.Expansions.Clear();
        var before = ResidentGenerator.Generate(cfg);
        Assert.Equal(30, before.Count);
        static string Key(Resident r) => $"{r.Id} {r.FullName} {r.HomeX} {r.HomeZ} {r.Workplace} {string.Join(',', r.Traits.Select(t => $"{t.Key}={t.Value}"))}";
        Assert.Equal(before.Select(Key), with.Take(30).Select(Key)); // mismos ids, nombres, casas y personalidades
        var downtown = with.Skip(30).ToList();
        Assert.Contains(downtown, p => p.Profession == "oficinista");
        Assert.All(downtown, p => Assert.StartsWith("centro-", p.District));
        Assert.All(downtown.Where(p => p.Profession != "vecino"), p => Assert.NotNull(p.Workplace));
    }

    [Fact]
    public void Es_determinista_misma_semilla_mismos_habitantes()
    {
        var a = ResidentGenerator.Generate(Config());
        var b = ResidentGenerator.Generate(Config());
        Assert.Equal(a.Select(p => (p.Id, p.FullName, p.Age, p.HomeX)), b.Select(p => (p.Id, p.FullName, p.Age, p.HomeX)));
    }

    [Fact]
    public void Las_personalidades_son_distintas_y_los_opuestos_se_complementan()
    {
        var cfg = Config();
        var people = ResidentGenerator.Generate(cfg);
        Assert.All(people, p =>
        {
            Assert.Equal(cfg.Traits.Count, p.Traits.Count);
            Assert.All(p.Traits.Values, v => Assert.InRange(v, 0, 1));
            Assert.Equal(1, p.Trait("sociable") + p.Trait("reservado"), 2);
        });
        // No son todos iguales: hay sociables y reservados, y el rasgo varía bastante.
        var sociable = people.Select(p => p.Trait("sociable")).ToList();
        Assert.True(sociable.Max() - sociable.Min() > 0.3);
    }

    [Fact]
    public void Cada_uno_vive_en_su_barrio_y_hay_gente_en_varios()
    {
        var cfg = Config();
        var people = ResidentGenerator.Generate(cfg);
        Assert.All(people, p =>
        {
            var d = cfg.AllDistricts.Single(x => x.Code == p.District);
            Assert.True(Math.Sqrt(Math.Pow(p.HomeX - d.X, 2) + Math.Pow(p.HomeZ - d.Z, 2)) <= d.Radius + 0.01);
        });
        Assert.True(people.Select(p => p.District).Distinct().Count() >= 3);
    }

    [Fact]
    public void Hay_policias_de_turno_de_dia_y_de_noche()
    {
        var police = ResidentGenerator.Generate(Config()).Where(p => p.Profession == "policia").ToList();
        Assert.Contains(police, p => p.OnShift(10));
        Assert.Contains(police, p => p.OnShift(23));
        Assert.All(police, p => Assert.False(p.Woman));
    }

    [Fact]
    public void El_reloj_de_Bellmont_da_dos_dias_por_dia_real_con_seis_horas_de_noche()
    {
        var argentina = TimeSpan.FromHours(-3);
        Assert.Equal(0, BellmontClock.Hour(new DateTimeOffset(2026, 11, 8, 0, 0, 0, argentina)));
        Assert.Equal(6, BellmontClock.Hour(new DateTimeOffset(2026, 11, 8, 3, 0, 0, argentina)));
        Assert.Equal(18, BellmontClock.Hour(new DateTimeOffset(2026, 11, 8, 9, 0, 0, argentina)));
        Assert.Equal(0, BellmontClock.Hour(new DateTimeOffset(2026, 11, 8, 12, 0, 0, argentina)));
        Assert.Equal(18, BellmontClock.Hour(new DateTimeOffset(2026, 11, 8, 21, 0, 0, argentina)));

        var night = 0;
        var start = new DateTimeOffset(2026, 11, 8, 0, 0, 0, argentina);
        for (var minute = 0; minute < 24 * 60; minute++)
            if (BellmontClock.IsNight(start.AddMinutes(minute)))
                night++;
        Assert.Equal(12 * 60, night); // 6 h de noche por cada día de Bellmont, dos por día real

        Assert.Equal(BellmontClock.Day(start) + 2, BellmontClock.Day(start.AddDays(1)));
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 3, 0, 0, argentina), BellmontClock.Next(start, BellmontClock.Dawn));
    }

    [Fact]
    public void Errores_claros_al_editar_mal()
    {
        var cfg = Config();
        cfg.Professions[0].Places.Add("no-existe");
        cfg.Opposites.Add(["sociable", "inventado"]);
        var errors = cfg.Validate();
        Assert.Contains(errors, e => e.Contains("no-existe"));
        Assert.Contains(errors, e => e.Contains("inventado"));
    }
}
