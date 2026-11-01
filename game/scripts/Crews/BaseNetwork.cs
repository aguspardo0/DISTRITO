using System.Text.Json;
using Distrito.Game.Combat;
using Distrito.Game.Economy;
using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Api;
using Distrito.Shared.Crews;
using Godot;

namespace Distrito.Game.Crews;

/// <summary>
/// Sedes, prestigio y misiones de cuadrilla por red (aprobado por el equipo 2026-11-01). El servidor sabe qué galpón es de
/// quién (zona segura adentro, reaparición, carteles), valida dónde está cada uno para comprar y cumplir misiones, y le
/// avisa al backend lo que pasa en el mundo (asaltos, guardias, vigilancia) para el progreso de las misiones.
/// </summary>
public partial class BaseNetwork : Node
{
    private const double RefreshSeconds = 10;
    private const double GuardTickSeconds = 30;

    private static BaseNetwork? _instance;
    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private CombatNetwork _combat = null!;
    private List<CrewBaseView> _bases = [];
    private readonly HashSet<long> _busy = new();
    private double _clock, _nextRefresh, _nextGuardTick = GuardTickSeconds;

    public event Action? BoardReceived;
    public event Action<string>? MessageReceived;
    public CrewBoardView? Board { get; private set; }

    /// <summary>Galpón de la sede de una cuadrilla (por nombre), o null.</summary>
    public static int? ShedOf(string crewName) =>
        _instance?._bases.FirstOrDefault(b => b.CrewName == crewName)?.Shed;

    public static IReadOnlyList<CrewBaseView> Bases => _instance?._bases ?? [];

    /// <summary>Nombre de cuadrilla del jugador (sin «(en formación)»).</summary>
    private static string CrewOf(Distrito.Game.Player.PlayerCharacter p) =>
        p.CrewName.Replace(" (en formación)", "");

