namespace Distrito.Shared.Api;

/// <summary>Un problema reportado desde el juego (F8): quién, dónde, en qué versión y qué pasó.</summary>
public sealed record BugReport(Guid? CharacterId, string Player, string Text, float X, float Z, string Zone, string Version,
    long Id = 0, DateTimeOffset? At = null)
{
    public const int MaxLength = 1000;
}
