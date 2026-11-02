using System.Text.Json;
using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>
/// Lo que el jugador ve del mercado (aprobado por el equipo, 2026-11-02).
/// El servidor consulta las pizarras cada tanto y se las manda a todos; las pizarras se dibujan al lado de cada local.
/// Cuando un jugador pasa frente a una pizarra, el servidor la anota en su libreta. La libreta, el diario, el informe de
/// Nino y el libro de cuentas se piden al abrir la ventana (tecla P).
/// </summary>
public partial class MarketNetwork : Node3D
{
    private const double PollSeconds = 20;
    private const double LookTickSeconds = 2;
    private const double LookAgainSeconds = 60;

    private GameNetwork _network = null!;
    private EconomyNetwork? _economy;
    private string _last = "";
    private double _clock, _nextPoll, _nextLook;
    private bool _polling;
    private readonly Dictionary<(long Peer, string Npc), double> _looked = new();
    private readonly Dictionary<string, Label3D> _signs = new();
    private static MarketNetwork? _instance;

    public List<PriceBoardView> Boards { get; private set; } = [];
    public MarketInfoView? Info { get; private set; }

    public event Action? InfoReceived;

    /// <summary>Resultado de una mejora o un cambio de precio (se muestra en la ventana).</summary>
    public event Action<string>? MessageReceived;

    public void Initialize(GameNetwork network, EconomyNetwork? economy = null)
    {
        _instance = this;
        _network = network;
        _economy = economy;
        _network.PlayerAdmitted += (peer, _) =>
        {
            if (_last.Length > 0)
                RpcId(peer, MethodName.ReceiveBoards, _last);
        };
    }

