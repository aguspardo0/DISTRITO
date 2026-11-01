using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>
/// Encargos personales por red (aprobado por el equipo, 2026-11-01). Tomar y cumplir pasos se hace hablando con la gente
/// (EconomyNetwork, opciones «encargo:…»). Acá queda lo demás: abandonar desde la libreta y avisarle al backend lo que
/// pasa en el mundo (asaltos y minutos de vigilancia), con un aviso al jugador cuando avanza.
/// </summary>
public partial class EncargoNetwork : Node
{
    private const double VigilTickSeconds = 30;

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private double _clock, _nextVigil = VigilTickSeconds;

    /// <summary>Avisos para el jugador (se muestran en pantalla y en el seguimiento).</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Locales que se pueden vigilar (los de las familias en el Mercado).</summary>
    private static readonly string[] Watchable = [EconomyCatalog.DepositNpc, EconomyCatalog.WorkshopNpc, EconomyCatalog.ValentiOfficeNpc, CasinoRules.Npc];

    public void Initialize(GameNetwork network, EconomyNetwork economy, Distrito.Game.Pve.PveNetwork pve)
    {
        _network = network;
        _economy = economy;
        pve.MissionEvent += (peer, kind, npc) =>
        {
            // Los asaltos a NPC cuentan para los encargos de asalto: camión, cobrador o la caja de un local.
            var target = kind switch
            {
                Distrito.Shared.Crews.MissionKinds.TruckJob => EncargoRules.Truck,
                EncargoRules.Collector => EncargoRules.Collector,
                Distrito.Shared.Crews.MissionKinds.Shop => npc,
                _ => null,
            };
            if (target is not null)
                _ = ProgressAsync(peer, EncargoKinds.Asalto, 1, target);
        };
    }

    // ================= Cliente → servidor =================

    public void Abandon(Guid encargo)
    {
        if (Multiplayer.IsServer())
            MessageReceived?.Invoke("Los encargos necesitan un servidor con cuentas.");
        else
            RpcId(1, MethodName.ServerAbandon, encargo.ToString());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerAbandon(string encargo)
    {
        if (Multiplayer.IsServer() && Guid.TryParse(encargo, out var id))
            _ = AbandonAsync(Multiplayer.GetRemoteSenderId(), id);
    }

    // ================= Servidor =================

    public override void _Process(double delta)
    {
        if (Multiplayer.MultiplayerPeer is null || !Multiplayer.IsServer() || _network.Backend is null)
            return;
        _clock += delta;
        if (_clock < _nextVigil)
            return;
        _nextVigil = _clock + VigilTickSeconds;
        // Vigilancia: cada medio minuto cerca de un local de las familias cuenta (si el jugador tiene ese encargo).
        foreach (var (peer, _) in _network.AuthenticatedPlayers)
        {
            if (_network.GetPlayer(peer) is not { Dead: false } p)
                continue;
            foreach (var code in Watchable)
                if (NpcInteractionRules.Find(code) is { } npc
                    && new Vector2(p.GlobalPosition.X - npc.Position.X, p.GlobalPosition.Z - npc.Position.Z).Length() <= EncargoRules.VigilRadius)
                    _ = ProgressAsync(peer, EncargoKinds.Vigilar, 1, code);
        }
    }

    private async Task AbandonAsync(long peer, Guid id)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var result = await api.AbandonEncargoAsync(_network.ServerKey, new EncargoAbandonRequest(me.CharacterId, id, Guid.NewGuid().ToString()));
        Send(peer, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
        _economy.RefreshPeer(peer);
    }

    private async Task ProgressAsync(long peer, string kind, int amount, string target)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var result = await api.EncargoProgressAsync(_network.ServerKey, new EncargoProgressRequest(me.CharacterId, kind, amount, Guid.NewGuid().ToString(), target));
        if (result is not { IsSuccess: true, Value.Ok: true })
            return;
        if (result.Value.Message.Length > 0)
        {
            GD.Print($"[DISTRITO] ENCARGO_PROGRESS {me.CharacterName}: {result.Value.Message}");
            Send(peer, result.Value.Message);
        }
        _economy.RefreshPeer(peer); // el seguimiento muestra el progreso
    }

    private void Send(long peer, string text)
    {
        if (Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveMessage, text);
    }

    // ================= Servidor → cliente =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);
}
