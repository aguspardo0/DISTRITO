namespace Distrito.Shared.Networking;

/// <summary>
/// Valores iniciales de red. Son puntos de partida a medir, no definitivos (ver docs/Architecture/NETWORKING.md).
/// </summary>
public static class NetworkDefaults
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 7777;
    public const int MaxPlayersPerZone = 64;
    public const int ServerTickRate = 30;

    /// <summary>Radio de interés (metros): más lejos que esto, un jugador no recibe a otro. A medir.</summary>
    public const float InterestRadius = 60f;
}
