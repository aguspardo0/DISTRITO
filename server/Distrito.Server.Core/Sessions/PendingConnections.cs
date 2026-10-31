namespace Distrito.Server.Core.Sessions;

/// <summary>
/// Conexiones de red que todavía no se autenticaron. Si no lo hacen a tiempo, el servidor las corta:
/// así nadie ocupa un lugar de la zona sin identificarse.
/// </summary>
public sealed class PendingConnections
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<long, double> _since = new();

    public int Count => _since.Count;

    public void Add(long peerId, double nowSeconds) => _since[peerId] = nowSeconds;

    /// <summary>Saca al peer de la lista. Devuelve false si no estaba (ya autenticado o desconocido).</summary>
    public bool TryTake(long peerId) => _since.Remove(peerId);

    public bool Contains(long peerId) => _since.ContainsKey(peerId);

    /// <summary>Saca y devuelve los peers que superaron el tiempo de espera.</summary>
    public IReadOnlyList<long> TakeExpired(double nowSeconds)
    {
        var expired = _since.Where(kv => nowSeconds - kv.Value > Timeout.TotalSeconds).Select(kv => kv.Key).ToList();
        foreach (var peer in expired)
            _since.Remove(peer);
        return expired;
    }
}