    public void Initialize(GameNetwork network, EconomyNetwork economy, CombatNetwork combat, Distrito.Game.Pve.PveNetwork pve)
    {
        _instance = this;
        _network = network;
        _economy = economy;
        _combat = combat;
        _combat.RespawnPoint = RespawnPointOf;
        pve.MissionEvent += (peer, kind, npc) => _ = ProgressAsync(peer, kind, 1, npc);
        _network.PlayerAdmitted += (peer, _) => SendBasesTo(peer);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    // ================= Cliente → servidor =================

    public void RequestBoard() => Send(MethodName.ServerBoard);
    public void BuyBase(int shed) => Send(MethodName.ServerBuyBase, shed);
    public void TurnIn(Guid mission) => Send(MethodName.ServerTurnIn, mission.ToString());
    public void StartOperation() => Send(MethodName.ServerStartOperation);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerStartOperation()
    {
        if (Multiplayer.IsServer())
            _ = GuardAsync(Multiplayer.GetRemoteSenderId(), StartOperationAsync);
    }

    private async Task StartOperationAsync(long peer)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var result = await api.StartCrewOperationAsync(_network.ServerKey, new CrewOperationStartRequest(me.CharacterId, Guid.NewGuid().ToString()));
        SendMessage(peer, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
        if (result is { IsSuccess: true, Value.Ok: true })
            GD.Print($"[DISTRITO] CREW_OPERATION {me.CharacterName}: {result.Value.Message}");
        await SendBoardAsync(peer);
    }

    private void Send(StringName method, params Variant[] args)
    {
        if (Multiplayer.IsServer())
            MessageReceived?.Invoke("La cuadrilla necesita un servidor con cuentas.");
        else
            RpcId(1, method, args);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerBoard()
    {
        if (Multiplayer.IsServer())
            _ = SendBoardAsync(Multiplayer.GetRemoteSenderId());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerBuyBase(int shed)
    {
        if (Multiplayer.IsServer())
            _ = GuardAsync(Multiplayer.GetRemoteSenderId(), peer => BuyAsync(peer, shed));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerTurnIn(string mission)
    {
        if (Multiplayer.IsServer() && Guid.TryParse(mission, out var id))
            _ = GuardAsync(Multiplayer.GetRemoteSenderId(), peer => TurnInAsync(peer, id));
    }

    // ================= Servidor =================

    private async Task GuardAsync(long peer, Func<long, Task> action)
    {
        if (!_busy.Add(peer))
            return;
        try { await action(peer); }
        finally { _busy.Remove(peer); }
    }

    public override void _Process(double delta)
    {
        if (Multiplayer.MultiplayerPeer is null || !Multiplayer.IsServer() || _network.Backend is not { } api)
            return;
        _clock += delta;
        if (_clock >= _nextRefresh)
        {
            _nextRefresh = _clock + RefreshSeconds;
            _ = RefreshAsync(api);
        }
        if (_clock >= _nextGuardTick)
        {
            _nextGuardTick = _clock + GuardTickSeconds;
            // Vigilancia: en cada sede con 2 o más de su cuadrilla adentro suma medio minuto.
            foreach (var b in _bases)
            {
                var inside = _network.AllPlayers.Where(p => !p.Dead && CrewOf(p) == b.CrewName
                    && CrewBaseRules.IsInside(b.Shed, p.GlobalPosition.X, p.GlobalPosition.Z)).ToList();
                if (inside.Count >= 2)
                    _ = ProgressAsync(inside[0].PeerId, MissionKinds.Guard, 1);
            }
        }
    }

    private async Task RefreshAsync(DistritoApiClient api)
    {
        var result = await api.GetCrewBasesAsync(_network.ServerKey);
        if (!result.IsSuccess)
            return;
        var changed = result.Value!.Count != _bases.Count || result.Value.Where((b, i) => _bases[i] != b).Any();
        _bases = result.Value;
        _combat.BaseSheds = _bases.Select(b => b.Shed).ToList();
        if (!changed)
            return;
        ApplySigns();
        foreach (var p in Multiplayer.GetPeers())
            SendBasesTo(p);
    }

    private void SendBasesTo(long peer)
    {
        if (!Multiplayer.IsServer() || !Multiplayer.GetPeers().Contains((int)peer))
            return;
        RpcId(peer, MethodName.ReceiveBases, _bases.Select(b => b.Shed).ToArray(), _bases.Select(b => b.CrewName).ToArray(), _bases.Select(b => b.Rank).ToArray());
    }

    private Vector3? RespawnPointOf(long peer)
    {
        if (_network.GetPlayer(peer) is not { } p || ShedOf(CrewOf(p)) is not { } shed)
            return null;
        var (x, z) = CrewBaseRules.Inside(shed);
        return new Vector3(x, 0.2f, z);
    }

    private bool IsAt(long peer, string? npcCode) =>
        npcCode is not null && NpcInteractionRules.Find(npcCode) is { } npc && _network.GetPlayer(peer) is { } p
        && NpcInteractionRules.InRange(new System.Numerics.Vector3(p.GlobalPosition.X, p.GlobalPosition.Y, p.GlobalPosition.Z), npc);

    private async Task BuyAsync(long peer, int shed)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me) || _network.GetPlayer(peer) is not { } p)
            return;
        if (shed < 0 || shed >= CrewBaseRules.Sheds.Count || !CrewBaseRules.AtBase(shed, p.GlobalPosition.X, p.GlobalPosition.Z))
        {
            SendMessage(peer, "Parate en la puerta del galpón que querés comprar (Los Muelles).");
            return;
        }
        var result = await api.BuyCrewBaseAsync(_network.ServerKey, new CrewBaseBuyRequest(me.CharacterId, shed, Guid.NewGuid().ToString()));
        SendMessage(peer, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
        if (result is { IsSuccess: true, Value.Ok: true })
        {
            GD.Print($"[DISTRITO] CREW_BASE {me.CharacterName}: {result.Value.Message}");
            _nextRefresh = 0;
            _combat.NotifyAll($"Una cuadrilla compró el galpón {shed + 1} de Los Muelles como sede.");
        }
        await SendBoardAsync(peer);
    }

    private async Task TurnInAsync(long peer, Guid missionId)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me) || _network.GetPlayer(peer) is not { } p)
            return;
        var board = (await api.GetCrewBoardAsync(_network.ServerKey, me.CharacterId)).Value;
        // La misión puede ser del tablero o la etapa actual de la operación del día.
        if (board?.Missions.Append(board.Operation?.Current).FirstOrDefault(m => m?.Id == missionId) is not { } mission)
            return;
        var companions = new List<Guid>();
        switch (mission.Kind)
        {
            case MissionKinds.Delivery when !IsAt(peer, mission.NpcCode):
                SendMessage(peer, "La entrega se hace en el lugar: andá hasta ahí.");
                return;
            case MissionKinds.Gather:
                var atBase = board.Shed is { } own && CrewBaseRules.AtBase(own, p.GlobalPosition.X, p.GlobalPosition.Z);
                if (!atBase && !Distrito.Game.World.VisualStock.AtShed(p.GlobalPosition))
                {
                    SendMessage(peer, "El acopio se entrega desde la sede (o desde el galpón de depósitos de Valenti).");
                    return;
                }
                break;
            case MissionKinds.Collection:
                if (!IsAt(peer, mission.NpcCode))
                {
                    SendMessage(peer, "La cobranza se hace en el lugar: vayan juntos hasta ahí.");
                    return;
                }
                foreach (var (otherPeer, other) in _network.AuthenticatedPlayers)
                    if (otherPeer != peer && IsAt(otherPeer, mission.NpcCode))
                        companions.Add(other.CharacterId);
                break;
        }
        var result = await api.TurnInCrewMissionAsync(_network.ServerKey, new CrewMissionTurnInRequest(me.CharacterId, missionId, Guid.NewGuid().ToString(), companions));
        SendMessage(peer, result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}");
        if (result is { IsSuccess: true, Value.Ok: true })
            GD.Print($"[DISTRITO] CREW_MISSION {me.CharacterName}: {result.Value.Message}");
        _economy.RefreshPeer(peer);
        await SendBoardAsync(peer);
    }

    /// <summary>Algo que pasó en el mundo cuenta para las misiones de la cuadrilla del jugador.</summary>
    private async Task ProgressAsync(long peer, string kind, int amount, string? npc = null)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var result = await api.CrewMissionProgressAsync(_network.ServerKey, new CrewMissionProgressRequest(me.CharacterId, kind, amount, Guid.NewGuid().ToString(), npc));
        if (result is { IsSuccess: true, Value: { Ok: true, Message.Length: > 0 } })
        {
            GD.Print($"[DISTRITO] CREW_MISSION_PROGRESS {me.CharacterName}: {result.Value.Message}");
            SendMessage(peer, result.Value.Message);
            _nextRefresh = 0;
        }
    }

    private async Task SendBoardAsync(long peer)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
        {
            SendMessage(peer, "La cuadrilla necesita un servidor con cuentas.");
            return;
        }
        var board = await api.GetCrewBoardAsync(_network.ServerKey, me.CharacterId);
        if (board.IsSuccess && Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveBoard, JsonSerializer.Serialize(board.Value, DistritoApiClient.Json));
    }

    private void SendMessage(long peer, string text)
    {
        if (Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveMessage, text);
    }

    // ================= Servidor → clientes =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveBases(int[] sheds, string[] names, int[] ranks)
    {
        _bases = sheds.Select((s, i) => new CrewBaseView(Guid.Empty, names[i], s, ranks[i])).ToList();
        ApplySigns();
    }

    private void ApplySigns()
    {
        if (GetTree().CurrentScene?.GetNodeOrNull("World") is not { } world)
            return;
        for (var i = 0; i < CrewBaseRules.Sheds.Count; i++)
        {
            var owner = _bases.FirstOrDefault(b => b.Shed == i);
            if (owner is null)
                Distrito.Game.World.DocksBuilder.SetShedSign(world, i, "SE VENDE\nSede de cuadrilla — $250", new Color(0.9f, 0.85f, 0.7f));
            else
                Distrito.Game.World.DocksBuilder.SetShedSign(world, i, $"SEDE DE «{owner.CrewName}»\nprestigio rango {owner.Rank}",
                    PrestigeRules.Honor(owner.Rank) ? new Color(1f, 0.82f, 0.25f) : new Color(0.95f, 0.75f, 0.45f));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveBoard(string json)
    {
        Board = JsonSerializer.Deserialize<CrewBoardView>(json, DistritoApiClient.Json);
        BoardReceived?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);

    /// <summary>Solo para la prueba visual.</summary>
    public void Preview()
    {
        var end = DateTimeOffset.UtcNow.AddHours(2);
        Board = new CrewBoardView(true, "Los del Puerto", 420, 3, 600, 1, 0,
        [
            new(Guid.NewGuid(), MissionKinds.Delivery, "Entrega: 6 cajones de bebidas al Taller Caruso", "Conseguilas y llevalas.", "cajon-bebidas", "npc:taller-moreno", 6, 0, 3800, 19, "abierta", end, "caruso"),
            new(Guid.NewGuid(), MissionKinds.TruckJob, "Encargo de los Caruso: asaltar el camión de reparto de Moretti", "Cuando salga el camión…", null, null, 1, 0, 5200, 24, "abierta", end, "caruso"),
            new(Guid.NewGuid(), MissionKinds.Guard, "Vigilar la sede 4 minutos (con 2 de la cuadrilla)", "Estén adentro.", null, null, 8, 3, 3000, 14, "abierta", end, "valenti"),
            new(Guid.NewGuid(), MissionKinds.Gather, "Acopio: juntar 12 bolsas de café en el depósito", "Tenelas guardadas.", "bolsa-cafe", null, 12, 12, 4400, 20, "cumplida", end, "valenti"),
        ], [1, 3], true, end,
            new CrewOperationView("ruta-camion", "La ruta del camión", CrewOperations.Templates[1].Intro, Distrito.Shared.Economy.EconomyCatalog.Caruso,
                18_400, 81, CrewOperations.Running,
                [
                    new("Seguir al camión", CrewOperations.Templates[1].Stages[0].Detail, true, false),
                    new("La coartada", CrewOperations.Templates[1].Stages[1].Detail, false, true),
                    new("El asalto", CrewOperations.Templates[1].Stages[2].Detail, false, false),
                ],
                new(Guid.NewGuid(), MissionKinds.Gather, "La ruta del camión (2/3): La coartada", "Junten 8 cajones.", "cajon-bebidas", null, 8, 0, 0, 0, "abierta", end, "caruso"),
                end, CrewOperations.MinMembers));
        BoardReceived?.Invoke();
    }
}
