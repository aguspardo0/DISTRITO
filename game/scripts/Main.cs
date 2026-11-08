using Distrito.Game.Backend;
using Distrito.Game.Crews;
using Distrito.Game.Diagnostics;
using Distrito.Game.Economy;
using Distrito.Server.Core.Npcs;
using Distrito.Game.Network;
using Distrito.Game.UI;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
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
        "WASD: moverse   Shift: correr   Clic: piña/disparar   1-4: arma   Mouse: cámara   Espacio: saltar   E: hablar   G: subir/bajar del vehículo   I: inventario   C: cuadrilla   J: tablero   L: encargos   P: precios   K: habilidades   M: mapa   Esc: opciones   F11: pantalla completa   F3: métricas";

    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private CrewNetwork _crews = null!;
    private CrewPanel? _crewPanel;
    private Distrito.Game.Inventory.InventoryNetwork _inventory = null!;
    private Distrito.Game.Inventory.InventoryPanel? _inventoryPanel;
    private Distrito.Game.Inventory.SupplierPanel? _supplierPanel;
    private Distrito.Game.Admin.AdminNetwork _admin = null!;
    private Distrito.Game.Transport.VehicleNetwork _vehicles = null!;
    private Distrito.Game.Transport.VehicleShopPanel? _vehicleShop;
    private Distrito.Game.World.VisualStock _stock = null!;
    private Vector3 _previewNear;
    private Distrito.Game.Pve.PveNetwork _pve = null!;
    private BaseNetwork _bases = null!;
    private CrewBoardPanel? _boardPanel;
    private EncargoNetwork _encargos = null!;
    private EncargoPanel? _encargoPanel;
    private MarketNetwork _market = null!;
    private MarketPanel? _marketPanel;
    private EncargoTracker? _encargoTracker;
    private EncargoMarkers? _encargoMarkers;
    private RewardBanner? _rewardBanner;
    private TutorialGuide? _tutorial;
    /// <summary>Encargos de ejemplo para las capturas de prueba.</summary>
    private EconomySnapshot? _encargoPreview;
    private Distrito.Game.Casino.CasinoNetwork _casino = null!;
    private CoffeeShiftNetwork _coffeeShift = null!;
    private CoffeeShiftPanel? _coffeePanel;
    private Distrito.Game.Player.SkillNetwork _skills = null!;
    private Distrito.Game.Player.SkillTreePanel? _skillPanel;
    private Distrito.Game.Casino.CasinoPanel? _casinoPanel;
    private Distrito.Game.Admin.AdminPanel? _adminPanel;
    private Distrito.Game.Map.MapNetwork _map = null!;
    private Distrito.Game.Combat.CombatNetwork _combat = null!;
    private Distrito.Game.Map.MapPanel? _mapPanel;
    private PauseMenu? _pause;
    private NpcInteractionController? _interaction;
    private Distrito.Game.Life.LifeNetwork? _life;
    private AccountSession? _account;
    private ConnectMenu? _menu;
    private Label? _hud;

    public LaunchOptions Options { get; private set; } = LaunchOptions.Default;

    public override void _Ready()
    {
        Distrito.Game.World.DocksBuilder.Build(GetNode<Node3D>("World"));
        Distrito.Game.World.DocksBuilder.BuildCasino(GetNode<Node3D>("World"));
        Distrito.Game.World.CityBuilder.Build(GetNode<Node3D>("World"));
        AddChild(new Distrito.Game.World.DayNightCycle { Name = "DayNight" });
        AddChild(new Distrito.Game.World.MusicPlayer { Name = "Music" });
        AddChild(new Distrito.Game.Audio.SoundPlayer { Name = "Sound" });
        Distrito.Game.World.InteriorBuilder.Build(GetNode<Node3D>("World"));
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

        // Sin pantalla no hay sincronización vertical: sin límite, cada proceso usaría un núcleo entero.
        if (DisplayServer.GetName() == "headless")
            Engine.MaxFps = Options.Mode == LaunchMode.Server ? 60 : 30;

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

        // Interés de red: solo actúa en el proceso que es servidor (dedicado o anfitrión).
        var interest = new InterestManager { Name = "Interest" };
        AddChild(interest);
        interest.Initialize(Options.InterestRadius);
        _network.Interest = interest;

        _economy = new EconomyNetwork { Name = "Economy" };
        AddChild(_economy);
        _economy.Initialize(_network);

        _crews = new CrewNetwork { Name = "Crews" };
        AddChild(_crews);
        _crews.Initialize(_network);

        _combat = new Distrito.Game.Combat.CombatNetwork { Name = "Combat" };
        AddChild(_combat);
        _combat.Initialize(_network, _economy);

        _inventory = new Distrito.Game.Inventory.InventoryNetwork { Name = "Inventory" };
        AddChild(_inventory);
        _inventory.Initialize(_network, _economy, _combat);

        var doors = new Distrito.Game.World.DoorNetwork { Name = "Doors" };
        AddChild(doors);
        doors.Initialize(_network);
        var props = new Distrito.Game.World.PropsNetwork();
        AddChild(props);
        props.Initialize(_network);

        _stock = new Distrito.Game.World.VisualStock { Name = "VisualStock" };
        AddChild(_stock);
        _stock.Initialize(_network);

        _pve = new Distrito.Game.Pve.PveNetwork { Name = "Pve" };
        AddChild(_pve);
        _pve.Initialize(_network, _economy, _combat);

        _skills = new Distrito.Game.Player.SkillNetwork { Name = "Skills" };
        AddChild(_skills);
        _skills.Initialize(_network, _economy);

        _casino = new Distrito.Game.Casino.CasinoNetwork { Name = "Casino" };
        AddChild(_casino);
        _casino.Initialize(_network, _economy);
        _coffeeShift = new CoffeeShiftNetwork { Name = "CoffeeShift" };
        AddChild(_coffeeShift);
        _coffeeShift.Initialize(_network, _economy);

        _bases = new BaseNetwork { Name = "Bases" };
        AddChild(_bases);
        _bases.Initialize(_network, _economy, _combat, _pve);

        _encargos = new EncargoNetwork { Name = "Encargos" };
        AddChild(_encargos);
        _encargos.Initialize(_network, _economy, _pve);

        _market = new MarketNetwork { Name = "Market" };
        AddChild(_market);
        _market.Initialize(_network, _economy);

        _vehicles = new Distrito.Game.Transport.VehicleNetwork { Name = "Vehicles" };
        AddChild(_vehicles);
        _vehicles.Initialize(_network, _economy, _combat);
        _inventory.UseVehicles(_vehicles);

        _admin = new Distrito.Game.Admin.AdminNetwork { Name = "Admin" };
        AddChild(_admin);
        _admin.Initialize(_network, _economy, _combat);
        _admin.UsePve(_pve);

        _map = new Distrito.Game.Map.MapNetwork { Name = "Map" };
        AddChild(_map);
        _map.Initialize(_network);
        var life = new Distrito.Game.Life.LifeNetwork { Name = "Life" };
        AddChild(life);
        life.Initialize(_network, _combat);
        _admin.UseLife(life);
        _life = life;
        SpawnNpcs();

        if (Options.Mode == LaunchMode.Server)
        {
            StartDedicatedServer();
            return;
        }

        if (Options.UiSnapshotDir is null)
            GameSettings.Load(); // la prueba visual usa la ventana que le pasa la línea de comandos
        CreateHud();

        if (Options.UiSnapshotDir is { } snapshotDir)
        {
            ShowMenu();
            StartUiSnapshot(snapshotDir);
            return;
        }

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
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.ValentiOffice, new Color(0.45f, 0.12f, 0.15f)));
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.CoffeeCorner, new Color(0.35f, 0.22f, 0.12f)));
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.JazzClub, new Color(0.2f, 0.15f, 0.35f)));
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.Casino, new Color(0.12f, 0.35f, 0.18f)));
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.Kiosk, new Color(0.5f, 0.44f, 0.3f)));
        // Los mismos que atienden en la vereda, detrás de su mostrador de adentro; y los dueños de las tiendas.
        foreach (var npc in new[] { NpcInteractionRules.Deposit, NpcInteractionRules.Workshop, NpcInteractionRules.ValentiOffice })
            foreach (var (counter, i) in (npc.Counters ?? []).Select((c, i) => (c, i)))
                npcs.AddChild(NpcActor.Create(npc, new Color(0.45f, 0.38f, 0.3f), new Vector3(counter.X, counter.Y, counter.Z), $"_adentro{i}"));
        foreach (var shop in NpcInteractionRules.Shops)
            npcs.AddChild(NpcActor.Create(shop, new Color(0.52f, 0.42f, 0.34f)));
        npcs.AddChild(NpcActor.Create(NpcInteractionRules.Informant, new Color(0.16f, 0.16f, 0.18f)));
        // Jefes de familia: traje oscuro con el color de su barrio.
        foreach (var (boss, barrio) in NpcInteractionRules.Bosses.Zip(Distrito.Server.Core.World.CityLayout.Barrios))
            npcs.AddChild(NpcActor.Create(boss, new Color(barrio.Accent.R, barrio.Accent.G, barrio.Accent.B).Darkened(0.45f)));
        // Su gente, con el color de la familia más claro.
        foreach (var contact in NpcInteractionRules.FamilyContacts)
            if (Distrito.Server.Core.World.CityLayout.BarrioAt(contact.Position.X, contact.Position.Z) is { } home)
                npcs.AddChild(NpcActor.Create(contact, new Color(home.Accent.R, home.Accent.G, home.Accent.B).Darkened(0.15f)));
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

        if (_network.StartDedicatedServer(Options.Port, backend, serverKey, Options.MaxPlayers) != Error.Ok)
        {
            GetTree().Quit(1);
            return;
        }
        GD.Print($"[DISTRITO] Radio de interés: {Options.InterestRadius} m");

        if (Options.MetricsIntervalSeconds > 0)
        {
            var metrics = new ServerMetrics { Name = "ServerMetrics" };
            AddChild(metrics);
            metrics.Initialize(_network, Options.MetricsIntervalSeconds);
        }
    }

    /// <summary>Conexión automática (bots / pruebas): opcionalmente inicia sesión y elige personaje.</summary>
    private async Task AutoConnectAsync()
    {
        if (Options.IsSelfTest)
        {
            var test = new NetworkSelfTest { Name = "NetworkSelfTest" };
            test.Configure(_network, Options.TestSeconds, Options.ExpectPlayers,
                expectMovement: Options.Bot && Options.ExpectSpawn is null && Options.BotFightWeapon is null, Options.ExpectSpawn);
            AddChild(test);

            if (Options.ExpectVisibleAtEnd is { } visible)
                test.ExpectVisibleAtEnd(visible);
            if (Options.ExpectMapPlayers is { } onMap)
                test.ExpectMapPlayers(_map, onMap);

            if ((Options.BotRoute ?? (Options.BotGoto is { } destination ? new List<(float X, float Z)> { destination } : null)) is { Count: > 0 } route)
            {
                var gotoBot = new GotoBot { Name = "GotoBot" };
                AddChild(gotoBot);
                gotoBot.Initialize(_network, route);
            }

            if (Options.BotCrewCreate is not null || Options.BotCrewJoin)
            {
                var crewBot = new CrewBot { Name = "CrewBot" };
                AddChild(crewBot);
                crewBot.Initialize(_network, _crews, Options.BotCrewCreate);
                test.ExpectCrew(crewBot);
            }

            if (Options.BotBusiness)
            {
                var businessBot = new BusinessBot { Name = "BusinessBot" };
                AddChild(businessBot);
                businessBot.Initialize(_network, _economy);
                test.ExpectBusiness(businessBot);
            }

            if (Options.BotFight)
            {
                var fightBot = new FightBot { Name = "FightBot" };
                AddChild(fightBot);
                fightBot.Initialize(_network, _combat, Options.BotFightWeapon);
                test.ExpectFight(fightBot);
            }

            if (Options.BotContract)
            {
                var contractBot = new ContractBot { Name = "ContractBot" };
                AddChild(contractBot);
                contractBot.Initialize(_network, _economy, Options.BotContractCode);
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
        layer.AddChild(UiAnchor.Wrap(new MetricsOverlay { Name = "Metrics", PlayerCount = () => _network.PlayerCount },
            UiAnchor.End, UiAnchor.Start, name: "MetricsArea"));

        var interaction = new NpcInteractionController { Name = "NpcInteraction" };
        _interaction = interaction;
        if (_life is not null)
            interaction.UseLife(_life);
        AddChild(interaction);
        interaction.Initialize(_network, _economy, layer);

        _mapPanel = new Distrito.Game.Map.MapPanel { Name = "MapPanel" };
        layer.AddChild(UiAnchor.Wrap(_mapPanel, UiAnchor.Center, UiAnchor.Center, name: "MapArea"));
        _mapPanel.Initialize(_network, _map);
        _mapPanel.Economy = () => _economy.LastSnapshot;

        _crewPanel = new CrewPanel { Name = "CrewPanel" };
        layer.AddChild(UiAnchor.Wrap(_crewPanel, UiAnchor.End, UiAnchor.Center, right: 20, name: "CrewArea"));
        _crewPanel.Initialize(_crews);

        _inventoryPanel = new Distrito.Game.Inventory.InventoryPanel { Name = "InventoryPanel" };
        layer.AddChild(UiAnchor.Wrap(_inventoryPanel, UiAnchor.Center, UiAnchor.Center, name: "InventoryArea"));
        _inventoryPanel.Initialize(_inventory, _combat, _network, _economy);

        _supplierPanel = new Distrito.Game.Inventory.SupplierPanel { Name = "SupplierPanel" };
        layer.AddChild(UiAnchor.Wrap(_supplierPanel, UiAnchor.Center, UiAnchor.Center, name: "SupplierArea"));
        _supplierPanel.Initialize(_inventory);
        interaction.SupplierRequested += npc => _supplierPanel.Open(npc);

        _skillPanel = new Distrito.Game.Player.SkillTreePanel { Name = "SkillTree" };
        layer.AddChild(UiAnchor.Wrap(_skillPanel, UiAnchor.Center, UiAnchor.Center, name: "SkillTreeArea"));
        _skillPanel.Initialize(_skills, _economy);

        _casinoPanel = new Distrito.Game.Casino.CasinoPanel { Name = "CasinoPanel" };
        layer.AddChild(UiAnchor.Wrap(_casinoPanel, UiAnchor.Center, UiAnchor.Center, name: "CasinoArea"));
        _casinoPanel.Initialize(_casino, () => _economy.LastSnapshot?.BalanceCents ?? 0);
        interaction.CasinoRequested += () => _casinoPanel.Open();

        _coffeePanel = new CoffeeShiftPanel { Name = "CoffeeShiftPanel" };
        layer.AddChild(UiAnchor.Wrap(_coffeePanel, UiAnchor.Center, UiAnchor.End, bottom: 40, name: "CoffeeShiftArea"));
        _coffeePanel.Initialize(_coffeeShift);
        interaction.CoffeeShiftRequested += _coffeeShift.Start;

        _marketPanel = new MarketPanel { Name = "MarketPanel" };
        layer.AddChild(UiAnchor.Wrap(_marketPanel, UiAnchor.Center, UiAnchor.Center, name: "MarketArea"));
        _marketPanel.Initialize(_market);
        interaction.MarketRequested += tab => _marketPanel.Open(tab);

        _boardPanel = new CrewBoardPanel { Name = "CrewBoard" };
        layer.AddChild(UiAnchor.Wrap(_boardPanel, UiAnchor.Center, UiAnchor.Center, name: "CrewBoardArea"));
        _boardPanel.Initialize(_bases, () => _network.LocalPlayer?.GlobalPosition);

        // Encargos: libreta (L), seguimiento arriba a la derecha, marcadores en el mundo y aviso al cumplir.
        _encargoPanel = new EncargoPanel { Name = "EncargoPanel" };
        layer.AddChild(UiAnchor.Wrap(_encargoPanel, UiAnchor.Center, UiAnchor.Center, name: "EncargoArea"));
        _encargoPanel.Initialize(_encargos, _economy);
        _encargoPanel.PreviewData = () => _encargoPreview;
        _encargoTracker = new EncargoTracker { Name = "EncargoTracker" };
        layer.AddChild(UiAnchor.Wrap(_encargoTracker, UiAnchor.End, UiAnchor.Start, right: 16, top: 150, name: "EncargoTrackerArea"));
        _encargoTracker.Initialize(_economy, () => _network.LocalPlayer?.VisualPosition, () => _encargoPreview);
        _encargoTracker.HiddenWhen = () => _encargoPanel is { IsOpen: true } || _mapPanel is { Visible: true };
        _encargoMarkers = new EncargoMarkers { Name = "EncargoMarkers" };
        GetNode<Node3D>("World").AddChild(_encargoMarkers);
        _encargoMarkers.Initialize(_economy, () => _encargoPreview);
        _rewardBanner = new RewardBanner { Name = "RewardBanner" };
        layer.AddChild(UiAnchor.Wrap(_rewardBanner, UiAnchor.Center, UiAnchor.Start, top: 150, name: "RewardBannerArea"));
        _economy.DialogReceived += dialog => _rewardBanner.ShowIfCompleted(dialog.Text);
        // Tutorial de inicio: arriba a la izquierda, debajo de los avisos; marca a dónde ir como los encargos.
        _tutorial = new TutorialGuide { Name = "Tutorial" };
        layer.AddChild(UiAnchor.Wrap(_tutorial, UiAnchor.Start, UiAnchor.Start, left: 12, top: 96, name: "TutorialArea"));
        _tutorial.Initialize(() => _economy.LastSnapshot, () => _network.LocalCharacterId,
            () => _mapPanel is { Visible: true }, () => _encargoPanel is { IsOpen: true });
        _tutorial.ShiftDone = () => _coffeeShift.CompletedOne;
        _encargoMarkers.Extra = () => _tutorial.Targets;
        _mapPanel.ExtraTargets = () => _tutorial.Targets;
        _tutorial.TargetsChanged += () => _encargoMarkers.Refresh();
        _encargos.MessageReceived += text =>
        {
            if (_hud is not null)
                _hud.Text = $"{text}\n{ControlsHelp}";
        };

        _vehicleShop = new Distrito.Game.Transport.VehicleShopPanel { Name = "VehicleShop" };
        layer.AddChild(UiAnchor.Wrap(_vehicleShop, UiAnchor.Center, UiAnchor.Center, name: "VehicleShopArea"));
        _vehicleShop.Initialize(_vehicles, () => _economy.LastSnapshot);
        interaction.VehicleShopRequested += () => _vehicleShop.Open();
        _inventoryPanel.ForceVehicle = id => _vehicles.Force(id);
        _vehicles.MessageReceived += text => _combat.ShowMessage(text);

        _adminPanel = new Distrito.Game.Admin.AdminPanel { Name = "AdminPanel" };
        layer.AddChild(UiAnchor.Wrap(_adminPanel, UiAnchor.Start, UiAnchor.Center, left: 20, name: "AdminArea"));
        _adminPanel.Initialize(_admin);

        // Las opciones van arriba de todo lo demás.
        _pause = new PauseMenu { Name = "PauseMenu" };
        layer.AddChild(_pause);
        _pause.LeaveRequested += LeaveMatch;
        _pause.TutorialSkipRequested += () => _tutorial?.Skip();
        _pause.TutorialRestartRequested += () => _tutorial?.Restart();
        // Arma en la mano: mira en el centro y balas abajo a la derecha.
        var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        crosshair.AddThemeFontSizeOverride("font_size", 26);
        crosshair.AddThemeColorOverride("font_shadow_color", Colors.Black);
        layer.AddChild(UiAnchor.Wrap(crosshair, UiAnchor.Center, UiAnchor.Center, name: "Crosshair"));
        var ammo = new Label();
        ammo.AddThemeFontSizeOverride("font_size", 18);
        ammo.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.45f));
        ammo.AddThemeColorOverride("font_shadow_color", Colors.Black);
        layer.AddChild(UiAnchor.Wrap(ammo, UiAnchor.End, UiAnchor.End, right: 20, bottom: 20, name: "AmmoArea"));
        // Vida y estado propio (abajo a la derecha, arriba de las balas).
        var health = new Label();
        health.AddThemeFontSizeOverride("font_size", 18);
        health.AddThemeColorOverride("font_shadow_color", Colors.Black);
        layer.AddChild(UiAnchor.Wrap(health, UiAnchor.End, UiAnchor.End, right: 20, bottom: 46, name: "HealthArea"));
        // Aviso al entrar a Los Muelles (zona libre).
        var freeZone = new Label { Text = "LOS MUELLES — zona libre: acá te pueden atacar y robar", Visible = false };
        freeZone.AddThemeFontSizeOverride("font_size", 17);
        freeZone.AddThemeColorOverride("font_color", new Color(0.95f, 0.5f, 0.38f));
        freeZone.AddThemeColorOverride("font_shadow_color", Colors.Black);
        layer.AddChild(UiAnchor.Wrap(freeZone, UiAnchor.Center, UiAnchor.Start, top: 110, name: "FreeZoneArea"));
        GetTree().ProcessFrame += () =>
        {
            if (!IsInstanceValid(health))
                return;
            var me = _network.LocalPlayer;
            freeZone.Visible = me is not null && Distrito.Server.Core.World.MapPresence.InDocks(me.VisualPosition.X, me.VisualPosition.Z);
            health.Text = me is null ? "" : me.Dead ? "Muerto — volvés en unos segundos"
                : me.Downed ? "Herido — esperá que te levante un compañero"
                : $"♥ {me.Health}";
            health.AddThemeColorOverride("font_color", me is { Downed: true } || me?.Health < 40 ? new Color(0.9f, 0.35f, 0.3f) : new Color(0.92f, 0.88f, 0.8f));
        };

        _combat.AmmoChanged += () =>
        {
            ammo.Text = _combat.AmmoText;
            crosshair.Visible = _combat.AmmoText.Length > 0;
        };

        _combat.MessageReceived += text =>
        {
            if (_hud is not null)
                _hud.Text = $"{text}\n{ControlsHelp}";
        };
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

        // Actualización automática (solo el juego exportado de Windows, y no en pruebas automáticas).
        if (Options.UiSnapshotDir is null && GameUpdater.Supported && defaults.Version.Length > 0)
        {
            var updater = new GameUpdater { Name = "Updater" };
            AddChild(updater);
            updater.Status += text => menu.ShowStatus(text);
            updater.Busy += busy => menu.SetBusy(busy);
            menu.ShowStatus($"Versión {defaults.Version}. Buscando actualizaciones…");
            updater.Start(defaults.Version);
        }

        menu.LoginRequested += (url, user, pass) => _ = AccountActionAsync(menu, url, s => s.LoginAsync(user, pass));
        menu.RegisterRequested += (url, user, pass) =>
            _ = AccountActionAsync(menu, url, s => s.RegisterAndLoginAsync(user, pass, acceptExisting: false));
        menu.CreateCharacterRequested += (name, look) => _ = CreateCharacterAsync(menu, name, look);

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

    private async Task CreateCharacterAsync(ConnectMenu menu, string name, string look)
    {
        if (_account?.Token is null)
            return;

        menu.SetBusy(true);
        var (character, error) = await _account.CreateCharacterAsync(name, look);
        menu.SetBusy(false);

        if (character is null)
        {
            menu.ShowStatus(error ?? "No se pudo crear el personaje.");
            return;
        }
        menu.ShowLoggedIn(_account.Characters, character);
        menu.ShowStatus($"Personaje {character.Name} creado.");
    }

    private void StartUiSnapshot(string dir)
    {
        var shot = new UiSnapshot { Name = "UiSnapshot" };
        AddChild(shot);
        shot.Initialize(dir,
        [
            ("menu", () => { }),
            ("menu-personaje", () => _menu?.ShowLoggedIn(
            [
                new CharacterSummary(Guid.NewGuid(), "Vito", "barrio", "ejecutivo"),
                new CharacterSummary(Guid.NewGuid(), "Sal", "barrio", "operativo"),
            ])),
            ("juego", () =>
            {
                _menu?.Hide();
                _network.StartHost(7990);
            }),
            ("dialogo", () => _interaction?.Dialog.ShowDialog(new NpcDialog(NpcInteractionRules.Deposit.Code, NpcInteractionRules.Deposit.Name,
                "Texto de prueba del diálogo, con varias líneas para ver que el panel crezca y siga centrado abajo. "
                + "Zona Calle del Almacén: nadie la controla todavía. Ánimo de la familia: tranquila. Próximo evento: Feria del puerto.",
                [.. Enumerable.Range(1, 12).Select(i => new NpcOption($"o{i}",
                    i % 3 == 0 ? $"Opción {i}: una opción muy larga como los contratos, con requisitos, plazos, pago y reputación, que antes ensanchaba la ventana"
                               : $"Opción {i}: aceptar un trabajo (paga $20.00, 4 minutos)"))]))),
            ("modelos", () =>
            {
                _interaction?.Dialog.Close();
                if (_network.LocalPlayer is not { } me)
                    return;
                var i = 0;
                foreach (var look in CharacterLooks.All)
                {
                    var model = Distrito.Game.Player.CharacterModel.Build(look);
                    AddChild(model);
                    model.GlobalPosition = me.GlobalPosition + new Vector3(-2.25f + 1.5f * i++, 0, -3.2f);
                    model.RotationDegrees = new Vector3(0, 180, 0); // de frente a la cámara
                }
            }),
            ("cuadrilla", () => { _crewPanel?.Toggle(); }),
            ("mapa", () => { _crewPanel?.Toggle(); _mapPanel?.Toggle(); }),
            ("inventario", () => { _mapPanel?.Toggle(); _inventoryPanel?.Toggle(); _inventory.Preview(); }),
            ("deposito", () => _inventoryPanel?.ShowWarehouseTab()),
            ("pedidos", () =>
            {
                _inventoryPanel?.Toggle();
                _supplierPanel?.Open(Distrito.Shared.Economy.EconomyCatalog.DepositNpc);
                _inventory.PreviewSupplier();
            }),
            ("vehiculos", () =>
            {
                _supplierPanel?.Close();
                if (_network.LocalPlayer is { } me)
                {
                    _previewNear = me.GlobalPosition;
                    _vehicles.Preview(me.GlobalPosition);
                }
                _vehicleShop?.Open();
            }),
            ("carga", () =>
            {
                _vehicleShop?.Close();
                _inventoryPanel?.Toggle();
                _inventory.Preview();
                _inventoryPanel?.ShowVehicleTab();
            }),
            ("existencias", () =>
            {
                _inventoryPanel?.Toggle();
                _stock.Preview();
                if (_network.LocalPlayer is { } me)
                    me.GlobalPosition = new Vector3(-24.5f, 0.2f, -1f); // adentro del galpón, mirando las primeras bahías
            }),
            ("camiones", () =>
            {
                if (_network.LocalPlayer is { } me)
                    me.GlobalPosition = _previewNear + new Vector3(0, 0, 1.5f); // los vehículos de ejemplo, con carga
            }),
            ("asaltos", () =>
            {
                if (_network.LocalPlayer is { } me)
                {
                    me.GlobalPosition = new Vector3(40f, 0.2f, 6f); // Los Muelles, con lugar
                    _pve.Preview(new Vector3(40f, 0, 6f));
                }
            }),
            // Barrios de las familias: parado en el bulevar, mirando hacia la plaza y el cuartel.
            ("barrio-moretti", () => ShowBarrio(Distrito.Server.Core.World.CityLayout.Moretti)),
            ("barrio-valenti", () => ShowBarrio(Distrito.Server.Core.World.CityLayout.Valenti)),
            ("barrio-caruso", () => ShowBarrio(Distrito.Server.Core.World.CityLayout.Caruso)),
            ("tutorial", () =>
            {
                _tutorial?.Preview(1); // «Tu primera compra», con la luz sobre el Almacén
                _encargoMarkers?.Refresh();
            }),
            ("encargos", () =>
            {
                // Dos encargos en curso (uno con premio por rapidez) y las ofertas del turno, con seguimiento y marcadores.
                _tutorial?.Skip();
                _encargoPreview = EncargoPreview();
                _encargoTracker?.Refresh();
                _encargoMarkers?.Refresh();
                _encargoPanel?.Toggle();
            }),
            ("encargo-cumplido", () =>
            {
                _encargoPanel?.Toggle();
                _rewardBanner?.ShowIfCompleted("¡Encargo cumplido! Los Caruso deben plata\nCobraste $21.00, +79 de experiencia y +3 de reputación con los Moretti.\n¡Subiste a nivel 4!");
            }),
            ("tablero", () => { if (_rewardBanner is not null) _rewardBanner.Visible = false; _encargoPreview = null; _encargoTracker?.Refresh(); _encargoMarkers?.Refresh(); _boardPanel?.Toggle(); _bases.Preview(); }),
            ("casino", () => { _boardPanel?.Toggle(); _casinoPanel?.Open(); _casinoPanel?.Preview(); }),
            ("pizarras", () =>
            {
                _casinoPanel?.Close();
                _market.Preview();
                if (_network.LocalPlayer is { } me)
                {
                    me.GlobalPosition = new Vector3(-9f, 0.2f, 0.5f); // en la calle: el Almacén y su pizarra, el kiosco de diarios más allá
                    me.GetNodeOrNull<Distrito.Game.Player.LocalPlayerController>("LocalController")?.LookToward(Mathf.Atan2(4f, 6f));
                }
            }),
            ("clientes", () =>
            {
                // La Esquina del café con gente haciendo cola (los clientes del último ciclo) y su pizarra.
                if (_network.LocalPlayer is { } me)
                {
                    me.GlobalPosition = new Vector3(-1f, 0.2f, 0.5f);
                    me.GetNodeOrNull<Distrito.Game.Player.LocalPlayerController>("LocalController")?.LookToward(Mathf.Pi + 0.25f);
                }
            }),
            ("libreta-precios", () => { _marketPanel?.Open(MarketPanel.Notes); _market.Preview(); }),
            // Interiores del Mercado: el galpón mayorista, una tienda y el diner, mirando hacia adentro.
            ("galpon", () => { _marketPanel?.Close(); EnterInterior("galpon-moretti"); }),
            ("ramos-generales", () => EnterInterior("ramos-generales")),
            ("diner", () => EnterInterior("diner")),
            ("taller", () => EnterInterior("taller-caruso")),
            ("puertas", () =>
            {
                // Afuera: la calle con las puertas, los toldos y los cajones con física.
                if (_network.LocalPlayer is { } me)
                {
                    me.GlobalPosition = new Vector3(2f, 0.2f, -6f);
                    me.GetNodeOrNull<Distrito.Game.Player.LocalPlayerController>("LocalController")?.LookToward(0);
                }
            }),
            ("diario", () => _marketPanel?.Open(MarketPanel.Paper)),
            ("informante", () => _marketPanel?.Open(MarketPanel.Report)),
            ("libro-cuentas", () => _marketPanel?.Open(MarketPanel.Ledger)),
            ("habilidades", () =>
            {
                _marketPanel?.Close();
                _casinoPanel?.Close();
                if (_skillPanel is null)
                    return;
                _skillPanel.PreviewData = () => (8, new Dictionary<string, int>
                {
                    ["piel-dura"] = 2, ["pulso-firme"] = 1, ["regateo"] = 1, ["buen-vendedor"] = 1, ["puntual"] = 1,
                });
                _skillPanel.Toggle();
            }),
            ("admin", () => { _skillPanel?.Toggle(); if (_skillPanel is not null) _skillPanel.PreviewData = null; _adminPanel?.Toggle(); _admin.Preview(); }),
            ("opciones", () => { _adminPanel?.Toggle(); _pause?.Open(); }),
        ]);
    }

    private static EconomySnapshot EncargoPreview()
    {
        var id = Guid.NewGuid();
        var drafts = EncargoGenerator.Generate(id, EncargoRules.SlotOf(DateTimeOffset.UtcNow), 6);
        var now = DateTimeOffset.UtcNow;
        var offers = drafts.Skip(1).Select(d => EncargoGenerator.ToOffer(d, now.AddMinutes(70))).ToList();
        offers.Add(EncargoGenerator.ToOffer(EncargoGenerator.Daily(id, EncargoRules.DayOf(now), 6), now.AddHours(9)));
        var first = drafts[0];
        var active = new List<EncargoView>
        {
            EncargoGenerator.ToOffer(first, now.AddMinutes(38)) with { Status = EncargoStatus.Active },
            new(Guid.NewGuid(), EncargoKinds.Contrarreloj, "npc:moretti-sofia", EconomyCatalog.Moretti, "Contra el reloj",
                "«Los Valenti nos quieren ganar de mano.»", EconomyCatalog.DrinksCrateItem, 2, null, 0, 2, 0, 2400, 92, 3,
                EncargoStatus.Active, false, now.AddMinutes(4), now.AddMinutes(4), now.AddMinutes(2)),
        };
        return new EconomySnapshot(2500, [], [], 0, [new ReputationView(EconomyCatalog.Moretti, "Familia Moretti", 34)], Encargos: [.. active, .. offers]);
    }

    private void EnterInterior(string code)
    {
        if (_network.LocalPlayer is not { } me || Distrito.Server.Core.World.InteriorLayout.Find(code) is not { } plan)
            return;
        var at = plan.EntryPoint;
        me.GlobalPosition = new Vector3(at.X, 0.2f, at.Y + 0.6f);
        me.GetNodeOrNull<Distrito.Game.Player.LocalPlayerController>("LocalController")?.LookToward(plan.EntryYaw);
    }

    private void ShowBarrio(Distrito.Server.Core.World.BarrioPlan b)
    {
        if (_network.LocalPlayer is not { } me)
            return;
        var spot = b.ToWorld(28, 3);
        me.GlobalPosition = new Vector3(spot.X, 0.2f, spot.Y);
        // Mirar hacia adentro del barrio: con yaw 0 se mira a −Z.
        me.GetNodeOrNull<Distrito.Game.Player.LocalPlayerController>("LocalController")?.LookToward(Mathf.Atan2(-b.Inward.X, -b.Inward.Y));
    }

    /// <summary>Salir de la partida sin cerrar el juego: se guarda (el servidor guarda al desconectar) y vuelve al menú.</summary>
    private void LeaveMatch()
    {
        if (_crewPanel is { IsOpen: true }) _crewPanel.Toggle();
        if (_mapPanel is { Visible: true }) _mapPanel.Toggle();
        if (_inventoryPanel is { IsOpen: true }) _inventoryPanel.Toggle();
        if (_adminPanel is { IsOpen: true }) _adminPanel.Toggle();
        if (_boardPanel is { IsOpen: true }) _boardPanel.Toggle();
        if (_skillPanel is { IsOpen: true }) _skillPanel.Toggle();
        if (_encargoPanel is { IsOpen: true }) _encargoPanel.Toggle();
        _marketPanel?.Close();
        _supplierPanel?.Close();
        _vehicleShop?.Close();
        _casinoPanel?.Close();
        _interaction?.Dialog.Close();
        _network.Leave();
        GetNode<Camera3D>("OverviewCamera").Current = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        if (_menu is null)
            ShowMenu();
        else
        {
            _menu.SetBusy(false);
            if (_account is { Token: not null } account)
                _menu.ShowLoggedIn(account.Characters);
            _menu.Show();
        }
        _menu?.ShowStatus("Saliste de la partida. Tu progreso quedó guardado.");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key &&
            (key.PhysicalKeycode == Key.F11 || (key.PhysicalKeycode == Key.Enter && key.AltPressed)))
        {
            GameSettings.ToggleFullscreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape } && _network.LocalPlayer is not null && _pause is not null)
        {
            // Esc cierra primero lo que esté abierto; si no hay nada, abre o cierra las opciones.
            if (_interaction?.Dialog is { IsOpen: true } || _supplierPanel is { IsOpen: true } || _vehicleShop is { IsOpen: true } || _casinoPanel is { IsOpen: true } || _coffeePanel is { IsOpen: true })
                return; // el diálogo y los pedidos se cierran solos con Esc
            if (_crewPanel is { IsOpen: true }) _crewPanel.Toggle();
            else if (_inventoryPanel is { IsOpen: true }) _inventoryPanel.Toggle();
            else if (_adminPanel is { IsOpen: true }) _adminPanel.Toggle();
            else if (_boardPanel is { IsOpen: true }) _boardPanel.Toggle();
            else if (_skillPanel is { IsOpen: true }) _skillPanel.Toggle();
            else if (_encargoPanel is { IsOpen: true }) _encargoPanel.Toggle();
            else if (_marketPanel is { IsOpen: true }) _marketPanel.Close();
            else if (_mapPanel is { Visible: true }) _mapPanel.Toggle();
            else if (_pause.IsOpen) _pause.Close();
            else _pause.Open();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_pause is { IsOpen: true })
            return; // con las opciones abiertas no se abren otros paneles

        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.C } && _network.LocalPlayer is not null)
            _crewPanel?.Toggle();
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.K } && _network.LocalPlayer is not null)
            _skillPanel?.Toggle();
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.J } && _network.LocalPlayer is not null)
            _boardPanel?.Toggle();
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.L } && _network.LocalPlayer is not null)
            _encargoPanel?.Toggle();
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.P } && _network.LocalPlayer is not null)
            _marketPanel?.Toggle();
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F10 } && _network.LocalPlayer is not null)
            _adminPanel?.Toggle();
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.I } && _network.LocalPlayer is not null)
            _inventoryPanel?.Toggle();
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.M } && _network.LocalPlayer is not null)
            _mapPanel?.Toggle();
    }

    public override void _ExitTree() => _account?.Dispose();
}
