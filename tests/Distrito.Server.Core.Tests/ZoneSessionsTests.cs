using System.Numerics;
using Distrito.Server.Core.Sessions;

namespace Distrito.Server.Core.Tests;

public class ZoneSessionsTests
{
    [Fact]
    public void Asigna_lugares_distintos_a_cada_jugador()
    {
        var zone = new ZoneSessions(capacity: 4);

        var a = zone.TryJoin(10)!;
        var b = zone.TryJoin(20)!;

        Assert.NotEqual(a.Slot, b.Slot);
        Assert.NotEqual(a.SpawnPoint, b.SpawnPoint);
        Assert.Equal(2, zone.Count);
    }

    [Fact]
    public void Rechaza_jugadores_si_la_zona_esta_llena()
    {
        var zone = new ZoneSessions(capacity: 1);
        Assert.NotNull(zone.TryJoin(1));
        Assert.Null(zone.TryJoin(2));
    }

    [Fact]
    public void No_registra_dos_veces_al_mismo_jugador()
    {
        var zone = new ZoneSessions(capacity: 4);
        Assert.NotNull(zone.TryJoin(1));
        Assert.Null(zone.TryJoin(1));
        Assert.Equal(1, zone.Count);
    }

    [Fact]
    public void Reutiliza_el_lugar_libre_mas_bajo()
    {
        var zone = new ZoneSessions(capacity: 4);
        zone.TryJoin(1);
        zone.TryJoin(2);
        zone.TryJoin(3);

        Assert.True(zone.Leave(2));
        Assert.Equal(1, zone.TryJoin(4)!.Slot);
    }

    [Fact]
    public void Los_puntos_de_aparicion_no_se_superponen()
    {
        var points = Enumerable.Range(0, 32).Select(ZoneSessions.SpawnPointFor).ToList();

        for (var i = 0; i < points.Count; i++)
        for (var j = i + 1; j < points.Count; j++)
            Assert.True(Vector3.Distance(points[i], points[j]) > 1f, $"Lugares {i} y {j} demasiado cerca");
    }

    [Fact]
    public void Capacidad_invalida_lanza_excepcion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneSessions(0));
    }
}

public class ZoneSessionsCharacterTests
{
    [Fact]
    public void Un_personaje_no_puede_estar_conectado_dos_veces()
    {
        var zone = new ZoneSessions(capacity: 4);
        var character = Guid.NewGuid();

        Assert.NotNull(zone.TryJoin(1, character, out _));
        Assert.Null(zone.TryJoin(2, character, out var rejection));
        Assert.Equal(JoinRejection.CharacterAlreadyOnline, rejection);
    }

    [Fact]
    public void Al_salir_el_personaje_puede_volver_a_entrar()
    {
        var zone = new ZoneSessions(capacity: 4);
        var character = Guid.NewGuid();
        zone.TryJoin(1, character, out _);
        zone.Leave(1);

        Assert.NotNull(zone.TryJoin(2, character, out var rejection));
        Assert.Equal(JoinRejection.None, rejection);
    }

    [Fact]
    public void Varios_invitados_pueden_entrar_a_la_vez()
    {
        var zone = new ZoneSessions(capacity: 4);
        Assert.NotNull(zone.TryJoin(1, null, out _));
        Assert.NotNull(zone.TryJoin(2, null, out _));
    }

    [Fact]
    public void Informa_zona_llena()
    {
        var zone = new ZoneSessions(capacity: 1);
        zone.TryJoin(1, null, out _);
        zone.TryJoin(2, null, out var rejection);
        Assert.Equal(JoinRejection.ZoneFull, rejection);
    }
}

public class PendingConnectionsTests
{
    [Fact]
    public void Vencen_las_conexiones_que_no_se_autentican_a_tiempo()
    {
        var pending = new PendingConnections();
        pending.Add(1, nowSeconds: 0);
        pending.Add(2, nowSeconds: 5);

        var expired = pending.TakeExpired(PendingConnections.Timeout.TotalSeconds + 1);

        Assert.Equal(new long[] { 1 }, expired);
        Assert.False(pending.Contains(1));
        Assert.True(pending.Contains(2));
    }

    [Fact]
    public void Autenticarse_saca_de_la_lista_una_sola_vez()
    {
        var pending = new PendingConnections();
        pending.Add(7, 0);

        Assert.True(pending.TryTake(7));
        Assert.False(pending.TryTake(7));
        Assert.Empty(pending.TakeExpired(1000));
    }
}
