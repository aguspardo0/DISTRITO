using System.Text.Json;
using Distrito.Game.Combat;
using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Admin;

/// <summary>
/// Panel de administración por red. El servidor pregunta al backend si la cuenta es admin (config/admins.json) antes
/// de cada acción; el backend vuelve a verificarlo en los regalos. Los clientes no pueden saltearse nada.
/// </summary>
public partial class AdminNetwork : Node
{
    public static readonly (string Name, Vector3 Spot)[] Places =
    [
        ("El Mercado (centro)", new Vector3(0, 0.2f, 0)),
        ("Almacén Moretti", new Vector3(-8, 0.2f, -5)),
        ("Oficina Valenti", new Vector3(-9, 0.2f, 0.5f)),
        ("Taller Caruso", new Vector3(10, 0.2f, 0)),
        ("Los Muelles", new Vector3(55, 0.2f, 0)),
        ("Casino El Trébol", new Vector3(24, 0.2f, 13.5f)),
        // Los barrios de las familias: en el bulevar, mirando hacia la plaza.
        .. Distrito.Server.Core.World.CityLayout.Barrios.Select(b => (b.Area.Name, ToGodot(b.ToWorld(20, 3)))),
    ];

    private static Vector3 ToGodot(System.Numerics.Vector2 p) => new(p.X, 0.2f, p.Y);

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private CombatNetwork _combat = null!;
    private Distrito.Game.Pve.PveNetwork? _pve;
    public void UsePve(Distrito.Game.Pve.PveNetwork pve) => _pve = pve;
    private Distrito.Game.Life.LifeNetwork? _life;
    public void UseLife(Distrito.Game.Life.LifeNetwork life) => _life = life;

