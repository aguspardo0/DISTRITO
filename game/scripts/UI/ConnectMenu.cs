using Distrito.Shared.Networking;
using Godot;

namespace Distrito.Game.UI;

/// <summary>Menú inicial: crear una partida (anfitrión) o unirse a una dirección IP.</summary>
public partial class ConnectMenu : Control
{
    private LineEdit _host = null!;
    private SpinBox _port = null!;
    private Label _status = null!;
    private Button _hostButton = null!;
    private Button _joinButton = null!;

    public event Action<int>? HostRequested;
    public event Action<string, int>? JoinRequested;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        panel.AddChild(margin);
        margin.AddChild(box);

        var title = new Label { Text = "DISTRITO — prototipo de red", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(title);

        box.AddChild(new Label { Text = "Dirección del servidor (para unirse):" });
        _host = new LineEdit { Text = NetworkDefaults.DefaultHost, PlaceholderText = "127.0.0.1" };
        box.AddChild(_host);

        box.AddChild(new Label { Text = "Puerto:" });
        _port = new SpinBox { MinValue = 1, MaxValue = 65535, Value = NetworkDefaults.DefaultPort, Rounded = true };
        box.AddChild(_port);

        _hostButton = new Button { Text = "Crear partida (anfitrión)" };
        _hostButton.Pressed += () => HostRequested?.Invoke((int)_port.Value);
        box.AddChild(_hostButton);

        _joinButton = new Button { Text = "Unirse a partida" };
        _joinButton.Pressed += () => JoinRequested?.Invoke(_host.Text.Trim(), (int)_port.Value);
        box.AddChild(_joinButton);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_status);
    }

    public void ShowStatus(string message) => _status.Text = message;

    public void SetBusy(bool busy)
    {
        _hostButton.Disabled = busy;
        _joinButton.Disabled = busy;
    }
}
