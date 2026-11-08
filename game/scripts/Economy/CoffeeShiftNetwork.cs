using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>
/// Turnos de empleado en la Esquina del café (primera hora de juego). El servidor manda los pedidos, toma el tiempo,
/// lleva la urna (cuántas tazas le quedan y si se está recargando), puntúa cada entrega y al final pide el pago al
/// backend. El cliente solo muestra la barra y manda lo que hizo el jugador (hasta dónde llenó, azúcar y leche).
/// </summary>
public partial class CoffeeShiftNetwork : Node
{
    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;

    private sealed class Shift
    {
        public required long Peer;
        public Guid? Character;
        public required double StartedAt;
        public required int Seed;
        public int NextId;
        public double NextOrderAt;
        public readonly Dictionary<int, (CoffeeOrder Order, double ShownAt)> Waiting = new();
        public readonly List<double> Scores = new();
        public int Lost;
        public int Cups = CoffeeShiftRules.UrnCups;
        public double RefillUntil;
        public double LastServeAt = -10;
        public bool Paying;
    }

    private readonly Dictionary<long, Shift> _shifts = new();
    private double _clock;

    /// <summary>Cliente: lo que llega del servidor para la barra.</summary>
    public event Action<double>? Started; // segundos que dura
    public event Action<CoffeeOrder>? OrderArrived;
    public event Action<int, double, string>? Served; // pedido, puntaje, comentario
    public event Action<int>? OrderLost;
    public event Action<int, bool>? UrnChanged; // tazas, recargando
    public event Action<string>? Finished;

    /// <summary>Cliente: terminó al menos un turno en esta partida (para el tutorial).</summary>
    public bool CompletedOne { get; private set; }

    public void Initialize(GameNetwork network, EconomyNetwork economy)
    {
        _network = network;
        _economy = economy;
        Multiplayer.PeerDisconnected += peer => _shifts.Remove(peer);
    }

    // ---------- Cliente ----------

    public void Start() => Send(MethodName.ServerStart);
    public void Serve(CoffeeServe s) => Send(MethodName.ServerServe, s.OrderId, s.Fill, s.Sugar, s.Milk);
    public void Refill() => Send(MethodName.ServerRefill);
    public void Quit() => Send(MethodName.ServerQuit);

    private void Send(StringName method, params Variant[] args)
    {
        if (!Multiplayer.HasMultiplayerPeer())
            return;
        if (Multiplayer.IsServer())
            Finished?.Invoke("Los turnos necesitan un servidor.");
        else
            RpcId(1, method, args);
    }

