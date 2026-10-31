using Distrito.Game.Diagnostics;
using Distrito.Game.Network;
using Distrito.Game.UI;
using Distrito.Shared.Launch;
using Godot;

namespace Distrito.Game;

/// <summary>
/// Punto de entrada. Lee las opciones de arranque y decide el modo:
/// servidor dedicado, cliente con conexión automática, o menú (crear / unirse).
/// </summary>
public partial class Main : Node3D
{
    private const string ControlsHelp =
        "WASD: moverse   Mouse: cámara   Espacio: saltar   Esc: soltar el mouse (clic para volver)";

    private GameNetwork _network = null!;
    private ConnectMenu? _menu;
    private Label? _hud;

    public LaunchOptions Options { get; private set; } = LaunchOptions.Default;

    public override void _Ready()
    {
        try
        {
            Options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        }
        catch (ArgumentException ex)
        {
            GD.PushError($"[DISTRITO] {ex.Message}");
            GetTree().Quit(2);
            return;
        }

        GD.Print($"[DISTRITO] Arranque OK — modo={Options.Mode} host={Options.Host} puerto={Options.Port} godot={Engine.GetVersionInfo()["string"]}");

        if (Options.SmokeTest)
        {
            GD.Print("[DISTRITO] SMOKE_TEST_OK");
            GetTree().Quit(0);
            return;
        }

        _network = new GameNetwork { Name = "GameNetwork" };
        AddChild(_network);
        _network.Initialize(
            GetNode<Node3D>("Players"),
            GetNode<MultiplayerSpawner>("PlayerSpawner"),
            botLocalPlayer: Options.Bot);

        if (Options.Mode == LaunchMode.Server)
        {
            if (_network.StartDedicatedServer(Options.Port) != Error.Ok)
                GetTree().Quit(1);
            return;
        }

        CreateHud();

        if (Options.AutoConnect)
        {
            if (Options.IsSelfTest)
            {
                var test = new NetworkSelfTest { Name = "NetworkSelfTest" };
                test.Configure(_network, Options.TestSeconds, Options.ExpectPlayers);
                AddChild(test);
            }

            if (_network.ConnectTo(Options.Host, Options.Port) != Error.Ok)
                GetTree().Quit(1);
            return;
        }

        ShowMenu();
    }

    private void CreateHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        _hud = new Label { Position = new Vector2(12, 10), Text = "" };
        _hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
        layer.AddChild(_hud);

        _network.StatusChanged += message =>
        {
            if (_hud is not null)
                _hud.Text = $"{message}\n{ControlsHelp}";
            _menu?.ShowStatus(message);
        };
        _network.Connected += () => _menu?.Hide();
    }

    private void ShowMenu()
    {
        var layer = new CanvasLayer { Name = "MenuLayer", Layer = 10 };
        AddChild(layer);
        var menu = new ConnectMenu { Name = "ConnectMenu" };
        _menu = menu;
        layer.AddChild(menu);

        menu.HostRequested += port =>
        {
            menu.SetBusy(true);
            if (_network.StartHost(port) != Error.Ok)
                menu.SetBusy(false);
        };
        menu.JoinRequested += (host, port) =>
        {
            menu.SetBusy(true);
            if (_network.ConnectTo(host, port) != Error.Ok)
                menu.SetBusy(false);
        };
        _network.StatusChanged += message =>
        {
            if (message.StartsWith("No se pudo", StringComparison.Ordinal) || message.StartsWith("Se perdió", StringComparison.Ordinal))
            {
                menu.SetBusy(false);
                menu.Show();
            }
        };
    }
}
