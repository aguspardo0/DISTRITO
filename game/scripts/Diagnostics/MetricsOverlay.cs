using System.Diagnostics;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>Contador en pantalla: FPS, RAM del proceso, ping y jugadores. F3 lo muestra u oculta.</summary>
public partial class MetricsOverlay : Label
{
    private const double RefreshSeconds = 0.5;

    private readonly Process _process = Process.GetCurrentProcess();
    private double _untilRefresh;

    public Func<int>? PlayerCount { get; init; }

    public override void _Ready()
    {
        AddThemeColorOverride("font_shadow_color", Colors.Black);
        AddThemeFontSizeOverride("font_size", 14);
        SetAnchorsPreset(LayoutPreset.TopRight);
        GrowHorizontal = GrowDirection.Begin;
        Position = new Vector2(-12, 10);
        HorizontalAlignment = HorizontalAlignment.Right;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 })
            Visible = !Visible;
    }

    public override void _Process(double delta)
    {
        _untilRefresh -= delta;
        if (_untilRefresh > 0 || !Visible)
            return;
        _untilRefresh = RefreshSeconds;

        _process.Refresh();
        var ramMb = _process.WorkingSet64 / (1024.0 * 1024.0);
        var vramMb = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);

        Text = $"FPS {Engine.GetFramesPerSecond():0}\n" +
               $"RAM {ramMb:0} MB · VRAM {vramMb:0} MB\n" +
               $"Ping {PingText()}\n" +
               $"Jugadores {PlayerCount?.Invoke() ?? 0}\n" +
               "(F3 ocultar)";
    }

    private string PingText()
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer enet || Multiplayer.IsServer())
            return "—";
        var server = enet.GetPeer(1);
        return server is null ? "—" : $"{server.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime):0} ms";
    }
}
