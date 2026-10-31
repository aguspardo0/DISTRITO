using System.Numerics;

namespace Distrito.Server.Core.Sessions;

/// <summary>Jugador conectado a una zona (barrio). Datos temporales de sesión, no persistentes.</summary>
/// <param name="CharacterId">Personaje persistente; null para invitados.</param>
public sealed record ZoneSession(long PeerId, int Slot, Vector3 SpawnPoint, Guid? CharacterId);

public enum JoinRejection
{
    None,
    ZoneFull,
    AlreadyInZone,
    CharacterAlreadyOnline,
}

/// <summary>
/// Registro de jugadores conectados a una zona: capacidad, asignación de lugar, punto de aparición
/// y regla de "un personaje no puede estar conectado dos veces".
/// </summary>
public sealed class ZoneSessions(int capacity)
{
    private const float SpawnRingRadius = 3f;
    private const int SpawnRingSlots = 8;

    private readonly Dictionary<long, ZoneSession> _byPeer = new();

    public int Capacity { get; } = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "La capacidad debe ser positiva.");

    public int Count => _byPeer.Count;

    public IEnumerable<ZoneSession> All => _byPeer.Values;

    /// <summary>Registra un jugador. Devuelve null y el motivo si no puede entrar.</summary>
    public ZoneSession? TryJoin(long peerId, Guid? characterId, out JoinRejection rejection)
    {
        rejection =
            _byPeer.ContainsKey(peerId) ? JoinRejection.AlreadyInZone :
            _byPeer.Count >= Capacity ? JoinRejection.ZoneFull :
            characterId is not null && _byPeer.Values.Any(s => s.CharacterId == characterId) ? JoinRejection.CharacterAlreadyOnline :
            JoinRejection.None;

        if (rejection != JoinRejection.None)
            return null;

        var slot = LowestFreeSlot();
        var session = new ZoneSession(peerId, slot, SpawnPointFor(slot), characterId);
        _byPeer.Add(peerId, session);
        return session;
    }

    /// <summary>Atajo para invitados.</summary>
    public ZoneSession? TryJoin(long peerId) => TryJoin(peerId, null, out _);

    public bool Leave(long peerId) => _byPeer.Remove(peerId);

    public ZoneSession? Get(long peerId) => _byPeer.GetValueOrDefault(peerId);

    /// <summary>Puntos repartidos en anillos alrededor del centro, para que nadie aparezca encima de otro.</summary>
    public static Vector3 SpawnPointFor(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        var ring = slot / SpawnRingSlots + 1;
        var angle = slot % SpawnRingSlots * (MathF.Tau / SpawnRingSlots);
        var radius = SpawnRingRadius * ring;
        return new Vector3(MathF.Cos(angle) * radius, 1f, MathF.Sin(angle) * radius);
    }

    private int LowestFreeSlot()
    {
        var used = _byPeer.Values.Select(s => s.Slot).ToHashSet();
        var slot = 0;
        while (used.Contains(slot)) slot++;
        return slot;
    }
}
