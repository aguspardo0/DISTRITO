using Distrito.Game.Network;
using Distrito.Shared.Api;
using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// Reportar un problema (prueba cerrada, 2026-11-08): con F8 se abre una ventanita, el jugador escribe qué pasó y el
/// servidor lo guarda con su nombre, dónde estaba, la zona y la versión del juego. Los administradores los ven en F10.
/// Uno cada 20 segundos por jugador, hasta 1.000 letras.
/// </summary>
public partial class BugReportPanel : PanelContainer
{
    private GameNetwork _network = null!;
    private TextEdit _text = null!;
    private Label _status = null!;
    private readonly Dictionary<long, double> _lastByPeer = new();
    private double _clock;

    public bool IsOpen => Visible;

    public void Initialize(GameNetwork network) => _network = network;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(520, 0);
        AddThemeStyleboxOverride("panel", Distrito.Game.Economy.EncargoLook.Panel(0.95f, Distrito.Game.Economy.EncargoLook.Gold));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        box.AddChild(Distrito.Game.Economy.EncargoLook.Text("Reportar un problema", Distrito.Game.Economy.EncargoLook.Gold, 500, 17));
        box.AddChild(Distrito.Game.Economy.EncargoLook.Text(
            "Contá qué pasó y qué estabas haciendo. Se guarda junto con dónde estás parado y la versión del juego.", null, 500, 13));
        _text = new TextEdit { CustomMinimumSize = new Vector2(500, 140), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Ej.: me quedé trabado contra la pared del Banco cuando…" };
        box.AddChild(_text);
        var row = new HBoxContainer();
        var send = new Button { Text = "Enviar" };
        send.Pressed += Send;
        row.AddChild(send);
        var close = new Button { Text = "Cerrar (Esc)" };
        close.Pressed += Close;
        row.AddChild(close);
        box.AddChild(row);
        _status = Distrito.Game.Economy.EncargoLook.Text("", Distrito.Game.Economy.EncargoLook.Muted, 500, 12);
        box.AddChild(_status);
    }

    public override void _Process(double delta) => _clock += delta;

    public void Toggle()
    {
        if (Visible)
        {
            Close();
            return;
        }
        Visible = true;
        _status.Text = "";
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _text.GrabFocus();
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        _text.ReleaseFocus();
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Send()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            _status.Text = "Escribí qué pasó.";
            return;
        }
        if (text.Length > BugReport.MaxLength)
            text = text[..BugReport.MaxLength];
        if (!Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer())
        {
            _status.Text = "Para reportar hay que estar conectado a un servidor.";
            return;
        }
        RpcId(1, MethodName.ServerReport, text, ProjectSettings.GetSetting("application/config/version", "").AsString());
        _status.Text = "Enviando…";
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ServerReport(string text, string version)
    {
        if (!Multiplayer.IsServer())
            return;
        var peer = (long)Multiplayer.GetRemoteSenderId();
        if (_lastByPeer.TryGetValue(peer, out var last) && _clock - last < 20)
        {
            RpcId(peer, MethodName.ReceiveAck, "Esperá unos segundos antes de mandar otro.");
            return;
        }
        _lastByPeer[peer] = _clock;
        _ = StoreAsync(peer, text ?? "", version ?? "");
    }

    private async Task StoreAsync(long peer, string text, string version)
    {
        var player = _network.GetPlayer(peer);
        var at = player?.GlobalPosition ?? Vector3.Zero;
        Guid? character = _network.TryGetSession(peer, out var s) ? s.CharacterId : null;
        var report = new BugReport(character, player?.DisplayName ?? $"peer {peer}", text.Length > BugReport.MaxLength ? text[..BugReport.MaxLength] : text,
            MathF.Round(at.X, 1), MathF.Round(at.Z, 1), Distrito.Server.Core.Combat.CombatRules.ZoneAt(new System.Numerics.Vector3(at.X, at.Y, at.Z)), version);
        GD.Print($"[DISTRITO] REPORTE de {report.Player} en ({report.X}, {report.Z}) {report.Zone}: {report.Text.Replace('\n', ' ')}");
        var ok = _network.Backend is { } api && (await api.SendBugReportAsync(_network.ServerKey, report)).IsSuccess;
        if (Multiplayer.GetPeers().Contains((int)peer))
            RpcId(peer, MethodName.ReceiveAck, ok ? "¡Gracias! El reporte quedó guardado." : "Quedó anotado en el servidor (sin base de datos no se guarda).");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAck(string text)
    {
        _status.Text = text;
        if (text.StartsWith("¡Gracias"))
            _text.Text = "";
    }
}
