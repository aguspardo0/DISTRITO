using System.Diagnostics;
using System.Globalization;
using Distrito.Game.Network;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Servidor: imprime métricas cada N segundos en una línea fácil de procesar:
/// [DISTRITO] METRICS jugadores=.. fps=.. fisica_ms=.. cpu_pct=.. ram_mb=.. enviado_kbps=.. recibido_kbps=.. pares_visibles=.. desconexiones=.. errores=..
/// cpu_pct: uso de CPU del proceso en el intervalo (100 = un núcleo entero). desconexiones/errores: acumulados desde el arranque.
/// </summary>
public partial class ServerMetrics : Node
{
    private readonly Process _process = Process.GetCurrentProcess();
    private GameNetwork _network = null!;
    private double _interval;
    private double _elapsed;
    private double _maxPhysicsMs;
    private TimeSpan _lastCpu;
    private int _disconnections;
    private int _errors;

    public void Initialize(GameNetwork network, int intervalSeconds)
    {
        _network = network;
        _interval = intervalSeconds;
        _lastCpu = _process.TotalProcessorTime;
        Multiplayer.PeerDisconnected += _ => _disconnections++;
        _network.ErrorReported += () => _errors++;
    }

    public override void _Process(double delta)
    {
        // El peor tiempo de física dentro del intervalo es lo que importa para saber si el servidor llega.
        _maxPhysicsMs = Math.Max(_maxPhysicsMs, Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);

        _elapsed += delta;
        if (_elapsed < _interval)
            return;

        var seconds = _elapsed;
        _elapsed = 0;

        double sentKbps = 0, receivedKbps = 0;
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet && enet.Host is { } host)
        {
            sentKbps = host.PopStatistic(ENetConnection.HostStatistic.SentData) * 8 / 1000.0 / seconds;
            receivedKbps = host.PopStatistic(ENetConnection.HostStatistic.ReceivedData) * 8 / 1000.0 / seconds;
        }

        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var cpuPct = (cpu - _lastCpu).TotalSeconds / seconds * 100.0;
        _lastCpu = cpu;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"jugadores={_network.PlayerCount} fps={Engine.GetFramesPerSecond():0} fisica_ms_max={_maxPhysicsMs:0.00} cpu_pct={cpuPct:0} " +
            $"ram_mb={_process.WorkingSet64 / 1048576.0:0} enviado_kbps={sentKbps:0.0} recibido_kbps={receivedKbps:0.0} " +
            $"pares_visibles={_network.Interest?.VisiblePairs ?? 0} desconexiones={_disconnections} errores={_errors}");
        GD.Print($"[DISTRITO] METRICS {line}");
        _maxPhysicsMs = 0;
    }
}
