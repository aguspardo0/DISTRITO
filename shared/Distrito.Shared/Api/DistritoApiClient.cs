using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;

namespace Distrito.Shared.Api;

/// <summary>Resultado de una llamada al backend: valor o error legible.</summary>
public sealed record ApiResult<T>(T? Value, HttpStatusCode Status, ApiError? Error)
{
    public bool IsSuccess => Error is null;

    public static ApiResult<T> Ok(T value, HttpStatusCode status) => new(value, status, null);
    public static ApiResult<T> Fail(HttpStatusCode status, ApiError error) => new(default, status, error);
}

/// <summary>
/// Cliente HTTP del backend. Lo usan el cliente del juego (login, personajes)
/// y el servidor de juego (API interna). Los tests del backend lo usan también,
/// así que el contrato queda probado de los dos lados.
/// </summary>
public sealed class DistritoApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<RegisterResponse>> RegisterAsync(string username, string password, CancellationToken ct = default) =>
        SendAsync<RegisterResponse>(HttpMethod.Post, "api/auth/register", new RegisterRequest(username, password), null, ct);

    public Task<ApiResult<LoginResponse>> LoginAsync(string username, string password, CancellationToken ct = default) =>
        SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", new LoginRequest(username, password), null, ct);

    public Task<ApiResult<List<CharacterSummary>>> ListCharactersAsync(string token, CancellationToken ct = default) =>
        SendAsync<List<CharacterSummary>>(HttpMethod.Get, "api/characters", null, r => Bearer(r, token), ct);

    public Task<ApiResult<CharacterSummary>> CreateCharacterAsync(string token, string name, CancellationToken ct = default) =>
        CreateCharacterAsync(token, name, CharacterLooks.Default, ct);

    public Task<ApiResult<CharacterSummary>> CreateCharacterAsync(string token, string name, string look, CancellationToken ct = default) =>
        SendAsync<CharacterSummary>(HttpMethod.Post, "api/characters", new CreateCharacterRequest(name, look), r => Bearer(r, token), ct);

    public Task<ApiResult<ResolvedSession>> ResolveSessionAsync(string serverKey, string token, Guid characterId, CancellationToken ct = default) =>
        SendAsync<ResolvedSession>(HttpMethod.Post, "internal/sessions/resolve", new ResolveSessionRequest(token, characterId),
            r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public async Task<ApiResult<bool>> SaveCharacterStateAsync(string serverKey, Guid characterId, CharacterState state, CancellationToken ct = default)
    {
        var result = await SendAsync<object>(HttpMethod.Put, $"internal/characters/{characterId}/state", state,
            r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);
        return result.IsSuccess ? ApiResult<bool>.Ok(true, result.Status) : ApiResult<bool>.Fail(result.Status, result.Error!);
    }

    // --- Economía (API interna) ---

    public Task<ApiResult<EconomySnapshot>> GetEconomyAsync(string serverKey, Guid characterId, CancellationToken ct = default) =>
        SendAsync<EconomySnapshot>(HttpMethod.Get, $"internal/economy/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> AcceptContractAsync(string serverKey, AcceptContractRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/economy/contracts/accept", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> BuyAsync(string serverKey, BuyItemRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/economy/buy", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> DeliverAsync(string serverKey, DeliverContractRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/economy/deliver", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> BusinessActionAsync(string serverKey, string action, BusinessActionRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, $"internal/economy/business/{action}", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    // --- Cuadrillas (API interna) ---

    public Task<ApiResult<CrewStatus>> GetCrewStatusAsync(string serverKey, Guid characterId, CancellationToken ct = default) =>
        SendAsync<CrewStatus>(HttpMethod.Get, $"internal/crews/status/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> CreateCrewAsync(string serverKey, CreateCrewRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/create", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> InviteToCrewAsync(string serverKey, InviteToCrewRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/invite", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> RespondCrewInvitationAsync(string serverKey, CrewInvitationResponse request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/respond", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> CrewMoneyAsync(string serverKey, bool deposit, CrewMoneyRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, deposit ? "internal/crews/deposit" : "internal/crews/withdraw", request,
            r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> InviteToCoalitionAsync(string serverKey, CoalitionInviteRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/coalition/invite", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> RespondCoalitionAsync(string serverKey, CoalitionResponseRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/coalition/respond", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> LeaveCoalitionAsync(string serverKey, CoalitionLeaveRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/coalition/leave", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> OpenCrewVoteAsync(string serverKey, CrewVoteOpenRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/vote/open", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> CastCrewVoteAsync(string serverKey, CrewVoteCastRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/vote/cast", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> SignPactAsync(string serverKey, PactRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/economy/pact", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> SetCrewSettingsAsync(string serverKey, CrewSettingsRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/settings", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> ExpandCrewAsync(string serverKey, ExpandCrewRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/expand", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> KickFromCrewAsync(string serverKey, KickFromCrewRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/kick", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> SetCrewRoleAsync(string serverKey, SetCrewRoleRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/role", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewActionResult>> LeaveCrewAsync(string serverKey, LeaveCrewRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/leave", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    private static void Bearer(HttpRequestMessage request, string token) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method, string path, object? body, Action<HttpRequestMessage>? configure, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        configure?.Invoke(request);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Fail(0, new ApiError("unreachable", $"No se pudo contactar al servidor de cuentas: {ex.Message}"));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ApiResult<T>.Fail(0, new ApiError("timeout", "El servidor de cuentas tardó demasiado en responder."));
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
                    return ApiResult<T>.Ok(default!, response.StatusCode);

                var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return value is null
                    ? ApiResult<T>.Fail(response.StatusCode, new ApiError("empty", "Respuesta vacía del servidor."))
                    : ApiResult<T>.Ok(value, response.StatusCode);
            }

            ApiError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiError>(Json, ct);
            }
            catch (JsonException)
            {
                // cuerpo no JSON: se usa el mensaje genérico
            }

            return ApiResult<T>.Fail(response.StatusCode,
                error ?? new ApiError("http_" + (int)response.StatusCode, $"Error del servidor ({(int)response.StatusCode})."));
        }
    }
}
