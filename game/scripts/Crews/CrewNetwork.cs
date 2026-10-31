using System.Text.Json;
using Distrito.Game.Network;
using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Godot;

namespace Distrito.Game.Crews;

/// <summary>Candidato a invitar: jugador con cuenta conectado en la zona.</summary>
public sealed record CrewCandidate(long Peer, string Name);

/// <summary>
/// Cuadrillas por red. El cliente pide acciones; el servidor identifica al personaje por su sesión
/// (el cliente nunca manda ids de personaje), llama al backend y avisa a los afectados.
/// </summary>
public partial class CrewNetwork : Node
{
    private static readonly JsonSerializerOptions Json = DistritoApiClient.Json;

    private GameNetwork _network = null!;
    private readonly HashSet<long> _busy = new();

    // ---- Cliente ----
    public event Action<CrewStatus, IReadOnlyList<CrewCandidate>>? StatusReceived;
    public event Action<string>? MessageReceived;
    public CrewStatus? LastStatus { get; private set; }
    public IReadOnlyList<CrewCandidate> LastCandidates { get; private set; } = [];

    public void Initialize(GameNetwork network)
    {
        _network = network;
        _network.PlayerAdmitted += (peer, session) => _ = RefreshPeerAsync(peer);
    }

    // ================= Cliente → servidor =================

    public void RequestPanel() => Send(MethodName.ServerPanel);
    public void Create(string name) => Send(MethodName.ServerCreate, name);
    public void Invite(long targetPeer) => Send(MethodName.ServerInvite, targetPeer);
    public void Respond(Guid crewId, bool accept) => Send(MethodName.ServerRespond, crewId.ToString(), accept);
    public void Leave() => Send(MethodName.ServerLeave);
    public void Kick(Guid characterId) => Send(MethodName.ServerKick, characterId.ToString());
    public void Expand() => Send(MethodName.ServerExpand);
    public void Deposit(long cents) => Send(MethodName.ServerMoney, true, cents);
    public void Withdraw(long cents) => Send(MethodName.ServerMoney, false, cents);
    public void SetSettings(CrewPermissions p) =>
        Send(MethodName.ServerSettings, p.SecondCanWithdraw, p.SecondCanInvite, p.MemberCanInvite, p.TithePercent);
    public void SetRole(Guid characterId, string role) => Send(MethodName.ServerSetRole, characterId.ToString(), role);

    private void Send(StringName method, params Variant[] args)
    {
        if (Multiplayer.IsServer())
            MessageReceived?.Invoke("Las cuadrillas necesitan un servidor con cuentas.");
        else
            RpcId(1, method, args);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerPanel() => Handle(api => Task.FromResult<ApiResult<CrewActionResult>?>(null));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerCreate(string name) =>
        Handle(async (api, me) => (ApiResult<CrewActionResult>?)await api.CreateCrewAsync(_network.ServerKey, new CreateCrewRequest(me, name.Trim())));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerInvite(long targetPeer) =>
        Handle(async (api, me) =>
        {
            if (!_network.TryGetSession(targetPeer, out var target))
                return null;
            var result = await api.InviteToCrewAsync(_network.ServerKey, new InviteToCrewRequest(me, target.CharacterId));
            if (result.IsSuccess && result.Value!.Ok)
            {
                await RefreshPeerAsync(targetPeer);
                SendMessage(targetPeer, $"Te invitaron a la cuadrilla {result.Value.Status.CrewName}. Abrí el panel con C.");
            }
            return result;
        });

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerRespond(string crewId, bool accept) =>
        Handle(async (api, me) => Guid.TryParse(crewId, out var id)
            ? await api.RespondCrewInvitationAsync(_network.ServerKey, new CrewInvitationResponse(me, id, accept))
            : null);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerLeave() =>
        Handle(async (api, me) => (ApiResult<CrewActionResult>?)await api.LeaveCrewAsync(_network.ServerKey, new LeaveCrewRequest(me)));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerMoney(bool deposit, long cents) =>
        Handle(async (api, me) => (ApiResult<CrewActionResult>?)await api.CrewMoneyAsync(_network.ServerKey, deposit,
            new CrewMoneyRequest(me, Math.Max(0, cents), Guid.NewGuid().ToString())));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerSettings(bool secondCanWithdraw, bool secondCanInvite, bool memberCanInvite, int tithePercent) =>
        Handle(async (api, me) => (ApiResult<CrewActionResult>?)await api.SetCrewSettingsAsync(_network.ServerKey,
            new CrewSettingsRequest(me, new CrewPermissions(secondCanWithdraw, secondCanInvite, memberCanInvite, tithePercent))));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerExpand() =>
        Handle(async (api, me) => (ApiResult<CrewActionResult>?)await api.ExpandCrewAsync(_network.ServerKey, new ExpandCrewRequest(me)));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerKick(string characterId) =>
        Handle(async (api, me) => Guid.TryParse(characterId, out var target)
            ? await api.KickFromCrewAsync(_network.ServerKey, new KickFromCrewRequest(me, target))
            : null);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerSetRole(string characterId, string role) =>
        Handle(async (api, me) => Guid.TryParse(characterId, out var target)
            ? await api.SetCrewRoleAsync(_network.ServerKey, new SetCrewRoleRequest(me, target, role))
            : null);

    // ================= Servidor =================

    private void Handle(Func<DistritoApiClient, Task<ApiResult<CrewActionResult>?>> action) =>
        Handle((api, _) => action(api));

    private void Handle(Func<DistritoApiClient, Guid, Task<ApiResult<CrewActionResult>?>> action)
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        _ = HandleAsync(peer, action);
    }