    // ---------- Servidor ----------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerStart()
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        if (_shifts.ContainsKey(peer) || _network.GetPlayer(peer) is not { } p)
            return;
        if (!NearCounter(p.GlobalPosition, NpcInteractionRules.InteractionRange + 1))
        {
            RpcId(peer, MethodName.ReceiveFinished, "Para trabajar tenés que estar en la barra de la Esquina del café.");
            return;
        }
        Guid? character = _network.TryGetSession(peer, out var session) ? session.CharacterId : null;
        var shift = new Shift { Peer = peer, Character = character, StartedAt = _clock, Seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() ^ peer), NextOrderAt = _clock + 3 };
        _shifts[peer] = shift;
        GD.Print($"[DISTRITO] TURNO_CAFE empieza {p.DisplayName}");
        RpcId(peer, MethodName.ReceiveStarted, (double)CoffeeShiftRules.ShiftSeconds);
        RpcId(peer, MethodName.ReceiveUrn, shift.Cups, false);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerServe(int orderId, double fill, int sugar, string milk)
    {
        if (!Multiplayer.IsServer() || !_shifts.TryGetValue(Multiplayer.GetRemoteSenderId(), out var s) || s.Paying)
            return;
        if (!s.Waiting.Remove(orderId, out var waiting) || _clock - s.LastServeAt < 2 || _clock < s.RefillUntil)
            return; // pedido que no existe, demasiado rápido (no se sirve en menos de 2 s) o la urna se está recargando
        s.LastServeAt = _clock;
        var watery = s.Cups <= 0;
        s.Cups = Math.Max(0, s.Cups - 1);
        var serve = new CoffeeServe(orderId, double.IsFinite(fill) ? Math.Clamp(fill, 0, 1.2) : 0, Math.Clamp(sugar, 0, 5), milk ?? "", watery);
        var score = CoffeeShiftRules.Score(waiting.Order, serve, _clock - waiting.ShownAt);
        s.Scores.Add(score);
        var note = CoffeeShiftRules.Verdict(score) + (watery ? " (la urna estaba vacía: salió aguado)" : "");
        RpcId(s.Peer, MethodName.ReceiveServed, orderId, score, note);
        RpcId(s.Peer, MethodName.ReceiveUrn, s.Cups, false);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerRefill()
    {
        if (!Multiplayer.IsServer() || !_shifts.TryGetValue(Multiplayer.GetRemoteSenderId(), out var s) || _clock < s.RefillUntil)
            return;
        s.RefillUntil = _clock + CoffeeShiftRules.RefillSeconds;
        s.Cups = CoffeeShiftRules.UrnCups;
        RpcId(s.Peer, MethodName.ReceiveUrn, s.Cups, true);
        GetTree().CreateTimer(CoffeeShiftRules.RefillSeconds).Timeout += () =>
        {
            if (_shifts.ContainsValue(s))
                RpcId(s.Peer, MethodName.ReceiveUrn, s.Cups, false);
        };
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerQuit()
    {
        if (Multiplayer.IsServer() && _shifts.Remove(Multiplayer.GetRemoteSenderId(), out var s))
            RpcId(s.Peer, MethodName.ReceiveFinished, "Dejaste el turno antes de tiempo: el encargado no te paga.");
    }

    private static bool NearCounter(Vector3 at, float range)
    {
        var c = NpcInteractionRules.CoffeeCorner.Position;
        return new Vector2(at.X - c.X, at.Z - c.Z).Length() <= range;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (!Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer() || _shifts.Count == 0)
            return;
        foreach (var s in _shifts.Values.ToList())
        {
            if (s.Paying)
                continue;
            if (_network.GetPlayer(s.Peer) is not { } p || !NearCounter(p.GlobalPosition, CoffeeShiftRules.CounterRange))
            {
                _shifts.Remove(s.Peer);
                if (Multiplayer.GetPeers().Contains((int)s.Peer))
                    RpcId(s.Peer, MethodName.ReceiveFinished, "Te fuiste de la barra: abandonaste el turno y no se cobra.");
                continue;
            }
            foreach (var (id, w) in s.Waiting.ToList())
                if (_clock - w.ShownAt > CoffeeShiftRules.PatienceSeconds)
                {
                    s.Waiting.Remove(id);
                    s.Lost++;
                    RpcId(s.Peer, MethodName.ReceiveLost, id);
                }
            var elapsed = _clock - s.StartedAt;
            if (elapsed >= CoffeeShiftRules.ShiftSeconds)
            {
                s.Paying = true;
                _ = FinishAsync(s, p.DisplayName);
                continue;
            }
            if (_clock >= s.NextOrderAt && s.Waiting.Count < CoffeeShiftRules.MaxWaiting && elapsed < CoffeeShiftRules.ShiftSeconds - 20)
            {
                var order = CoffeeShiftRules.Order(s.Seed, s.NextId++);
                s.Waiting[order.Id] = (order, _clock);
                s.NextOrderAt = _clock + CoffeeShiftRules.OrderEverySeconds;
                RpcId(s.Peer, MethodName.ReceiveOrder, order.Id, order.Customer, order.Sugar, order.Milk);
            }
        }
    }

    private async Task FinishAsync(Shift s, string name)
    {
        var (pay, tip) = CoffeeShiftRules.Earnings(s.Scores);
        var average = s.Scores.Count == 0 ? 0 : s.Scores.Average();
        var summary = $"Terminó el turno: serviste {s.Scores.Count} cafés (promedio {average * 100:0} %), se fueron {s.Lost} sin atender.";
        try
        {
            if (pay + tip == 0)
                summary += $" Para cobrar hay que servir al menos {CoffeeShiftRules.MinServed}.";
            else if (_network.Backend is { } api && s.Character is { } character)
            {
                var result = await api.CoffeeShiftPayAsync(_network.ServerKey,
                    new CoffeeShiftPayRequest(character, $"turno-cafe:{character}:{s.Seed}:{(long)s.StartedAt}", pay, tip, s.Scores.Count));
                summary += " " + (result.IsSuccess ? result.Value!.Message : $"No se pudo cobrar: {result.Error!.Message}");
                if (result.IsSuccess)
                    _economy.RefreshPeer(s.Peer);
            }
            else
                summary += $" (Sin cuenta no se cobra: serían {EconomyCatalog.FormatMoney(pay)} y {EconomyCatalog.FormatMoney(tip)} de propina.)";
            GD.Print($"[DISTRITO] TURNO_CAFE termina {name}: servidos={s.Scores.Count} promedio={average:0.00} sueldo={pay} propina={tip}");
        }
        finally
        {
            _shifts.Remove(s.Peer);
            if (Multiplayer.GetPeers().Contains((int)s.Peer))
                RpcId(s.Peer, MethodName.ReceiveFinished, "✔ " + summary);
        }
    }

    // ---------- Respuestas al cliente ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveStarted(double seconds) => Started?.Invoke(seconds);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveOrder(int id, string customer, int sugar, string milk) => OrderArrived?.Invoke(new CoffeeOrder(id, customer, sugar, milk));

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveServed(int id, double score, string note) => Served?.Invoke(id, score, note);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLost(int id) => OrderLost?.Invoke(id);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveUrn(int cups, bool refilling) => UrnChanged?.Invoke(cups, refilling);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveFinished(string text)
    {
        if (text.StartsWith('✔'))
            CompletedOne = true;
        Finished?.Invoke(text);
    }
}
