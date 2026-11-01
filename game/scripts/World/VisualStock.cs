using System.Text.Json;
using Distrito.Game.Network;
using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Inventario, etapa 7: las existencias se ven en Bellmont. El servidor consulta al backend lo que hay de verdad
/// (depósitos de cuadrilla, estantes del proveedor, carga de los vehículos) y lo reparte cuando cambia; los clientes
/// solo lo dibujan, con bultos agrupados. Guardar, retirar, cargar o pedir algo lo actualiza enseguida.
/// </summary>
public partial class VisualStock : Node3D
{
    private const double PollSeconds = 5;

    /// <summary>Galpón de depósitos de Valenti: bahías a lo largo de z, en el borde oeste.</summary>
    public const float ShedX = -25f;
    public static float BayZ(int bay) => -9f + 3f * bay;
    private const float BayWidth = 2.6f, BayDepth = 6f;

    /// <summary>Estantes del Almacén Moretti y pedidos listos.</summary>
    private static readonly Vector3 ShelvesAt = new(-6.4f, 0, -12.3f);
    private static readonly Vector3 PalletsAt = new(-2.5f, 0, -10f);

    private static VisualStock? _instance;
    private GameNetwork _network = null!;
    private string _last = "";
    private double _clock, _nextPoll;
    private bool _polling;

    private readonly Node3D[] _bays = new Node3D[StockVisualRules.WarehouseBays];
    private Node3D _shelves = null!;
    private Node3D _pallets = null!;
    private readonly Dictionary<int, Node3D> _baseStock = new();

    /// <summary>Pide actualizar pronto (después de guardar, retirar, cargar, pedir…).</summary>
    public static void RequestRefresh()
    {
        if (_instance is not null)
            _instance._nextPoll = Math.Min(_instance._nextPoll, _instance._clock + 0.3);
    }

    /// <summary>¿Está en una bahía del galpón de depósitos? (también vale para guardar y retirar)</summary>
    public static bool AtShed(Vector3 p) =>
        p.X > ShedX - BayDepth / 2 - 1 && p.X < ShedX + BayDepth / 2 + 2 && p.Z > BayZ(0) - 2 && p.Z < BayZ(StockVisualRules.WarehouseBays - 1) + 2;

    public void Initialize(GameNetwork network)
    {
        _instance = this;
        _network = network;
        _network.PlayerAdmitted += (peer, _) =>
        {
            if (_last.Length > 0)
                RpcId(peer, MethodName.ReceiveStock, _last);
        };
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public override void _Ready()
    {
        BuildShed();
        BuildShelves();
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 at, Color color) =>
        new() { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = color }, Position = at };

    private void BuildShed()
    {
        var shed = new Node3D { Name = "DepositosValenti" };
        AddChild(shed);
        var length = 3f * StockVisualRules.WarehouseBays + 1;
        var centerZ = (BayZ(0) + BayZ(StockVisualRules.WarehouseBays - 1)) / 2;
        var wood = new Color(0.33f, 0.24f, 0.16f);
        shed.AddChild(Box(new Vector3(BayDepth + 1, 0.04f, length), new Vector3(ShedX, 0.02f, centerZ), new Color(0.24f, 0.23f, 0.21f)));
        shed.AddChild(Box(new Vector3(BayDepth + 1.4f, 0.15f, length + 0.4f), new Vector3(ShedX, 4f, centerZ), new Color(0.2f, 0.18f, 0.16f)));
        foreach (var dx in new[] { -BayDepth / 2 - 0.3f, BayDepth / 2 + 0.3f })
            for (var i = 0; i <= StockVisualRules.WarehouseBays; i++)
                shed.AddChild(Box(new Vector3(0.18f, 4f, 0.18f), new Vector3(ShedX + dx, 2f, BayZ(0) - 1.5f + 3f * i), wood));
        shed.AddChild(Box(new Vector3(0.12f, 3.9f, length), new Vector3(ShedX - BayDepth / 2 - 0.4f, 1.95f, centerZ), new Color(0.29f, 0.21f, 0.15f)));
        shed.AddChild(new Label3D
        {
            Text = "DEPÓSITOS VALENTI", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 52, OutlineSize = 10,
            Modulate = new Color(0.95f, 0.8f, 0.45f), Position = new Vector3(ShedX + BayDepth / 2 + 0.5f, 4.6f, centerZ),
        });
        for (var i = 0; i < StockVisualRules.WarehouseBays; i++)
        {
            // Separadores pintados en el piso entre bahías.
            shed.AddChild(Box(new Vector3(BayDepth, 0.02f, 0.08f), new Vector3(ShedX, 0.05f, BayZ(i) - 1.5f), new Color(0.75f, 0.68f, 0.45f)));
            _bays[i] = new Node3D { Name = $"Bay{i}", Position = new Vector3(ShedX, 0.05f, BayZ(i)) };
            shed.AddChild(_bays[i]);
            // Una lamparita por bahía: luz cálida bajo el techo.
            shed.AddChild(new OmniLight3D
            {
                Position = new Vector3(ShedX, 3.6f, BayZ(i)), LightColor = new Color(1f, 0.82f, 0.55f), LightEnergy = 1.4f, OmniRange = 5.5f,
            });
        }
    }

