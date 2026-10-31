using System.Text.Json;
using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>
/// Interacción con NPCs y economía por red.
/// Cliente: pide hablar con un NPC y elige opciones. Nunca decide resultados.
/// Servidor: verifica sesión y distancia, arma el diálogo con <see cref="NpcInteractionRules"/>,
/// valida que la opción estuviera disponible y ejecuta la acción en el backend (transaccional).
/// </summary>
public partial class EconomyNetwork : Node
{
    private static readonly JsonSerializerOptions Json = DistritoApiClient.Json;

    private GameNetwork _network = null!;
    private readonly HashSet<long> _busy = new();

    // ---- Cliente ----
    public event Action<NpcDialog>? DialogReceived;
    public event Action<EconomySnapshot>? EconomyChanged;
    public EconomySnapshot? LastSnapshot { get; private set; }

    public void Initialize(GameNetwork network)
    {
        _network = network;
        _network.PlayerAdmitted += (peer, session) => _ = PushSnapshotAsync(peer, session.CharacterId);
    }

    // ================= Cliente → servidor =================

    // En modo anfitrión el servidor es este mismo proceso: se llama directo (un RPC a uno mismo no llega).
    public void Interact(string npcCode)
    {
        if (Multiplayer.IsServer())
            _ = HandleAsync(Multiplayer.GetUniqueId(), npcCode, optionId: null);
        else
            RpcId(1, MethodName.RequestInteract, npcCode);
    }

    public void Choose(string npcCode, string optionId)
    {
        if (Multiplayer.IsServer())
            _ = HandleAsync(Multiplayer.GetUniqueId(), npcCode, optionId);
        else
            RpcId(1, MethodName.RequestOption, npcCode, optionId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestInteract(string npcCode)
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        _ = HandleAsync(peer, npcCode, optionId: null);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestOption(string npcCode, string optionId)
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        _ = HandleAsync(peer, npcCode, optionId);
    }

    // ================= Servidor =================

    private async Task HandleAsync(long peer, string npcCode, string? optionId)
    {
        var npc = NpcInteractionRules.Find(npcCode);
        if (npc is null)
            return;

        if (_network.Backend is not { } backend || !_network.TryGetSession(peer, out var session))
        {
            SendDialog(peer, new NpcDialog(npc.Code, npc.Name, "Para hacer negocios necesitás entrar con una cuenta.", []));
            return;
        }

        var player = _network.GetPlayer(peer);
        if (player is null)
            return;
        var p = player.GlobalPosition;
        if (!NpcInteractionRules.InRange(new System.Numerics.Vector3(p.X, p.Y, p.Z), npc))
        {
            SendDialog(peer, new NpcDialog(npc.Code, npc.Name, "Acercate un poco más.", []));
            return;
        }

        // Una acción a la vez por jugador.
        if (!_busy.Add(peer))
            return;
        try
        {
            var snapshot = await backend.GetEconomyAsync(_network.ServerKey, session.CharacterId);
            if (!snapshot.IsSuccess)
            {
                SendDialog(peer, new NpcDialog(npc.Code, npc.Name, "No se pudo consultar tu situación. Probá de nuevo.", []));
                return;
            }

            if (optionId is null)
            {
                SendSnapshot(peer, snapshot.Value!);
                SendDialog(peer, NpcInteractionRules.DialogFor(npc.Code, snapshot.Value!));
                return;
            }

            var action = NpcInteractionRules.Resolve(npc.Code, optionId, snapshot.Value!);
            if (action is null)
            {
                SendDialog(peer, NpcInteractionRules.DialogFor(npc.Code, snapshot.Value!, "Esa opción ya no está disponible."));
                return;
            }

            var requestId = Guid.NewGuid().ToString();
            var result = action.Kind switch
            {
                NpcActionKind.AcceptContract => await backend.AcceptContractAsync(_network.ServerKey,
                    new AcceptContractRequest(session.CharacterId, action.Code, requestId)),
                NpcActionKind.Buy => await backend.BuyAsync(_network.ServerKey,
                    new BuyItemRequest(session.CharacterId, npc.Code, action.Code, 1, requestId)),
                NpcActionKind.BuyLicense => await backend.BusinessActionAsync(_network.ServerKey, "license",
                    new BusinessActionRequest(session.CharacterId, requestId)),
                NpcActionKind.Restock => await backend.BusinessActionAsync(_network.ServerKey, "restock",
                    new BusinessActionRequest(session.CharacterId, requestId)),
                NpcActionKind.Withdraw => await backend.BusinessActionAsync(_network.ServerKey, "withdraw",
                    new BusinessActionRequest(session.CharacterId, requestId)),
                _ => await backend.DeliverAsync(_network.ServerKey,
                    new DeliverContractRequest(session.CharacterId, action.ContractId!.Value, requestId)),
            };

            if (!result.IsSuccess)
            {
                SendDialog(peer, new NpcDialog(npc.Code, npc.Name, $"Hubo un problema: {result.Error!.Message}", []));
                return;
            }

            var outcome = result.Value!;
            GD.Print($"[DISTRITO] {session.CharacterName} → {npc.Name}: {action.Kind} ok={outcome.Ok} saldo={EconomyCatalog.FormatMoney(outcome.Snapshot.BalanceCents)}");
            SendSnapshot(peer, outcome.Snapshot);
            SendDialog(peer, NpcInteractionRules.DialogFor(npc.Code, outcome.Snapshot, outcome.Message));
        }
        finally
        {
            _busy.Remove(peer);
        }
    }

    private async Task PushSnapshotAsync(long peer, Guid characterId)
    {
        if (_network.Backend is not { } backend)
            return;
        var snapshot = await backend.GetEconomyAsync(_network.ServerKey, characterId);
        if (snapshot.IsSuccess && Multiplayer.GetPeers().Contains((int)peer))
            SendSnapshot(peer, snapshot.Value!);
    }

    private void SendDialog(long peer, NpcDialog dialog)
    {
        var ids = dialog.Options.Select(o => o.Id).ToArray();
        var labels = dialog.Options.Select(o => o.Label).ToArray();
        if (peer == Multiplayer.GetUniqueId())
            ReceiveDialog(dialog.NpcCode, dialog.Title, dialog.Text, ids, labels);
        else
            RpcId(peer, MethodName.ReceiveDialog, dialog.NpcCode, dialog.Title, dialog.Text, ids, labels);
    }

    private void SendSnapshot(long peer, EconomySnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, Json);
        if (peer == Multiplayer.GetUniqueId())
            ReceiveEconomy(json);
        else
            RpcId(peer, MethodName.ReceiveEconomy, json);
    }

    // ================= Servidor → cliente =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveDialog(string npcCode, string title, string text, string[] optionIds, string[] optionLabels)
    {
        var options = optionIds.Zip(optionLabels, (id, label) => new NpcOption(id, label)).ToList();
        DialogReceived?.Invoke(new NpcDialog(npcCode, title, text, options));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveEconomy(string snapshotJson)
    {
        var snapshot = JsonSerializer.Deserialize<EconomySnapshot>(snapshotJson, Json);
        if (snapshot is null)
            return;
        LastSnapshot = snapshot;
        EconomyChanged?.Invoke(snapshot);
    }
}
