using Distrito.Server.Core.World;

namespace Distrito.Server.Core.Tests;

public class MapPresenceTests
{
    private static readonly MapMarker[] Everyone =
    [
        new(2, "Lejano", 25.26f, -20.1f),
        new(1, "Yo", 3.1f, 0.2f),
    ];

    [Fact]
    public void Todos_ven_a_todos_con_posicion_redondeada()
    {
        var seen = MapPresence.VisibleTo(1, Everyone);
        Assert.Equal(new long[] { 1, 2 }, seen.Select(m => m.Peer));
        Assert.Equal(25.5f, seen[1].X);
        Assert.Equal(-20f, seen[1].Z);
    }

    [Fact]
    public void Empaquetar_y_desempaquetar_conserva_los_datos()
    {
        var (peers, names, coords) = MapPresence.Pack(Everyone);
        Assert.Equal(Everyone, MapPresence.Unpack(peers, names, coords));
        Assert.Empty(MapPresence.Unpack([1], ["x"], [])); // datos incompletos: nada
    }

    [Fact]
    public void Los_edificios_del_mapa_estan_dentro_del_barrio()
    {
        Assert.All(MapPresence.Buildings, b =>
        {
            const float e = 0.01f; // redondeo de float en los edificios pegados al borde
            Assert.InRange(b.X - b.Width / 2, MapPresence.MinX - e, MapPresence.MaxX + e);
            Assert.InRange(b.X + b.Width / 2, MapPresence.MinX - e, MapPresence.MaxX + e);
            Assert.InRange(b.Z - b.Depth / 2, MapPresence.MinZ - e, MapPresence.MaxZ + e);
            Assert.InRange(b.Z + b.Depth / 2, MapPresence.MinZ - e, MapPresence.MaxZ + e);
        });
    }
}