    private void BuildShelves()
    {
        _shelves = new Node3D { Name = "EstantesMoretti", Position = ShelvesAt };
        AddChild(_shelves);
        _pallets = new Node3D { Name = "PedidosMoretti", Position = PalletsAt };
        AddChild(_pallets);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (Multiplayer.MultiplayerPeer is null || !Multiplayer.IsServer() || _network?.Backend is not { } backend || _polling || _clock < _nextPoll)
            return;
        _nextPoll = _clock + PollSeconds;
        _ = PollAsync(backend);
    }

    private async Task PollAsync(DistritoApiClient backend)
    {
        _polling = true;
        try
        {
            var result = await backend.GetWorldStockAsync(_network.ServerKey);
            if (!result.IsSuccess)
                return;
            var json = JsonSerializer.Serialize(result.Value, DistritoApiClient.Json);
            if (json == _last)
                return;
            _last = json;
            foreach (var p in Multiplayer.GetPeers())
                RpcId(p, MethodName.ReceiveStock, json);
            Apply(result.Value!);
        }
        finally
        {
            _polling = false;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveStock(string json)
    {
        if (JsonSerializer.Deserialize<WorldStockView>(json, DistritoApiClient.Json) is { } view)
            Apply(view);
    }

    /// <summary>Dibuja todo de nuevo según los datos (pocos nodos: bultos agrupados y con tope).</summary>
    public void Apply(WorldStockView view)
    {
        for (var i = 0; i < _bays.Length; i++)
        {
            foreach (var child in _bays[i].GetChildren()) child.QueueFree();
            var w = view.Warehouses.FirstOrDefault(x => x.Bay == i);
            var sign = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 30, OutlineSize = 6,
                Position = new Vector3(BayDepth / 2 + 0.2f, 2.7f, 0),
            };
            if (w is null)
            {
                sign.Text = "Bahía libre";
                sign.Modulate = new Color(0.6f, 0.58f, 0.52f);
            }
            else
            {
                var kg = w.Items.Sum(p => p.WeightKg);
                sign.Text = $"«{w.CrewName}»\n{kg:0} kg{(w.CapacityKg is { } cap ? $" / {cap:0}" : "")}";
                sign.Modulate = new Color(0.95f, 0.85f, 0.6f);
                var area = new Node3D { Rotation = new Vector3(0, Mathf.Pi / 2, 0) };
                _bays[i].AddChild(area);
                StockProps.Layout(area, w.Items, BayWidth, BayDepth - 0.6f, levels: 3);
            }
            _bays[i].AddChild(sign);
        }

        // Depósitos de sede: la mercadería se ve apilada al fondo del galpón.
        foreach (var node in _baseStock.Values) node.QueueFree();
        _baseStock.Clear();
        foreach (var w in view.Warehouses.Where(w => w.Shed is not null))
        {
            var shed = w.Shed!.Value;
            if (shed < 0 || shed >= Distrito.Shared.Crews.CrewBaseRules.Sheds.Count)
                continue;
            var s = Distrito.Shared.Crews.CrewBaseRules.Sheds[shed];
            var side = Distrito.Shared.Crews.CrewBaseRules.DoorSide(shed);
            var area = new Node3D { Position = new Vector3(s.X, 0.05f, s.Z - side * (s.Depth / 2 - 2.2f)) };
            AddChild(area);
            _baseStock[shed] = area;
            StockProps.Layout(area, w.Items, s.Width - 2f, 3.2f, levels: 3);
        }

        foreach (var child in _shelves.GetChildren()) child.QueueFree();
        if (view.Suppliers.FirstOrDefault(s => s.SupplierCode == EconomyCatalog.DepositNpc) is { } moretti)
        {
            var codes = SupplierRules.Catalog[EconomyCatalog.DepositNpc];
            for (var i = 0; i < codes.Length; i++)
            {
                var pile = moretti.Items.FirstOrDefault(p => p.ItemCode == codes[i]);
                BuildShelf(new Vector3(-2f + 2f * i, 0, 0), codes[i], pile);
            }
            foreach (var child in _pallets.GetChildren()) child.QueueFree();
            for (var i = 0; i < Math.Min(moretti.ReadyOrders, 6); i++)
            {
                var pallet = new Node3D { Position = new Vector3((i % 3) * 0.9f, 0, (i / 3) * 0.9f) };
                pallet.AddChild(Box(new Vector3(0.8f, 0.12f, 0.8f), new Vector3(0, 0.06f, 0), new Color(0.45f, 0.35f, 0.22f)));
                var crate = StockProps.Build(EconomyCatalog.SparePartsItem, ItemKinds.Goods);
                crate.Position = new Vector3(0, 0.12f, 0);
                pallet.AddChild(crate);
                _pallets.AddChild(pallet);
            }
            if (moretti.ReadyOrders + moretti.PreparingOrders > 0)
                _pallets.AddChild(new Label3D
                {
                    Text = $"Pedidos listos: {moretti.ReadyOrders}\nEn preparación: {moretti.PreparingOrders}",
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 28, OutlineSize = 6,
                    Position = new Vector3(0.9f, 1.4f, 0.4f), Modulate = new Color(0.9f, 0.85f, 0.7f),
                });
        }

        foreach (var v in view.Vehicles)
            Distrito.Game.Transport.VehicleNetwork.Find(v.VehicleId.ToString())?.SetCargo(v.Items);
    }

    /// <summary>Un estante: hasta tres tablas llenas según cuánto le queda al proveedor.</summary>
    private void BuildShelf(Vector3 at, string itemCode, StockPile? pile)
    {
        var shelf = new Node3D { Position = at };
        _shelves.AddChild(shelf);
        var wood = new Color(0.3f, 0.22f, 0.15f);
        foreach (var dx in new[] { -0.85f, 0.85f })
            shelf.AddChild(Box(new Vector3(0.08f, 2.2f, 0.5f), new Vector3(dx, 1.1f, 0), wood));
        var level = StockVisualRules.ShelfLevel(pile?.Quantity ?? 0);
        for (var board = 0; board < 3; board++)
        {
            var y = 0.15f + 0.7f * board;
            shelf.AddChild(Box(new Vector3(1.7f, 0.05f, 0.5f), new Vector3(0, y, 0), wood));
            if (board >= level || pile is null)
                continue;
            var row = new Node3D { Position = new Vector3(0, y + 0.03f, 0) };
            shelf.AddChild(row);
            StockProps.Layout(row, [pile with { Quantity = 3, Kind = pile.Kind }], 1.6f, 0.45f, levels: 1);
        }
        shelf.AddChild(new Label3D
        {
            Text = pile is null ? "Agotado" : itemCode.Replace('-', ' '),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 22, OutlineSize = 5,
            Position = new Vector3(0, 2.45f, 0), Modulate = new Color(0.9f, 0.85f, 0.7f),
        });
    }

    /// <summary>Solo para la prueba visual: datos de ejemplo.</summary>
    public void Preview()
    {
        Apply(new WorldStockView(
        [
            new(Guid.NewGuid(), "Los del Puerto", 0, [new("caja-repuestos", ItemKinds.Goods, 14, 168), new("bolsa-cafe", ItemKinds.RawMaterial, 8, 40), new("bala-pistola", ItemKinds.Ammo, 120, 2.4)], 300),
            new(Guid.NewGuid(), "La Banda del Norte", 1, [new("cajon-bebidas", ItemKinds.Goods, 10, 150)], 300),
        ],
        [new(EconomyCatalog.DepositNpc, [new("caja-repuestos", ItemKinds.Goods, 99_000, 0), new("bolsa-cafe", ItemKinds.RawMaterial, 3_000, 0), new("cajon-bebidas", ItemKinds.Goods, 60_000, 0)], 2, 1)],
        []));
    }
}
