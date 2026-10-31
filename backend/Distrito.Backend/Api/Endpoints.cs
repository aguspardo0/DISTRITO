using System.Security.Cryptography;
using System.Text;
using Distrito.Backend.Accounts;
using Distrito.Backend.Characters;
using Distrito.Backend.Crews;
using Distrito.Backend.Economy;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Distrito.Shared.Api;

namespace Distrito.Backend.Api;

public static class Endpoints
{
    public static void MapDistritoApi(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);

        var characters = app.MapGroup("/api/characters");
        characters.MapGet("", ListCharacters);
        characters.MapPost("", CreateCharacter);

        var internalApi = app.MapGroup("/internal").AddEndpointFilter(RequireServerKey);
        internalApi.MapPost("/sessions/resolve", ResolveSession);
        internalApi.MapPut("/characters/{id:guid}/state", SaveCharacterState);
        internalApi.MapGet("/economy/{characterId:guid}", async (Guid characterId, EconomyService economy, CancellationToken ct) =>
            Results.Ok(await economy.GetSnapshotAsync(characterId, ct)));
        internalApi.MapPost("/economy/contracts/accept", (AcceptContractRequest r, EconomyService e, CancellationToken ct) => e.AcceptContractAsync(r, ct));
        internalApi.MapPost("/economy/buy", (BuyItemRequest r, EconomyService e, CancellationToken ct) => e.BuyAsync(r, ct));
        internalApi.MapGet("/crews/status/{characterId:guid}", async (Guid characterId, CrewService crews, CancellationToken ct) =>
            Results.Ok(await crews.GetStatusAsync(characterId, ct)));
        internalApi.MapPost("/crews/create", (CreateCrewRequest r, CrewService s, CancellationToken ct) => s.CreateAsync(r, ct));
        internalApi.MapPost("/crews/invite", (InviteToCrewRequest r, CrewService s, CancellationToken ct) => s.InviteAsync(r, ct));
        internalApi.MapPost("/crews/respond", (CrewInvitationResponse r, CrewService s, CancellationToken ct) => s.RespondAsync(r, ct));
        internalApi.MapPost("/crews/leave", (LeaveCrewRequest r, CrewService s, CancellationToken ct) => s.LeaveAsync(r, ct));
        internalApi.MapPost("/economy/business/license", (BusinessActionRequest r, EconomyService e, CancellationToken ct) => e.BuyCoffeeStandLicenseAsync(r, ct));
        internalApi.MapPost("/economy/business/restock", (BusinessActionRequest r, EconomyService e, CancellationToken ct) => e.RestockCoffeeStandAsync(r, ct));
        internalApi.MapPost("/economy/business/withdraw", (BusinessActionRequest r, EconomyService e, CancellationToken ct) => e.WithdrawCoffeeStandAsync(r, ct));
        internalApi.MapPost("/economy/deliver", (DeliverContractRequest r, EconomyService e, CancellationToken ct) => e.DeliverAsync(r, ct));
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new ApiError(code, message), statusCode: status);

    private static async Task<IResult> Register(RegisterRequest request, AccountStore accounts, CancellationToken ct)
    {
        var invalid = NameRules.ValidateUsername(request.Username) ?? NameRules.ValidatePassword(request.Password);
        if (invalid is not null)
            return Error(400, "invalid", invalid);

        var id = await accounts.RegisterAsync(request.Username, request.Password, ct);
        return id is null
            ? Error(409, "username_taken", "Ese usuario ya existe.")
            : Results.Created($"/api/accounts/{id}", new RegisterResponse(id.Value));
    }

    private static async Task<IResult> Login(LoginRequest request, AccountStore accounts, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            return Error(401, "bad_credentials", "Usuario o contraseña incorrectos.");

        var session = await accounts.LoginAsync(request.Username, request.Password, ct);
        return session is null
            ? Error(401, "bad_credentials", "Usuario o contraseña incorrectos.")
            : Results.Ok(new LoginResponse(session.Token, session.ExpiresAt));
    }

    private static async Task<Guid?> AccountFromBearer(HttpContext http, AccountStore accounts, CancellationToken ct)
    {
        var header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.Ordinal)
            ? await accounts.ResolveTokenAsync(header[prefix.Length..].Trim(), ct)
            : null;
    }

    private static async Task<IResult> ListCharacters(HttpContext http, AccountStore accounts, CharacterStore characters, CancellationToken ct)
    {
        var accountId = await AccountFromBearer(http, accounts, ct);
        return accountId is null
            ? Error(401, "unauthorized", "Sesión inválida o vencida.")
            : Results.Ok(await characters.ListAsync(accountId.Value, ct));
    }

    private static async Task<IResult> CreateCharacter(
        CreateCharacterRequest request, HttpContext http, AccountStore accounts, CharacterStore characters, CancellationToken ct)
    {
        var accountId = await AccountFromBearer(http, accounts, ct);
        if (accountId is null)
            return Error(401, "unauthorized", "Sesión inválida o vencida.");

        var invalid = NameRules.ValidateCharacterName(request.Name);
        if (invalid is not null)
            return Error(400, "invalid", invalid);

        var (outcome, character) = await characters.CreateAsync(accountId.Value, request.Name, ct);
        return outcome switch
        {
            CreateCharacterOutcome.Created => Results.Created($"/api/characters/{character!.Id}", character),
            CreateCharacterOutcome.NameTaken => Error(409, "name_taken", "Ya existe un personaje con ese nombre."),
            _ => Error(409, "limit_reached", $"Máximo {NameRules.MaxCharactersPerAccount} personajes por cuenta."),
        };
    }

    // --- API interna: solo servidores de juego con la clave compartida ---

    private static async ValueTask<object?> RequireServerKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Internal:ServerKey"];
        if (string.IsNullOrEmpty(expected))
            return Error(503, "internal_disabled", "La API interna no está configurada.");

        var provided = context.HttpContext.Request.Headers[ApiHeaders.ServerKey].ToString();
        var match = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        return match ? await next(context) : Error(401, "bad_server_key", "Clave de servidor inválida.");
    }

    private static async Task<IResult> ResolveSession(
        ResolveSessionRequest request, AccountStore accounts, CharacterStore characters, CancellationToken ct)
    {
        var accountId = await accounts.ResolveTokenAsync(request.Token, ct);
        if (accountId is null)
            return Error(401, "unauthorized", "Sesión inválida o vencida.");

        var session = await characters.LoadForAccountAsync(accountId.Value, request.CharacterId, ct);
        return session is null
            ? Error(404, "character_not_found", "El personaje no existe o no es de esta cuenta.")
            : Results.Ok(session);
    }

    private static async Task<IResult> SaveCharacterState(Guid id, CharacterState state, CharacterStore characters, CancellationToken ct)
    {
        if (!double.IsFinite(state.X) || !double.IsFinite(state.Y) || !double.IsFinite(state.Z) || !double.IsFinite(state.Yaw))
            return Error(400, "invalid", "Posición inválida.");

        return await characters.SaveStateAsync(id, state, ct)
            ? Results.NoContent()
            : Error(404, "character_not_found", "El personaje no existe.");
    }
}
