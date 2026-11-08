using System.Buffers.Binary;
using System.Numerics;

namespace Distrito.Server.Core.Interest;

/// <summary>Lo que cambia todo el tiempo de un personaje: dónde está, hacia dónde mira y cuánto apunta arriba/abajo.</summary>
public readonly record struct PlayerState(long PeerId, Vector3 Position, float Yaw, float AimPitch);

/// <summary>
/// Paquete compacto con el movimiento de varios personajes (reemplaza el envío automático de Godot, que manda
/// cada personaje en su propio paquete, 20 veces por segundo, aunque esté lejos o quieto).
/// Cada personaje ocupa 20 bytes: id (4), posición en centímetros (3 × 4), giro (2) y mira (2).
/// Precisión: 1 cm en la posición y unos 0,006° en el giro, de sobra para dibujarlo.
/// </summary>
public static class PlayerSnapshotCodec
{
    public const int EntryBytes = 20;

    /// <summary>Entradas por paquete, para no pasar el tamaño de un paquete de red (~1200 bytes).</summary>
    public const int MaxEntriesPerPacket = 60;

    public static byte[] Encode(IReadOnlyList<PlayerState> states)
    {
        if (states.Count > MaxEntriesPerPacket)
            throw new ArgumentException($"A lo sumo {MaxEntriesPerPacket} personajes por paquete.", nameof(states));
        var data = new byte[states.Count * EntryBytes];
        var span = data.AsSpan();
        for (var i = 0; i < states.Count; i++)
        {
            var s = states[i];
            var e = span.Slice(i * EntryBytes, EntryBytes);
            BinaryPrimitives.WriteInt32LittleEndian(e, checked((int)s.PeerId));
            BinaryPrimitives.WriteInt32LittleEndian(e[4..], ToCm(s.Position.X));
            BinaryPrimitives.WriteInt32LittleEndian(e[8..], ToCm(s.Position.Y));
            BinaryPrimitives.WriteInt32LittleEndian(e[12..], ToCm(s.Position.Z));
            BinaryPrimitives.WriteUInt16LittleEndian(e[16..], YawToUInt16(s.Yaw));
            BinaryPrimitives.WriteInt16LittleEndian(e[18..], PitchToInt16(s.AimPitch));
        }
        return data;
    }

    public static List<PlayerState> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length % EntryBytes != 0)
            throw new ArgumentException("Paquete de movimiento con tamaño inválido.", nameof(data));
        var result = new List<PlayerState>(data.Length / EntryBytes);
        for (var offset = 0; offset < data.Length; offset += EntryBytes)
        {
            var e = data.Slice(offset, EntryBytes);
            result.Add(new PlayerState(
                BinaryPrimitives.ReadInt32LittleEndian(e),
                new Vector3(
                    BinaryPrimitives.ReadInt32LittleEndian(e[4..]) / 100f,
                    BinaryPrimitives.ReadInt32LittleEndian(e[8..]) / 100f,
                    BinaryPrimitives.ReadInt32LittleEndian(e[12..]) / 100f),
                BinaryPrimitives.ReadUInt16LittleEndian(e[16..]) / 65536f * MathF.Tau - MathF.PI,
                BinaryPrimitives.ReadInt16LittleEndian(e[18..]) / 10000f));
        }
        return result;
    }

    private static int ToCm(float meters) => (int)MathF.Round(Math.Clamp(meters, -2e7f, 2e7f) * 100f);

    private static ushort YawToUInt16(float yaw)
    {
        var normalized = (yaw + MathF.PI) % MathF.Tau; // [-π, π) → [0, 2π)
        if (normalized < 0)
            normalized += MathF.Tau;
        return (ushort)((int)MathF.Round(normalized / MathF.Tau * 65536f) & 0xFFFF);
    }

    private static short PitchToInt16(float pitch) => (short)Math.Clamp((int)MathF.Round(pitch * 10000f), short.MinValue, short.MaxValue);
}

/// <summary>
/// Decide, en cada paso de envío (20 por segundo), a qué observador se le manda cada personaje:
/// cerca seguido, lejos menos seguido, y nada si no cambió (salvo un recordatorio cada tanto, por si se perdió un paquete).
/// El propio personaje siempre va en cada paso: lo necesita la predicción local.
/// </summary>
public sealed class SnapshotScheduler
{
    public const int TicksPerSecond = 20;

    /// <summary>Hasta esta distancia (m) se manda en cada paso (20 por segundo).</summary>
    public const float NearDistance = 20f;

    /// <summary>Hasta esta distancia, cada 2 pasos (10 por segundo); más lejos, cada 4 (5 por segundo).</summary>
    public const float MidDistance = 40f;

    /// <summary>Aunque no cambie, se vuelve a mandar cada tantos pasos (1 s), por si se perdió el último paquete.</summary>
    public const int KeepAliveTicks = TicksPerSecond;

    private const float MinMove = 0.01f;   // 1 cm
    private const float MinTurn = 0.002f;  // ~0,1°

    private readonly Dictionary<(long Target, long Observer), (PlayerState State, long Tick)> _lastSent = new();

    public static int IntervalFor(float distance) => distance <= NearDistance ? 1 : distance <= MidDistance ? 2 : 4;

    /// <summary>¿Hay que mandarle <paramref name="target"/> a <paramref name="observer"/> en el paso <paramref name="tick"/>?</summary>
    public bool ShouldSend(PlayerState target, long observer, Vector3 observerPosition, long tick)
    {
        var key = (target.PeerId, observer);
        if (!_lastSent.TryGetValue(key, out var last))
            return true;
        if (target.PeerId == observer)
            return true;
        var dx = target.Position.X - observerPosition.X;
        var dz = target.Position.Z - observerPosition.Z;
        var interval = IntervalFor(MathF.Sqrt(dx * dx + dz * dz));
        // Repartidos entre pasos según el id, para que no salgan todos los lejanos en el mismo paquete.
        if ((tick + target.PeerId) % interval != 0 && tick - last.Tick < interval * 2)
            return false;
        var changed = Vector3.Distance(target.Position, last.State.Position) >= MinMove
                      || MathF.Abs(AngleDelta(target.Yaw, last.State.Yaw)) >= MinTurn
                      || MathF.Abs(target.AimPitch - last.State.AimPitch) >= MinTurn;
        return changed || tick - last.Tick >= KeepAliveTicks;
    }

    public void MarkSent(PlayerState target, long observer, long tick) => _lastSent[(target.PeerId, observer)] = (target, tick);

    /// <summary>Olvida lo enviado a o sobre un peer (al irse, o al dejar de verse: si vuelve a verse, se manda enseguida).</summary>
    public void Forget(long target, long observer) => _lastSent.Remove((target, observer));

    public void ForgetPeer(long peer)
    {
        foreach (var key in _lastSent.Keys.Where(k => k.Target == peer || k.Observer == peer).ToList())
            _lastSent.Remove(key);
    }

    private static float AngleDelta(float a, float b)
    {
        var d = (a - b) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau;
        if (d < -MathF.PI) d += MathF.Tau;
        return d;
    }
}
