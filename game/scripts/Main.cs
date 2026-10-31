using Distrito.Game.Backend;
using Distrito.Game.Crews;
using Distrito.Game.Diagnostics;
using Distrito.Game.Economy;
using Distrito.Server.Core.Npcs;
using Distrito.Game.Network;
using Distrito.Game.UI;
using Distrito.Shared.Api;
using Distrito.Shared.Launch;
using Godot;

namespace Distrito.Game;

/// <summary>
/// Punto de entrada. Según las opciones de arranque:
/// servidor dedicado (con o sin cuentas), cliente automático (bots de prueba), o menú.
/// </summary>
public partial class Main : Node3D
{
    private const string ControlsHelp =
        "WASD: moverse   Mouse: cámara   Espacio: saltar   E: hablar   C: cuadrilla   Esc: soltar el mouse (clic para volver)   F3: métricas";

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private CrewNetwork _crews = null!;
    private CrewPanel? _crewPanel;
    private AccountSession? _account;
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

        var bot = Options.Bot
            ? new BotSettings(true, StopAfterSeconds: Options.IsSelfTest ? Math.Max(1, Options.TestSeconds - 4) : double.MaxValue)
            : BotSettings.Off;

        _network = new GameNetwork { Name = "GameNetwork" };
        AddChild(_network);
        _network.Initialize(GetNode<Node3D>("Players"), GetNode<MultiplayerSpawner>("PlayerSpawner"), bot);

        _economy = new EconomyNetwork { Name = "Economy" };
        AddChild(_economy);
        _economy.Initialize(_network);

        _crews = new CrewNetwork { Name = "Crews" };
        AddChild(_crews);
        _crews.Initialize(_network);
        SpawnNpcs();

        if (Options.Mode == LaunchMode.Server)
        {
            StartDedicatedServer();
            return;
        }

        CreateHud();