    /// <summary>Pide actualizar las pizarras pronto (después de una compra o un pedido).</summary>
    public static void RequestRefresh()
    {
        if (_instance is not null)
            _instance._nextPoll = Math.Min(_instance._nextPoll, _instance._clock + 0.5);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public override void _Ready() => BuildKiosk();

    // ================= Pizarras =================

    private static readonly Dictionary<string, Vector3> SignOffsets = new()
    {
        [EconomyCatalog.DepositNpc] = new(2.3f, 0, 1.2f),
        [EconomyCatalog.WorkshopNpc] = new(-2.3f, 0, 1.2f),
        [EconomyCatalog.CoffeeCornerNpc] = new(2.3f, 0, 0),
        [EconomyCatalog.JazzClubNpc] = new(-2.3f, 0, 0.6f),
        [MarketInfoRules.KioskNpc] = new(2.4f, 0, 0.4f),
    };

    private void ShowBoards()
    {
        foreach (var board in Boards)
        {
            if (NpcInteractionRules.Find(board.NpcCode) is not { } npc)
                continue;
            if (!_signs.TryGetValue(board.NpcCode, out var sign))
            {
                var offset = SignOffsets.GetValueOrDefault(board.NpcCode, new Vector3(1.4f, 0, 0));
                var at = new Vector3(npc.Position.X, 0, npc.Position.Z) + offset;
                var stand = new Node3D { Name = $"Pizarra_{board.NpcCode.Replace(':', '_')}", Position = at };
                AddChild(stand);
                // Caballete de madera con la pizarra negra arriba (el texto se ve siempre de frente).
                var wood = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.25f, 0.15f) };
                foreach (var x in new[] { -0.7f, 0.7f })
                    stand.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.07f, 1.95f, 0.07f) }, MaterialOverride = wood, Position = new Vector3(x, 0.97f, 0.05f) });
                // La pizarra mira hacia donde llega la gente (el centro del Mercado); el texto se ve siempre de frente.
                stand.Rotation = new Vector3(0, Mathf.Atan2(-at.X, -at.Z), 0);
                stand.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(1.7f, 1.55f, 0.05f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.12f, 0.11f) }, Position = new Vector3(0, 1.12f, 0),
                });
                sign = new Label3D
                {
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 28, OutlineSize = 6, PixelSize = 0.0055f,
                    Modulate = new Color(0.96f, 0.95f, 0.89f), OutlineModulate = new Color(0.08f, 0.1f, 0.09f),
                    Width = 300, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    VerticalAlignment = VerticalAlignment.Top, Position = new Vector3(0, 1.85f, 0.06f),
                };
                stand.AddChild(sign);
                _signs[board.NpcCode] = sign;
            }
            sign.Text = MarketInfoRules.BoardText(board);
            ShowCrowd(board, npc);
        }
    }

    private readonly Dictionary<string, Node3D> _crowds = new();

    private static readonly Color[] CoatColors =
    [
        new(0.32f, 0.27f, 0.22f), new(0.2f, 0.24f, 0.32f), new(0.45f, 0.18f, 0.16f), new(0.24f, 0.3f, 0.22f),
        new(0.5f, 0.45f, 0.38f), new(0.15f, 0.15f, 0.17f), new(0.55f, 0.32f, 0.4f), new(0.3f, 0.42f, 0.45f),
    ];

    /// <summary>La gente que se ve en el local: tantos como clientes atendidos en el último ciclo (hasta 16 para no pesar).</summary>
    private void ShowCrowd(PriceBoardView board, NpcDefinition npc)
    {
        if (_crowds.TryGetValue(board.NpcCode, out var old))
        {
            if (old.GetChildCount() == Math.Min(board.Crowd, 16))
                return;
            old.QueueFree();
            _crowds.Remove(board.NpcCode);
        }
        var count = Math.Min(board.Crowd, 16);
        if (count == 0)
            return;
        var crowd = new Node3D { Name = $"Clientes_{board.NpcCode.Replace(':', '_')}", Position = new Vector3(npc.Position.X, 0, npc.Position.Z) };
        AddChild(crowd);
        _crowds[board.NpcCode] = crowd;
        var rng = new Random(board.NpcCode.GetHashCode());
        // La cola mira hacia el centro del Mercado (de donde llega la gente), en semicírculo y en dos filas.
        var toCenter = Mathf.Atan2(-npc.Position.Z, -npc.Position.X);
        for (var i = 0; i < count; i++)
        {
            var ring = i < 7 ? 1.9f : 3.0f;
            var angle = toCenter + Mathf.Pi * 0.7f * (((i % 7) + 0.5f) / 7f - 0.5f) + (i >= 7 ? 0.2f : 0);
            var person = new Node3D { Position = new Vector3(Mathf.Cos(angle) * ring, 0, Mathf.Sin(angle) * ring), Rotation = new Vector3(0, -angle - Mathf.Pi / 2, 0) };
            var coat = new StandardMaterial3D { AlbedoColor = CoatColors[rng.Next(CoatColors.Length)] };
            var height = 1.5f + (float)rng.NextDouble() * 0.3f;
            person.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.22f, Height = height }, MaterialOverride = coat, Position = new Vector3(0, height / 2, 0) });
            person.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.6f, 0.47f) }, Position = new Vector3(0, height + 0.08f, 0),
            });
            if (rng.Next(3) > 0)
                person.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.2f, Height = 0.08f },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.1f, 0.09f) }, Position = new Vector3(0, height + 0.2f, 0),
                });
            crowd.AddChild(person);
        }
    }

    /// <summary>El kiosco de diarios: un puesto con techito, pilas de diarios y el cartel.</summary>
    private void BuildKiosk()
    {
        var p = NpcInteractionRules.Kiosk.Position;
        var kiosk = new Node3D { Name = "Kiosco", Position = new Vector3(p.X, 0, p.Z + 1.1f) };
        AddChild(kiosk);
        var green = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.3f, 0.22f) };
        var paper = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.87f, 0.78f) };
        kiosk.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.2f, 1f, 0.9f) }, MaterialOverride = green, Position = new Vector3(0, 0.5f, 0) });
        kiosk.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.08f, 1.3f) }, MaterialOverride = green, Position = new Vector3(0, 2.3f, -0.1f) });
        foreach (var x in new[] { -1.05f, 1.05f })
            kiosk.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 2.3f, 0.08f) }, MaterialOverride = green, Position = new Vector3(x, 1.15f, 0.35f) });
        for (var i = 0; i < 4; i++)
            kiosk.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.04f + 0.03f * i, 0.3f) }, MaterialOverride = paper, Position = new Vector3(-0.75f + 0.5f * i, 1.02f + 0.015f * i, -0.1f) });
        kiosk.AddChild(new Label3D
        {
            Text = "DIARIOS · REVISTAS", FontSize = 40, OutlineSize = 8, PixelSize = 0.008f, Modulate = new Color(0.95f, 0.8f, 0.45f),
            Position = new Vector3(0, 2.5f, -0.1f), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        });
    }

    // ================= Servidor =================

    public override void _Process(double delta)
    {
        _clock += delta;
        if (Multiplayer.MultiplayerPeer is null || !Multiplayer.IsServer() || _network?.Backend is not { } backend)
            return;
        if (!_polling && _clock >= _nextPoll)
        {
            _nextPoll = _clock + PollSeconds;
            _ = PollAsync(backend);
        }
        if (_clock >= _nextLook)
        {
            _nextLook = _clock + LookTickSeconds;
            LookAround(backend);
        }
    }

    private async Task PollAsync(DistritoApiClient backend)
    {
        _polling = true;
        try
        {
            var result = await backend.GetMarketBoardsAsync(_network.ServerKey);
            if (!result.IsSuccess)
                return;
            var json = JsonSerializer.Serialize(result.Value, DistritoApiClient.Json);
            if (json == _last)
                return;
            _last = json;
            foreach (var p in Multiplayer.GetPeers())
                RpcId(p, MethodName.ReceiveBoards, json);
            Boards = result.Value!;
            ShowBoards();
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>Quien está frente a una pizarra la lee: se anota en su libreta (como mucho una vez por minuto por local).</summary>
    private void LookAround(DistritoApiClient backend)
    {
        foreach (var (peer, session) in _network.AuthenticatedPlayers)
        {
            if (_network.GetPlayer(peer) is not { Dead: false } body)
                continue;
            foreach (var board in Boards)
            {
                if (NpcInteractionRules.Find(board.NpcCode) is not { } npc)
                    continue;
                var d = new Vector2(body.GlobalPosition.X - npc.Position.X, body.GlobalPosition.Z - npc.Position.Z).Length();
                if (d > MarketInfoRules.BoardReadRange)
                    continue;
                var key = (peer, board.NpcCode);
                if (_looked.TryGetValue(key, out var at) && _clock - at < LookAgainSeconds)
                    continue;
                _looked[key] = _clock;
                _ = backend.LookAtBoardAsync(_network.ServerKey, new MarketLookRequest(session.CharacterId, board.NpcCode));
            }
        }
    }

    // ================= Cliente → servidor =================

    public void RequestInfo()
    {
        if (Multiplayer.MultiplayerPeer is null)
            return;
        if (Multiplayer.IsServer())
            _ = SendInfoAsync(1);
        else
            RpcId(1, MethodName.ServerInfo);
    }

    /// <summary>Comprar el próximo nivel de una rama de mejoras de un negocio propio.</summary>
    public void Upgrade(string kind, string branch) => SendAction(MethodName.ServerUpgrade, kind, branch);

    /// <summary>Cambiar el precio al público de un negocio propio.</summary>
    public void SetPrice(string kind, long cents) => SendAction(MethodName.ServerPrice, kind, cents.ToString());

    private void SendAction(StringName method, string kind, string value)
    {
        if (Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer())
            MessageReceived?.Invoke("Esto necesita un servidor con cuentas.");
        else
            RpcId(1, method, kind, value);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerUpgrade(string kind, string branch)
    {
        if (Multiplayer.IsServer())
            _ = ActAsync(Multiplayer.GetRemoteSenderId(), (api, me) =>
                api.UpgradeBusinessAsync(_network.ServerKey, new BusinessUpgradeRequest(me, kind ?? "", branch ?? "", Guid.NewGuid().ToString())));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerPrice(string kind, string cents)
    {
        if (Multiplayer.IsServer() && long.TryParse(cents, out var value))
            _ = ActAsync(Multiplayer.GetRemoteSenderId(), (api, me) =>
                api.SetBusinessPriceAsync(_network.ServerKey, new BusinessPriceRequest(me, kind ?? "", value, Guid.NewGuid().ToString())));
    }

    private readonly HashSet<long> _busy = new();

    private async Task ActAsync(long peer, Func<DistritoApiClient, Guid, Task<ApiResult<EconomyActionResult>>> action)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me) || !_busy.Add(peer))
            return;
        try
        {
            var result = await action(api, me.CharacterId);
            var text = result.IsSuccess ? result.Value!.Message : $"Hubo un problema: {result.Error!.Message}";
            if (result is { IsSuccess: true, Value.Ok: true })
                GD.Print($"[DISTRITO] NEGOCIO {me.CharacterName}: {text}");
            if (Multiplayer.GetPeers().Contains((int)peer))
                RpcId(peer, MethodName.ReceiveMessage, text);
            _economy?.RefreshPeer(peer);
            RequestRefresh();
            await SendInfoAsync(peer);
        }
        finally
        {
            _busy.Remove(peer);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveMessage(string text) => MessageReceived?.Invoke(text);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerInfo()
    {
        if (Multiplayer.IsServer())
            _ = SendInfoAsync(Multiplayer.GetRemoteSenderId());
    }

    private async Task SendInfoAsync(long peer)
    {
        if (_network.Backend is not { } api || !_network.TryGetSession(peer, out var me))
            return;
        var result = await api.GetMarketInfoAsync(_network.ServerKey, me.CharacterId);
        if (!result.IsSuccess)
            return;
        var json = JsonSerializer.Serialize(result.Value, DistritoApiClient.Json);
        if (peer == 1 || !Multiplayer.GetPeers().Contains((int)peer))
            ReceiveInfo(json);
        else
            RpcId(peer, MethodName.ReceiveInfo, json);
    }

    // ================= Servidor → cliente =================

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveBoards(string json)
    {
        if (JsonSerializer.Deserialize<List<PriceBoardView>>(json, DistritoApiClient.Json) is { } boards)
        {
            Boards = boards;
            ShowBoards();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveInfo(string json)
    {
        Info = JsonSerializer.Deserialize<MarketInfoView>(json, DistritoApiClient.Json);
        InfoReceived?.Invoke();
    }

    // ================= Prueba visual =================

    /// <summary>Datos de ejemplo para las capturas (sin servidor): pizarras, libreta, diario, informe y libro.</summary>
    public void Preview()
    {
        var now = DateTimeOffset.UtcNow;
        Boards =
        [
            new(EconomyCatalog.DepositNpc, "Almacén Moretti",
            [
                new(EconomyCatalog.CoffeeBagItem, "Bolsa de café", 330, true, 1), new(EconomyCatalog.DrinksCrateItem, "Cajón de bebidas", 400, false),
                new(EconomyCatalog.CoffeeBagItem, "Bolsa de café", 180, true, 0, true), new(EconomyCatalog.SparePartsItem, "Caja de repuestos", 480, true, 0, true),
            ], now),
            new(EconomyCatalog.WorkshopNpc, "Taller Caruso", [new("vehiculo:camioneta", "Camioneta de reparto", 12_000, true), new("vehiculo:camion", "Camión de carga", 35_000, true)], now),
            new(EconomyCatalog.CoffeeCornerNpc, "Esquina del café",
            [
                new("a", "«Café Don Sal» (Café)", 560, true), new("b", "«La Esquina de Vito» (Puesto)", 500, true), new("c", "«El Rincón» (Puesto)", 450, false),
            ], now, "De moda: «Café Don Sal» (Popular)", 11),
            new(MarketInfoRules.KioskNpc, MarketInfoRules.KioskName, [new(MarketInfoRules.Newspaper, MarketInfoRules.PaperName, 10, true)], now,
                "«TENSIÓN: LOS MORETTI SE PONEN DUROS»"),
        ];
        ShowBoards();
        var day = MarketInfoRules.DayOf(now);
        var facts = new NewspaperFacts(day, Boards.Take(3).ToList(), new() { [MarketInfoRules.Key(EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagItem, false)] = 300 },
            [new(EconomyCatalog.Moretti, "Familia Moretti", FamilyRules.Rivalry, 0, 3), new(EconomyCatalog.Caruso, "Familia Caruso", FamilyRules.Cooperation, 9, 0)],
            2, 1, ["«Café Don Sal», puesto de café, en la Esquina del café"],
            [new("Café Don Sal", "Puesto de café", "la Esquina del café", false, true), new("La Esquina de Vito", "Puesto de café", "la Esquina del café", true, true)],
            [new(TerritoryRules.Corner, "La Esquina", "Los del Puerto", 140, 0)],
            [new("feria", "Feria del puerto", null, TerritoryRules.Docks, now.AddHours(3), now.AddHours(4), 2, 20)], 14, 2);
        var paper = MarketInfoRules.Write(facts);
        Info = new MarketInfoView(
        [
            new(EconomyCatalog.DepositNpc, "Almacén Moretti", EconomyCatalog.CoffeeBagItem, "Bolsa de café", 330, true, false, now.AddMinutes(-12)),
            new(EconomyCatalog.DepositNpc, "Almacén Moretti", EconomyCatalog.DrinksCrateItem, "Cajón de bebidas", 400, false, false, now.AddMinutes(-12)),
            new(EconomyCatalog.DepositNpc, "Almacén Moretti", EconomyCatalog.CoffeeBagItem, "Bolsa de café", 180, true, true, now.AddMinutes(-12)),
            new(EconomyCatalog.CoffeeCornerNpc, "Esquina del café", EconomyCatalog.CoffeeBagItem, "Café en taza", 500, true, false, now.AddHours(-8)),
            new(EconomyCatalog.WorkshopNpc, "Taller Caruso", "vehiculo:camioneta", "Camioneta de reparto", 12_000, true, false, now.AddHours(-2)),
        ], paper, paper with { News = [], Quotes = [], Classifieds = [] },
            new InformantReport(now.AddMinutes(41),
            [
                new("Almacén Moretti", "En el depósito quedan 34 × bolsa de café, 0 × cajón de bebidas, 12 × caja de repuestos."),
                new("Almacén Moretti", "Tiene 3 pedidos por mayor en preparación (60 unidades). Hay cola."),
                new("Taller Caruso", "Por la puerta de atrás vende fierros: pistola $60.00 (nivel 2), escopeta $140.00 (nivel 4), thompson $320.00 (nivel 7). Y te los compra al 60 %."),
                new("Familia Moretti", "Están con la sangre en el ojo: cobran 10 % más y pagan menos los trabajos."),
                new("«La Esquina de Vito»", "Puesto de café: le quedan 4 × bolsa de café, con ayudante."),
                new("Fletes en la calle", "Hay 1 con carga: 15 cajones de bebidas de Moretti al Club de jazz (225 kg) (quedan 6 min)."),
            ]),
            [
                new(Guid.NewGuid(), "Café Don Sal", EconomyCatalog.CoffeeStandLocation, 3, 4_250, 560, 300, 520, 21, 27, 11_760, 1_200, 0,
                [
                    new(now.AddMinutes(-5), 0, 3, 0, 0), new(now.AddMinutes(-10), 2, 3, 1_000, 700), new(now.AddMinutes(-15), 3, 3, 1_500, 1_000),
                    new(now.AddMinutes(-20), 3, 3, 1_500, 1_000), new(now.AddMinutes(-25), 3, 4, 1_500, 1_000),
                ], new BusinessGrowthView(712, "Popular", 2000, "Café", 5, 1.5, 1.2, 560, 600, 250, 1500, 130, 5,
                [
                    new(BusinessGrowth.Capacity, "Capacidad", 2, "Mesitas en la vereda", "Salón con mesas", 3_070, "Atendés 1 cliente más por ciclo"),
                    new(BusinessGrowth.Quality, "Calidad", 2, "Máquina de espresso", "Café de primera", 4_610, "La gente te prefiere, da más fama y acepta un 10 % más de precio"),
                    new(BusinessGrowth.Attraction, "Atracción", 1, "Toldo a rayas", "Cartel de neón", 1_440, "Llega 20 % más de gente nueva"),
                ], "Mañana de feria", now.AddHours(2), now.AddHours(3), 4, "«Café Don Sal»: muy recomendable. Buena atención y buen producto.")),
            ]);
        InfoReceived?.Invoke();
    }
}
