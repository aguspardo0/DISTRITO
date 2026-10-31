using System.Globalization;
using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

public class DistrictEventsTests
{
    private static DateTimeOffset At(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);

    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void El_archivo_de_eventos_del_repo_es_valido()
    {
        var (events, errors) = DistrictEvents.Parse(File.ReadAllText(RepoFile("config/eventos.json")));
        Assert.Empty(errors);
        Assert.NotEmpty(events);
    }

    [Fact]
    public void Errores_claros_al_editar_mal()
    {
        var (events, errors) = DistrictEvents.Parse("""
            { "eventos": [ { "codigo": "x y", "nombre": "", "zona": "luna", "dias": ["lunez"], "inicio": "25:00",
                             "minutos": 1, "puntosPorContrato": 10, "topePorCuadrilla": 5 } ] }
            """);
        Assert.Empty(events);
        Assert.True(errors.Count >= 6);
        Assert.Single(DistrictEvents.Parse("no es json").Errors);
    }

    [Fact]
    public void Las_veces_del_evento_en_hora_de_argentina_y_el_anuncio()
    {
        var feria = new DistrictEvent("feria", "Feria", null, TerritoryRules.Corner, ["sabado"], "21:00", 30, 10, 20);
        // Sábado 31/10/2026, 21:00 en Argentina = domingo 00:00 UTC.
        var list = DistrictEvents.Occurrences([feria], At("2026-10-31T20:00:00Z"), At("2026-11-01T20:00:00Z"));
        var e = Assert.Single(list);
        Assert.Equal(At("2026-11-01T00:00:00Z"), e.StartsAt);
        Assert.Equal("feria:2026-10-31", e.OccurrenceId);

        Assert.Null(DistrictEvents.Banner(list, At("2026-10-31T22:00:00Z")));                 // 19:00: falta más de una hora
        Assert.StartsWith("Próximo evento", DistrictEvents.Banner(list, At("2026-10-31T23:30:00Z")));
        Assert.StartsWith("EVENTO", DistrictEvents.Banner(list, At("2026-11-01T00:10:00Z")));
        Assert.Null(DistrictEvents.Banner(list, At("2026-11-01T00:31:00Z")));
    }
}