        if (Options.AutoConnect)
            _ = AutoConnectAsync();
        else
            ShowMenu();
    }

    /// <summary>NPCs fijos del barrio: se crean igual en servidor y clientes.</summary>
    private void SpawnNpcs()
    {
        var npcs = new Node3D { Name = "Npcs" };
        AddChild(npcs);
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.Deposit, new Color(0.55f, 0.42f, 0.3f)));
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.Workshop, new Color(0.3f, 0.38f, 0.5f)));
    }

    private void StartDedicatedServer()
    {
        DistritoApiClient? backend = null;
        var serverKey = "";
        if (Options.BackendUrl is not null)
        {
            serverKey = System.Environment.GetEnvironmentVariable("DISTRITO_SERVER_KEY") ?? "";
            if (serverKey.Length == 0)
            {
                GD.PushError("[DISTRITO] Con --backend hace falta la variable de entorno DISTRITO_SERVER_KEY.");
                GetTree().Quit(2);
                return;
            }
            _account = new AccountSession(Options.BackendUrl); // se reutiliza su HttpClient
            backend = _account.Api;
        }

        if (_network.StartDedicatedServer(Options.Port, backend, serverKey) != Error.Ok)
            GetTree().Quit(1);
    }

    /// <summary>Conexión automática (bots / pruebas): opcionalmente inicia sesión y elige personaje.</summary>
    private async Task AutoConnectAsync()
    {
        if (Options.IsSelfTest)
        {
            var test = new NetworkSelfTest { Name = "NetworkSelfTest" };
            test.Configure(_network, Options.TestSeconds, Options.ExpectPlayers,
                expectMovement: Options.Bot && Options.ExpectSpawn is null, Options.ExpectSpawn);
            AddChild(test);

            if (Options.BotCrewCreate is not null || Options.BotCrewJoin)
            {
                var crewBot = new CrewBot { Name = "CrewBot" };
                AddChild(crewBot);
                crewBot.Initialize(_network, _crews, Options.BotCrewCreate);
                test.ExpectCrew(crewBot);
            }

            if (Options.BotContract)
            {
                var contractBot = new ContractBot { Name = "ContractBot" };
                AddChild(contractBot);
                contractBot.Initialize(_network, _economy);
                test.ExpectContract(contractBot);
            }
        }

        var credentials = ClientCredentials.Guest;
        if (Options.Account is not null)
        {
            _account = new AccountSession(Options.BackendUrl!);
            CharacterSummary? character = null;
            var characterError = await _account.RegisterAndLoginAsync(Options.Account, Options.Password!, acceptExisting: true);
            if (characterError is null)
                (character, characterError) = await _account.EnsureCharacterAsync(Options.Character!);
            if (character is null)
            {
                GD.PrintErr($"[DISTRITO] NET_TEST_FAIL (cuenta: {characterError})");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"[DISTRITO] Sesión iniciada como {Options.Account}, personaje {character.Name}");
            credentials = new ClientCredentials(_account.Token!, character.Id);
        }

        if (_network.ConnectTo(Options.Host, Options.Port, credentials) != Error.Ok)
            GetTree().Quit(1);
    }

    private void CreateHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        _hud = new Label { Position = new Vector2(12, 10), Text = "" };
        _hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
        layer.AddChild(_hud);
        layer.AddChild(new MetricsOverlay { Name = "Metrics", PlayerCount = () => _network.PlayerCount });

        var interaction = new NpcInteractionController { Name = "NpcInteraction" };
        AddChild(interaction);
        interaction.Initialize(_network, _economy, layer);

        _crewPanel = new CrewPanel { Name = "CrewPanel" };
        layer.AddChild(_crewPanel);
        _crewPanel.Initialize(_crews);
        _crews.MessageReceived += text =>
        {
            if (_hud is not null)
                _hud.Text = $"{text}\n{ControlsHelp}";
        };

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
        var defaults = ServerDefaults.Load();
        var menu = new ConnectMenu
        {
            Name = "ConnectMenu",
            InitialBackendUrl = Options.BackendUrl ?? defaults.BackendUrl,
            InitialGameHost = defaults.GameHost,
        };
        _menu = menu;
        layer.AddChild(menu);

        menu.LoginRequested += (url, user, pass) => _ = AccountActionAsync(menu, url, s => s.LoginAsync(user, pass));
        menu.RegisterRequested += (url, user, pass) =>
            _ = AccountActionAsync(menu, url, s => s.RegisterAndLoginAsync(user, pass, acceptExisting: false));
        menu.CreateCharacterRequested += name => _ = CreateCharacterAsync(menu, name);

        menu.HostRequested += port =>
        {
            menu.SetBusy(true);
            if (_network.StartHost(port) != Error.Ok)
                menu.SetBusy(false);
        };
        menu.JoinRequested += (host, port, character) =>
        {
            var credentials = character is null || _account?.Token is null
                ? ClientCredentials.Guest
                : new ClientCredentials(_account.Token, character.Id);
            menu.SetBusy(true);
            if (_network.ConnectTo(host, port, credentials) != Error.Ok)
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

    private async Task AccountActionAsync(ConnectMenu menu, string url, Func<AccountSession, Task<string?>> action)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            menu.ShowStatus("Escribí la dirección del servidor de cuentas, por ejemplo http://servidor:5080");
            return;
        }

        menu.SetBusy(true);
        menu.ShowStatus("Conectando con el servidor de cuentas…");
        _account?.Dispose();
        _account = new AccountSession(url);
        var error = await action(_account);
        menu.SetBusy(false);

        if (error is not null)
        {
            menu.ShowStatus(error);
            return;
        }

        menu.ShowLoggedIn(_account.Characters);
        menu.ShowStatus(_account.Characters.Count == 0
            ? "Sesión iniciada. Creá tu primer personaje."
            : "Sesión iniciada. Elegí tu personaje y unite a una partida.");
    }

    private async Task CreateCharacterAsync(ConnectMenu menu, string name)
    {
        if (_account?.Token is null)
            return;

        menu.SetBusy(true);
        var (character, error) = await _account.CreateCharacterAsync(name);
        menu.SetBusy(false);

        if (character is null)
        {
            menu.ShowStatus(error ?? "No se pudo crear el personaje.");
            return;
        }
        menu.ShowLoggedIn(_account.Characters, character);
        menu.ShowStatus($"Personaje {character.Name} creado.");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.C } && _network.LocalPlayer is not null)
            _crewPanel?.Toggle();
    }

    public override void _ExitTree() => _account?.Dispose();
}
