using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Shared.Progression;
using Godot;

namespace Distrito.Game.Player;

/// <summary>Árbol de habilidades por red: el cliente pide aprender un nodo; el backend valida puntos, nivel y requisitos.</summary>
public partial class SkillNetwork : Node
{
    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private readonly HashSet<long> _busy = new();

    public event Action<string>? MessageReceived;

    public void Initialize(GameNetwork network, EconomyNetwork economy)
    {
        _network = network;
        _economy = economy;
    }

    public void Learn(string node)
    {
        if (Multiplayer.IsServer())
            MessageReceived?.Invoke("Las habilidades necesitan un servidor con cuentas.");
        else
            RpcId(1, MethodName.ServerLearn, node);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerLearn(string node)
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        if (_busy.Add(peer))
            _ = LearnAsync(peer, node ?? "");
    }

    private async Task LearnAsync(long peer, string node)
    {
        try
        {
            if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
                return;
            var result = await api.LearnSkillAsync(_network.ServerKey, new LearnSkillRequest(me.CharacterId, node, Guid.NewGuid().ToString()));
            RpcId(peer, MethodName.ReceiveMessage, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
            if (result is { IsSuccess: true, Value.Ok: true })
                GD.Print($"[DISTRITO] SKILL {me.CharacterName}: {node}");
            _economy.RefreshPeer(peer); // la economía trae los rangos (y el servidor de juego aplica los efectos)
        }
        finally
        {
            _busy.Remove(peer);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);
}
