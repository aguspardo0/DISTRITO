using Distrito.Shared.Api;

namespace Distrito.Backend.Tests;

/// <summary>Reportes de problemas de la prueba cerrada (F8).</summary>
[Trait("Category", "Database")]
public class BugReportApiTests
{
    private const string Key = TestBackend.ServerKey;

    [Fact]
    public async Task Se_guarda_el_reporte_y_se_ve_entre_los_ultimos()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var text = $"Me trabé contra la pared del Banco {Guid.NewGuid():N}";
        var id = (await api.SendBugReportAsync(Key, new BugReport(null, "Tester", text, 121.5f, -121f, "el-centro", "0.2.1"))).Value;
        Assert.True(id > 0);
        var latest = (await api.GetBugReportsAsync(Key, 20)).Value!;
        var mine = Assert.Single(latest, r => r.Text == text);
        Assert.Equal("Tester", mine.Player);
        Assert.Equal("el-centro", mine.Zone);
        Assert.NotNull(mine.At);

        Assert.False((await api.SendBugReportAsync(Key, new BugReport(null, "Tester", "   ", 0, 0, "", ""))).IsSuccess);
        var longText = new string('x', 3000);
        var longId = (await api.SendBugReportAsync(Key, new BugReport(null, "Tester", longText, 0, 0, "", ""))).Value;
        Assert.Equal(BugReport.MaxLength, (await api.GetBugReportsAsync(Key, 5)).Value!.Single(r => r.Id == longId).Text.Length);
    }

    [Fact]
    public async Task Sin_clave_de_servidor_no_se_leen()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        Assert.False((await api.GetBugReportsAsync("clave-falsa")).IsSuccess);
    }
}
