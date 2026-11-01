using System.Text.Json;
using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Casino;

/// <summary>Casino por red: el cliente elige la jugada; el servidor valida que esté en la puerta y el backend tira el azar.</summary>
public partial class CasinoNetwork : Node
{
    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private readonly HashSet<long> _busy = new();

    public event Action<CasinoResult>? ResultReceived;
    public event Action<string>? MessageReceived;

    public void Initialize(GameNetwork network, EconomyNetwork economy)
    {
        _network = network;
        _economy = economy;
    }

    public void Roulette(long stake, string bet, int number) => Send(MethodName.ServerPlay, "ruleta", stake, bet, number);
    public void Dice(long stake) => Send(MethodName.ServerPlay, "dados", stake, "", 0);
    public void Blackjack(string action, long stake) => Send(MethodName.ServerPlay, "blackjack", stake, action, 0);

    private void Send(StringName method, params Variant[] args)
    {
        if (Multiplayer.IsServer())
            MessageReceived?.Invoke("El casino necesita un servidor con cuentas.");
        else
            RpcId(1, method, args);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerPlay(string game, long stake, string choice, int number)
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        if (_busy.Add(peer))
            _ = PlayAsync(peer, game ?? "", stake, choice ?? "", number);
    }

    private async Task PlayAsync(long peer, string game, long stake, string choice, int number)
    {
        try
        {
            if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me) || _network.GetPlayer(peer) is not { } p)
                return;
            if (!NpcInteractionRules.InRange(new System.Numerics.Vector3(p.GlobalPosition.X, p.GlobalPosition.Y, p.GlobalPosition.Z), NpcInteractionRules.Casino))
            {
                RpcId(peer, MethodName.ReceiveMessage, "Para jugar tenés que estar en el casino.");
                return;
            }
            var id = Guid.NewGuid().ToString();
            var result = game switch
            {
                "ruleta" => await api.RouletteAsync(_network.ServerKey, new RouletteRequest(me.CharacterId, stake, choice, number, id)),
                "dados" => await api.DiceAsync(_network.ServerKey, new DiceRequest(me.CharacterId, stake, id)),
                _ => await api.BlackjackAsync(_network.ServerKey, new BlackjackRequest(me.CharacterId, choice, stake, id)),
            };
            if (!result.IsSuccess)
            {
                RpcId(peer, MethodName.ReceiveMessage, $"Hubo un problema: {result.Error!.Message}");
                return;
            }
            if (result.Value!.Ok)
                GD.Print($"[DISTRITO] CASINO {me.CharacterName} {game}: {result.Value.Message}");
            RpcId(peer, MethodName.ReceiveResult, JsonSerializer.Serialize(result.Value, DistritoApiClient.Json));
            _economy.RefreshPeer(peer);
        }
        finally
        {
            _busy.Remove(peer);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveResult(string json)
    {
        if (JsonSerializer.Deserialize<CasinoResult>(json, DistritoApiClient.Json) is { } r)
            ResultReceived?.Invoke(r);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);
}