    /// <summary>Llegó el informe de qué hace cada habitante y por qué.</summary>
    public event Action<string>? ResidentsReceived;
    public void RequestResidents() => Send(MethodName.ServerResidents);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerResidents()
    {
        if (Multiplayer.IsServer())
            _ = AsAdminAsync(Multiplayer.GetRemoteSenderId(), (peer, _) =>
            {
                if (Multiplayer.GetPeers().Contains((int)peer))
                    RpcId(peer, MethodName.ReceiveResidents, _life?.Report(DateTimeOffset.UtcNow) ?? "La vida de los NPC no está disponible.");
            });
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveResidents(string text) => ResidentsReceived?.Invoke(text);

    /// <summary>Los últimos problemas reportados con F8 (prueba cerrada).</summary>
    public event Action<string>? ReportsReceived;
    public void RequestReports() => Send(MethodName.ServerReports);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerReports()
    {
        if (Multiplayer.IsServer())
            _ = AsAdminAsync(Multiplayer.GetRemoteSenderId(), (peer, _) => { var send = SendReportsAsync(peer); });
    }

    private async Task SendReportsAsync(long peer)
    {
        if (_network.Backend is not { } api)
            return;
        var result = await api.GetBugReportsAsync(_network.ServerKey, 60);
        var text = !result.IsSuccess ? $"No se pudieron leer: {result.Error!.Message}"
            : result.Value!.Count == 0 ? "Todavía nadie reportó nada."
            : string.Join("\n\n", result.Value.Select(r =>
                $"#{r.Id} · {r.At?.ToOffset(TimeSpan.FromHours(-3)):dd/MM HH:mm} · {r.Player} · {r.Zone} ({r.X:0}, {r.Z:0}) · versión {r.Version}\n{r.Text}"));
        if (Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveReports, text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveReports(string text) => ReportsReceived?.Invoke(text);

    /// <summary>Prueba de reacciones (etapa 7): un tiro al aire donde está el administrador, sin daño.</summary>
    public void RequestTestShot() => Send(MethodName.ServerTestShot);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerTestShot()
    {
        if (Multiplayer.IsServer())
            _ = AsAdminAsync(Multiplayer.GetRemoteSenderId(), (peer, _) =>
            {
                if (_network.GetPlayer(peer) is not { } me || _life is null)
                    return;
                _life.OnIncident(Distrito.Shared.Life.IncidentKinds.Shot, me.GlobalPosition.X, me.GlobalPosition.Z, DateTimeOffset.UtcNow);
                if (Multiplayer.GetPeers().Contains((int)peer))
                    RpcId(peer, MethodName.ReceiveResidents, _life.Report(DateTimeOffset.UtcNow));
            });
    }

    public event Action? StatusReceived;
    public event Action<string>? MessageReceived;
    public AdminView? Status { get; private set; }

    public void Initialize(GameNetwork network, EconomyNetwork economy, CombatNetwork combat)
    {
        _network = network;
        _economy = economy;
        _combat = combat;
    }

    public void RequestStatus() => Send(MethodName.ServerStatus);
    public void Grant(long cents, string itemCode, int quantity, int level) => Send(MethodName.ServerGrant, cents, itemCode, quantity, level);
    public void Teleport(int place) => Send(MethodName.ServerTeleport, place);
    public void Heal() => Send(MethodName.ServerHeal);
    public void Tool(string tool, int amount) => Send(MethodName.ServerTool, tool, amount);

    private void Send(StringName method, params Variant[] args)
    {
        if (Multiplayer.IsServer())
            MessageReceived?.Invoke("El panel de administración necesita un servidor con cuentas.");
        else
            RpcId(1, method, args);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerStatus()
    {
        if (Multiplayer.IsServer())
            _ = StatusAsync(Multiplayer.GetRemoteSenderId());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerGrant(long cents, string itemCode, int quantity, int level)
    {
        if (Multiplayer.IsServer())
            _ = GrantAsync(Multiplayer.GetRemoteSenderId(), cents, itemCode ?? "", quantity, level);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerTeleport(int place)
    {
        if (Multiplayer.IsServer())
            _ = AsAdminAsync(Multiplayer.GetRemoteSenderId(), (peer, _) =>
            {
                if (place < 0 || place >= Places.Length || _network.GetPlayer(peer) is not { } player)
                    return;
                player.Velocity = Vector3.Zero;
                player.GlobalPosition = Places[place].Spot;
                GD.Print($"[DISTRITO] ADMIN teletransporte de {player.DisplayName} a {Places[place].Name}");
                SendMessage(peer, $"Fuiste a {Places[place].Name}.");
            });
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerHeal()
    {
        if (Multiplayer.IsServer())
            _ = AsAdminAsync(Multiplayer.GetRemoteSenderId(), (peer, _) =>
            {
                _combat.AdminHeal(peer);
                SendMessage(peer, "Salud al máximo.");
            });
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerTool(string tool, int amount)
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        tool ??= "";
        if (tool.StartsWith("evento-"))
        {
            _ = AsAdminAsync(peer, (p, _) =>
            {
                var text = _pve?.ForceEvent(tool["evento-".Length..]) ?? "Los eventos no están disponibles.";
                GD.Print($"[DISTRITO] ADMIN evento {tool}: {text}");
                SendMessage(p, text);
            });
            return;
        }
        _ = ToolAsync(peer, tool, amount);
    }

    private async Task ToolAsync(long peer, string tool, int amount)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var result = await api.AdminToolAsync(_network.ServerKey, new AdminToolRequest(me.CharacterId, tool, amount, Guid.NewGuid().ToString()));
        SendMessage(peer, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
        if (result is { IsSuccess: true, Value.Ok: true })
            GD.Print($"[DISTRITO] ADMIN {me.CharacterName}: {result.Value.Message}");
        _economy.RefreshPeer(peer);
    }

    private async Task AsAdminAsync(long peer, Action<long, Guid> action)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var status = await api.GetAdminAsync(_network.ServerKey, me.CharacterId);
        if (status is not { IsSuccess: true, Value.IsAdmin: true })
        {
            SendMessage(peer, "Tu cuenta no es de administración.");
            return;
        }
        action(peer, me.CharacterId);
    }

    private async Task StatusAsync(long peer)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "El panel de administración necesita un servidor con cuentas.");
            return;
        }
        var status = await api.GetAdminAsync(_network.ServerKey, me.CharacterId);
        if (status.IsSuccess && Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveStatus, JsonSerializer.Serialize(status.Value, DistritoApiClient.Json));
    }

    private async Task GrantAsync(long peer, long cents, string itemCode, int quantity, int level)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var result = await api.AdminGrantAsync(_network.ServerKey,
            new AdminGrantRequest(me.CharacterId, cents, itemCode.Length > 0 ? itemCode : null, quantity, level > 0 ? level : null, Guid.NewGuid().ToString()));
        SendMessage(peer, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
        if (result is { IsSuccess: true, Value.Ok: true })
            GD.Print($"[DISTRITO] ADMIN {me.CharacterName}: {result.Value.Message}");
        _economy.RefreshPeer(peer);
    }

    private void SendMessage(long peer, string text)
    {
        if (Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveMessage, text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveStatus(string json)
    {
        Status = JsonSerializer.Deserialize<AdminView>(json, DistritoApiClient.Json);
        StatusReceived?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);

    /// <summary>Solo para la prueba visual.</summary>
    public void Preview()
    {
        Status = new AdminView(true, [new("pistola", "Pistola", 0), new("bala-pistola", "Bala de pistola", 0), new("bolsa-cafe", "Bolsa de café", 0)]);
        StatusReceived?.Invoke();
    }
}
