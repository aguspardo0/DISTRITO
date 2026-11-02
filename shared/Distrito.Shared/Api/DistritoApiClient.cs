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

    public Task<ApiResult<EconomyActionResult>> LearnSkillAsync(string serverKey, Distrito.Shared.Progression.LearnSkillRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/skills/learn", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> SellAsync(string serverKey, SellItemRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/economy/sell", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

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

    public Task<ApiResult<CrewActionResult>> DeclareWarAsync(string serverKey, DeclareWarRequest request, CancellationToken ct = default) =>
        SendAsync<CrewActionResult>(HttpMethod.Post, "internal/crews/war", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CombatStateView>> GetCombatStateAsync(string serverKey, CancellationToken ct = default) =>
        SendAsync<CombatStateView>(HttpMethod.Get, "internal/combat/state", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<List<DropView>>> GetDropsAsync(string serverKey, CancellationToken ct = default) =>
        SendAsync<List<DropView>>(HttpMethod.Get, "internal/combat/drops", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<DropView>> KnockoutAsync(string serverKey, KnockoutRequest request, CancellationToken ct = default) =>
        SendAsync<DropView>(HttpMethod.Post, "internal/combat/knockout", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<int>> UseAmmoAsync(string serverKey, AmmoUseRequest request, CancellationToken ct = default) =>
        SendAsync<int>(HttpMethod.Post, "internal/combat/ammo", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<InventoryView>> GetInventoryAsync(string serverKey, Guid characterId, CancellationToken ct = default) =>
        SendAsync<InventoryView>(HttpMethod.Get, $"internal/inventory/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    /// <summary>Depósito de la cuadrilla del personaje; Value null si no está en ninguna.</summary>
    public Task<ApiResult<WarehouseView>> GetWarehouseAsync(string serverKey, Guid characterId, CancellationToken ct = default) =>
        SendAsync<WarehouseView>(HttpMethod.Get, $"internal/warehouse/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> OpenWarehouseAsync(string serverKey, WarehouseOpenRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/warehouse/open", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> MoveWarehouseAsync(string serverKey, WarehouseMoveRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/warehouse/move", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> SetWarehousePermissionsAsync(string serverKey, WarehousePermissionsRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/warehouse/permissions", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<SupplierView>> GetSupplierAsync(string serverKey, Guid characterId, string supplierCode, CancellationToken ct = default) =>
        SendAsync<SupplierView>(HttpMethod.Get, $"internal/supplier/{characterId}?npc={Uri.EscapeDataString(supplierCode)}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> PlaceSupplierOrderAsync(string serverKey, SupplierOrderRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/supplier/order", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> CancelSupplierOrderAsync(string serverKey, SupplierOrderActionRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/supplier/cancel", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> PickupSupplierOrderAsync(string serverKey, SupplierOrderActionRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/supplier/pickup", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<AdminView>> GetAdminAsync(string serverKey, Guid characterId, CancellationToken ct = default) =>
        SendAsync<AdminView>(HttpMethod.Get, $"internal/admin/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> AdminToolAsync(string serverKey, AdminToolRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/admin/tool", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> AdminGrantAsync(string serverKey, AdminGrantRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/admin/grant", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CasinoResult>> RouletteAsync(string serverKey, RouletteRequest request, CancellationToken ct = default) =>
        SendAsync<CasinoResult>(HttpMethod.Post, "internal/casino/ruleta", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CasinoResult>> DiceAsync(string serverKey, DiceRequest request, CancellationToken ct = default) =>
        SendAsync<CasinoResult>(HttpMethod.Post, "internal/casino/dados", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CasinoResult>> BlackjackAsync(string serverKey, BlackjackRequest request, CancellationToken ct = default) =>
        SendAsync<CasinoResult>(HttpMethod.Post, "internal/casino/blackjack", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<CrewBoardView>> GetCrewBoardAsync(string serverKey, Guid characterId, CancellationToken ct = default) =>
        SendAsync<CrewBoardView>(HttpMethod.Get, $"internal/crews/board/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<List<CrewBaseView>>> GetCrewBasesAsync(string serverKey, CancellationToken ct = default) =>
        SendAsync<List<CrewBaseView>>(HttpMethod.Get, "internal/crews/bases", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> BuyCrewBaseAsync(string serverKey, CrewBaseBuyRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/crews/base", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> TurnInCrewMissionAsync(string serverKey, CrewMissionTurnInRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/crews/missions/turnin", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> FoundFamilyAsync(string serverKey, FoundFamilyRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/families/found", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> StartCrewOperationAsync(string serverKey, CrewOperationStartRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/crews/operation/start", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> EncargoActionAsync(string serverKey, EncargoActionRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/encargos/accion", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> AbandonEncargoAsync(string serverKey, EncargoAbandonRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/encargos/abandonar", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> EncargoProgressAsync(string serverKey, EncargoProgressRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/encargos/progreso", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> CrewMissionProgressAsync(string serverKey, CrewMissionProgressRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/crews/missions/progress", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<int>> PveLoadAsync(string serverKey, PveLoadRequest request, CancellationToken ct = default) =>
        SendAsync<int>(HttpMethod.Post, "internal/pve/load", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<object>> PveUnloadAsync(string serverKey, PveUnloadRequest request, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Post, "internal/pve/unload", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<DropItemResult>> PveRobGoodsAsync(string serverKey, PveRobGoodsRequest request, CancellationToken ct = default) =>
        SendAsync<DropItemResult>(HttpMethod.Post, "internal/pve/rob-goods", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> PveRobCashAsync(string serverKey, PveRobCashRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/pve/rob-cash", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<DropView>> PveContrabandAsync(string serverKey, PveContrabandRequest request, CancellationToken ct = default) =>
        SendAsync<DropView>(HttpMethod.Post, "internal/pve/contraband", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> UpgradeBusinessAsync(string serverKey, BusinessUpgradeRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/economy/business/upgrade", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> SetBusinessPriceAsync(string serverKey, BusinessPriceRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/economy/business/price", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<List<PriceBoardView>>> GetMarketBoardsAsync(string serverKey, CancellationToken ct = default) =>
        SendAsync<List<PriceBoardView>>(HttpMethod.Get, "internal/market/boards", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<object>> LookAtBoardAsync(string serverKey, MarketLookRequest request, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Post, "internal/market/look", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<MarketInfoView>> GetMarketInfoAsync(string serverKey, Guid characterId, CancellationToken ct = default) =>
        SendAsync<MarketInfoView>(HttpMethod.Get, $"internal/market/info/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> BuyMarketInfoAsync(string serverKey, MarketBuyRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/market/buy", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<WorldStockView>> GetWorldStockAsync(string serverKey, CancellationToken ct = default) =>
        SendAsync<WorldStockView>(HttpMethod.Get, "internal/world/stock", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<List<VehicleView>>> GetVehiclesAsync(string serverKey, CancellationToken ct = default) =>
        SendAsync<List<VehicleView>>(HttpMethod.Get, "internal/vehicles", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<VehicleBuyResult>> BuyVehicleAsync(string serverKey, VehicleBuyRequest request, CancellationToken ct = default) =>
        SendAsync<VehicleBuyResult>(HttpMethod.Post, "internal/vehicles/buy", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<object>> ParkVehicleAsync(string serverKey, VehicleParkRequest request, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Post, "internal/vehicles/park", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<VehicleCargoView>> GetVehicleCargoAsync(string serverKey, Guid vehicleId, Guid characterId, CancellationToken ct = default) =>
        SendAsync<VehicleCargoView>(HttpMethod.Get, $"internal/vehicles/{vehicleId}/cargo/{characterId}", null, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> MoveVehicleCargoAsync(string serverKey, VehicleCargoRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/vehicles/cargo", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<DropItemResult>> ForceVehicleAsync(string serverKey, VehicleForceRequest request, CancellationToken ct = default) =>
        SendAsync<DropItemResult>(HttpMethod.Post, "internal/vehicles/force", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> GiveItemAsync(string serverKey, GiveItemRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/inventory/give", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<DropItemResult>> DropItemAsync(string serverKey, DropItemRequest request, CancellationToken ct = default) =>
        SendAsync<DropItemResult>(HttpMethod.Post, "internal/inventory/drop", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

    public Task<ApiResult<EconomyActionResult>> PickupAsync(string serverKey, PickupRequest request, CancellationToken ct = default) =>
        SendAsync<EconomyActionResult>(HttpMethod.Post, "internal/combat/pickup", request, r => r.Headers.Add(ApiHeaders.ServerKey, serverKey), ct);

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
