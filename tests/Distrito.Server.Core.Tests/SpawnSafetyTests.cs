using System.Numerics;
using Distrito.Server.Core.Sessions;
using Distrito.Server.Core.World;

namespace Distrito.Server.Core.Tests;

public class SpawnSafetyTests
{
    [Fact]
    public void Posiciones_dentro_del_barrio_son_validas()
    {
        Assert.True(SpawnSafety.IsWithinWorld(new Vector3(3, 1, 0)));
        Assert.True(SpawnSafety.IsWithinWorld(new Vector3(-28, 0, 28)));
    }

    [Theory]
    [InlineData(500f, 1f, 500f)]   // fuera del barrio
    [InlineData(0f, -50f, 0f)]     // cayó al vacío
    [InlineData(0f, 80f, 0f)]      // en el aire
    [InlineData(float.NaN, 0f, 0f)]
    public void Posiciones_invalidas_se_rechazan(float x, float y, float z)
    {
        Assert.False(SpawnSafety.IsWithinWorld(new Vector3(x, y, z)));
    }

    [Fact]
    public void Los_puntos_de_aparicion_son_seguros()
    {
        for (var slot = 0; slot < 24; slot++)
            Assert.True(SpawnSafety.IsWithinWorld(ZoneSessions.SpawnPointFor(slot)));
    }
}
