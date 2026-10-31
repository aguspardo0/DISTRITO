using System.Numerics;
using Distrito.Server.Core.Interest;

namespace Distrito.Server.Core.Tests;

public class InterestRulesTests
{
    private readonly InterestRules _rules = new(enterRadius: 20f, hysteresis: 5f);

    [Fact]
    public void Ve_lo_que_esta_dentro_del_radio()
    {
        Assert.True(_rules.IsVisible(Vector3.Zero, new Vector3(19, 0, 0), wasVisible: false));
        Assert.False(_rules.IsVisible(Vector3.Zero, new Vector3(21, 0, 0), wasVisible: false));
    }

    [Fact]
    public void La_altura_no_cuenta()
    {
        Assert.True(_rules.IsVisible(Vector3.Zero, new Vector3(10, 50, 10), wasVisible: false));
    }

    [Fact]
    public void Histeresis_evita_parpadeo_en_el_borde()
    {
        var edge = new Vector3(22, 0, 0); // entre radio de entrada (20) y de salida (25)

        Assert.False(_rules.IsVisible(Vector3.Zero, edge, wasVisible: false)); // no entra todavía
        Assert.True(_rules.IsVisible(Vector3.Zero, edge, wasVisible: true));   // si ya lo veía, lo sigue viendo
        Assert.False(_rules.IsVisible(Vector3.Zero, new Vector3(26, 0, 0), wasVisible: true));
    }

    [Fact]
    public void Radio_invalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InterestRules(0));
    }
}
