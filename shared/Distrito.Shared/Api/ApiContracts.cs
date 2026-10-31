namespace Distrito.Shared.Api;

// Mensajes HTTP entre cliente / servidor de juego y backend. Se serializan en JSON (camelCase).

public sealed record RegisterRequest(string Username, string Password);

public sealed record RegisterResponse(Guid AccountId);

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

public sealed record CreateCharacterRequest(string Name, string Look = CharacterLooks.Default);

public sealed record CharacterSummary(Guid Id, string Name, string ZoneId, string Look = CharacterLooks.Default);

public sealed record ApiError(string Code, string Message);

// --- API interna (servidor de juego → backend) ---

public sealed record ResolveSessionRequest(string Token, Guid CharacterId);

public sealed record CharacterState(double X, double Y, double Z, double Yaw);

public sealed record ResolvedSession(Guid AccountId, Guid CharacterId, string CharacterName, string ZoneId, CharacterState State, string Look = CharacterLooks.Default);

public static class ApiHeaders
{
    public const string ServerKey = "X-Distrito-Server-Key";
}

public static class ZoneIds
{
    /// <summary>Único barrio del prototipo.</summary>
    public const string TestNeighborhood = "barrio-prueba";

    /// <summary>Dónde aparece un personaje recién creado.</summary>
    public static readonly CharacterState NewCharacterSpawn = new(3, 1, 0, 0);
}
