using System.Numerics;
using Distrito.Server.Core.Interest;

namespace Distrito.Server.Core.Tests;

public class PlayerSnapshotTests
{
    [Fact]
    public void Codifica_y_decodifica_con_precision_de_un_centimetro()
    {
        var states = new List<PlayerState>
        {
            new(1_424_160_960, new Vector3(-6.35f, 0.29f, 12.071f), 2.5f, -0.4f),
            new(7, new Vector3(3012.5f, -480.02f, 1499.99f), -3.1f, 0.85f),
        };

        var bytes = PlayerSnapshotCodec.Encode(states);
        var back = PlayerSnapshotCodec.Decode(bytes);

        Assert.Equal(2 * PlayerSnapshotCodec.EntryBytes, bytes.Length);
        for (var i = 0; i < states.Count; i++)
        {
            Assert.Equal(states[i].PeerId, back[i].PeerId);
            Assert.True(Vector3.Distance(states[i].Position, back[i].Position) < 0.01f);
            Assert.True(MathF.Abs(states[i].Yaw - back[i].Yaw) < 0.001f);
            Assert.True(MathF.Abs(states[i].AimPitch - back[i].AimPitch) < 0.001f);
        }
    }

    [Fact]
    public void Un_paquete_lleno_entra_en_un_paquete_de_red()
    {
        Assert.True(PlayerSnapshotCodec.MaxEntriesPerPacket * PlayerSnapshotCodec.EntryBytes <= 1200);
        var tooMany = Enumerable.Range(1, PlayerSnapshotCodec.MaxEntriesPerPacket + 1)
            .Select(i => new PlayerState(i, Vector3.Zero, 0, 0)).ToList();
        Assert.Throws<ArgumentException>(() => PlayerSnapshotCodec.Encode(tooMany));
    }

    [Theory]
    [InlineData(5f, 1)]
    [InlineData(20f, 1)]
    [InlineData(30f, 2)]
    [InlineData(55f, 4)]
    public void Lejos_se_manda_menos_seguido(float distance, int interval) =>
        Assert.Equal(interval, SnapshotScheduler.IntervalFor(distance));

    [Fact]
    public void Quieto_no_se_vuelve_a_mandar_salvo_el_recordatorio_de_cada_segundo()
    {
        var scheduler = new SnapshotScheduler();
        var target = new PlayerState(2, new Vector3(5, 0, 0), 0, 0);
        var sent = 0;
        for (long tick = 0; tick < 60; tick++)
        {
            if (!scheduler.ShouldSend(target, observer: 1, Vector3.Zero, tick))
                continue;
            scheduler.MarkSent(target, 1, tick);
            sent++;
        }
        Assert.InRange(sent, 3, 4); // el primero y uno por segundo
    }

    [Fact]
    public void En_movimiento_cerca_va_siempre_y_lejos_a_un_cuarto()
    {
        var scheduler = new SnapshotScheduler();
        int near = 0, far = 0;
        for (long tick = 0; tick < 40; tick++)
        {
            var a = new PlayerState(2, new Vector3(5 + tick * 0.2f, 0, 0), 0, 0);
            var b = new PlayerState(3, new Vector3(55 + tick * 0.2f, 0, 0), 0, 0);
            if (scheduler.ShouldSend(a, 1, Vector3.Zero, tick)) { scheduler.MarkSent(a, 1, tick); near++; }
            if (scheduler.ShouldSend(b, 1, Vector3.Zero, tick)) { scheduler.MarkSent(b, 1, tick); far++; }
        }
        Assert.Equal(40, near);
        Assert.InRange(far, 10, 11);
    }

    [Fact]
    public void El_propio_personaje_va_en_cada_paso()
    {
        var scheduler = new SnapshotScheduler();
        var me = new PlayerState(1, new Vector3(100, 0, 0), 0, 0);
        for (long tick = 0; tick < 10; tick++)
        {
            Assert.True(scheduler.ShouldSend(me, 1, me.Position, tick));
            scheduler.MarkSent(me, 1, tick);
        }
    }
}