    private async Task HandleAsync(long peer, Func<DistritoApiClient, Guid, Task<ApiResult<CrewActionResult>?>> action)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "Para formar cuadrillas necesitás entrar con una cuenta.");
            return;
        }
        if (!_busy.Add(peer))
            return;
        try
        {
            // Miembros antes de la acción: si alguien se va, también hay que avisarles.
            var before = await api.GetCrewStatusAsync(_network.ServerKey, me.CharacterId);
            var result = await action(api, me.CharacterId);

            if (result is not null)
            {
                if (!result.IsSuccess)
                    SendMessage(peer, $"Hubo un problema: {result.Error!.Message}");
                else
                {
                    SendMessage(peer, result.Value!.Message);
                    GD.Print($"[DISTRITO] Cuadrilla — {me.CharacterName}: {result.Value.Message} (ok={result.Value.Ok})");
                }
            }

            var affected = new HashSet<Guid>();
            if (before.IsSuccess)
                foreach (var m in before.Value!.Members) affected.Add(m.CharacterId);
            if (result is { IsSuccess: true })
                foreach (var m in result.Value!.Status.Members) affected.Add(m.CharacterId);
            affected.Add(me.CharacterId);

            foreach (var characterId in affected)
                if (_network.PeerOfCharacter(characterId) is { } affectedPeer)
                    await RefreshPeerAsync(affectedPeer);
        }
        finally
        {
            _busy.Remove(peer);
        }
    }

    /// <summary>Consulta el estado de cuadrilla de un jugador, actualiza su nombre visible y se lo manda.</summary>
    private async Task RefreshPeerAsync(long peer)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var session))
            return;
        var status = await api.GetCrewStatusAsync(_network.ServerKey, session.CharacterId);
        if (!status.IsSuccess || !Multiplayer.GetPeers().Contains((int)peer))
            return;

        if (_network.GetPlayer(peer) is { } player)
            player.CrewName = status.Value!.CrewName is { } name
                ? (status.Value.IsFormed ? name : $"{name} (en formación)")
                : "";

        var members = status.Value!.Members.Select(m => m.CharacterId).ToHashSet();
        var candidates = _network.AuthenticatedPlayers
            .Where(p => p.Peer != peer && !members.Contains(p.Session.CharacterId))
            .Select(p => new CrewCandidate(p.Peer, p.Session.CharacterName))
            .ToList();

        RpcId(peer, MethodName.ReceiveStatus, JsonSerializer.Serialize(status.Value, Json),
            candidates.Select(c => c.Peer).ToArray(), candidates.Select(c => c.Name).ToArray());
    }

    private void SendMessage(long peer, string text) => RpcId(peer, MethodName.ReceiveMessage, text);

    // ================= Servidor → cliente =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveStatus(string statusJson, long[] candidatePeers, string[] candidateNames)
    {
        var status = JsonSerializer.Deserialize<CrewStatus>(statusJson, Json);
        if (status is null)
            return;
        LastStatus = status;
        LastCandidates = candidatePeers.Zip(candidateNames, (p, n) => new CrewCandidate(p, n)).ToList();
        StatusReceived?.Invoke(status, LastCandidates);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);
}
