using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Inventory;

/// <summary>
/// Pedidos por mayor a un proveedor (se abre desde el diálogo del mostrador). A la izquierda el catálogo con cantidad,
/// modo de entrega y total; a la derecha los pedidos propios con su estado (cancelar mientras se prepara, retirar cuando está listo).
/// </summary>
public partial class SupplierPanel : PanelContainer
{
    private static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    private static readonly Color Muted = new(0.72f, 0.68f, 0.6f);
    private static readonly Color Good = new(0.6f, 0.85f, 0.55f);

    private InventoryNetwork _inventory = null!;
    private Label _title = null!;
    private VBoxContainer _catalog = null!;
    private VBoxContainer _orders = null!;
    private Label _message = null!;
    private string _npc = "";
    private string? _selected;
    private int _quantity = SupplierRules.MinUnits;
    private int _mode; // 0 retiro, 1 depósito, 2 negocio

    public bool IsOpen => Visible;

    public void Initialize(InventoryNetwork inventory)
    {
        _inventory = inventory;
        _inventory.SupplierReceived += () => { if (Visible) Rebuild(); };
        _inventory.MessageReceived += text => { if (Visible) _message.Text = text; };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(640, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.07f, 0.94f),
            BorderColor = new Color(0.55f, 0.43f, 0.22f),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 19);
        _title.AddThemeColorOverride("font_color", Gold);
        box.AddChild(_title);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        box.AddChild(columns);
        _catalog = new VBoxContainer { CustomMinimumSize = new Vector2(300, 320) };
        _catalog.AddThemeConstantOverride("separation", 4);
        columns.AddChild(_catalog);
        columns.AddChild(new VSeparator());
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(300, 320) };
        _orders = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _orders.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_orders);
        columns.AddChild(scroll);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(600, 0) };
        _message.AddThemeColorOverride("font_color", Good);
        box.AddChild(_message);
        var close = new Button { Text = "Cerrar (Esc)" };
        close.Pressed += Close;
        box.AddChild(close);
    }

    public void Open(string npc)
    {
        _npc = npc;
        _message.Text = "";
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _inventory.RequestSupplier(npc);
        Rebuild();
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private Label Text(string text, Color? color = null, int width = 290)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width, 0) };
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    private void Rebuild()
    {
        foreach (var child in _catalog.GetChildren()) child.QueueFree();
        foreach (var child in _orders.GetChildren()) child.QueueFree();
        var view = _inventory.Supplier is { } s && s.SupplierCode == _npc ? s : null;
        _title.Text = $"Pedidos por mayor — {SupplierRules.Names.GetValueOrDefault(_npc, "Proveedor")}";
        if (view is null)
        {
            _catalog.AddChild(Text(Multiplayer.IsServer() ? "Los pedidos necesitan un servidor con cuentas." : "Cargando…", Muted));
            return;
        }
        BuildCatalog(view);
        BuildOrders(view);
    }

    private void BuildCatalog(SupplierView view)
    {
        _catalog.AddChild(Text("Se paga al pedir. Sale del stock del proveedor y queda apartado hasta que lo retirás o llega al depósito.", Muted));
        if (_selected is null || view.Items.All(i => i.ItemCode != _selected))
            _selected = view.Items.FirstOrDefault()?.ItemCode;
        foreach (var item in view.Items)
        {
            var button = new Button
            {
                Text = $"{item.Name}   {EconomyCatalog.FormatMoney(item.UnitPriceCents)} c/u",
                Alignment = HorizontalAlignment.Left, ToggleMode = true, ButtonPressed = item.ItemCode == _selected,
            };
            var code = item.ItemCode;
            button.Pressed += () => { _selected = code; Rebuild(); };
            _catalog.AddChild(button);
        }
        if (view.Items.FirstOrDefault(i => i.ItemCode == _selected) is not { } chosen)
            return;

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Cantidad:" });
        var qty = new SpinBox { MinValue = SupplierRules.MinUnits, MaxValue = SupplierRules.MaxUnits, Value = _quantity, Rounded = true, CustomMinimumSize = new Vector2(100, 0) };
        row.AddChild(qty);
        _catalog.AddChild(row);

        var fee = $"+{SupplierRules.DeliveryPercent} %, mín. {EconomyCatalog.FormatMoney(SupplierRules.DeliveryMinCents)}";
        var business = view.Businesses?.FirstOrDefault(b => b.ItemCode == chosen.ItemCode);
        var mode = new OptionButton();
        mode.AddItem("Retiro en el mostrador (gratis)");
        mode.AddItem($"Envío al depósito ({fee})");
        mode.AddItem(business is null ? $"Envío a tu negocio ({fee})" : $"Envío a tu {business.Name.ToLowerInvariant()} ({fee})");
        mode.SetItemDisabled(1, !view.HasWarehouse);
        mode.SetItemDisabled(2, business is null);
        if ((_mode == 1 && !view.HasWarehouse) || (_mode == 2 && business is null)) _mode = 0;
        mode.Selected = _mode;
        _catalog.AddChild(mode);
        if (!view.HasWarehouse && business is null)
            _catalog.AddChild(Text("Los envíos llegan al depósito de tu cuadrilla (se abre en La Oficina) o a tu negocio si vende esto.", Muted));

        var summary = Text("", Gold);
        _catalog.AddChild(summary);
        void Update()
        {
            _quantity = (int)qty.Value;
            _mode = mode.Selected;
            var shipped = _mode > 0;
            var goods = SupplierRules.GoodsCents(chosen.UnitPriceCents, _quantity);
            var shipping = shipped ? SupplierRules.DeliveryCents(goods) : 0;
            var cycles = SupplierRules.PrepCycles(_quantity, view.UnitsInQueue) + (shipped ? SupplierRules.DeliveryExtraCycles : 0);
            summary.Text = $"Total: {EconomyCatalog.FormatMoney(goods + shipping)}"
                           + (_quantity >= SupplierRules.VolumeDiscountFrom ? $"  ({SupplierRules.VolumeDiscountPercent} % menos por volumen)" : $"  ({SupplierRules.VolumeDiscountPercent} % menos desde {SupplierRules.VolumeDiscountFrom})")
                           + $"\n{(shipped ? "Llega" : "Listo")} en {(cycles == 1 ? "1 ciclo" : $"unos {cycles} ciclos")} (5 min c/u) · {_quantity * chosen.UnitWeightKg:0.#} kg";
        }
        qty.ValueChanged += _ => Update();
        mode.ItemSelected += _ => Update();
        Update();

        var order = new Button { Text = "Hacer el pedido" };
        order.Pressed += () => _inventory.PlaceOrder(_npc, chosen.ItemCode, (int)qty.Value,
            mode.Selected switch { 1 => SupplierRules.Delivery, 2 => SupplierRules.ToBusiness, _ => SupplierRules.Pickup });
        _catalog.AddChild(order);
    }

    private void BuildOrders(SupplierView view)
    {
        var title = new Label { Text = "Tus pedidos" };
        title.AddThemeColorOverride("font_color", Gold);
        _orders.AddChild(title);
        if (view.UnitsInQueue > 0)
            _orders.AddChild(Text($"En cola en el proveedor: {view.UnitsInQueue} unidades (prepara {SupplierRules.SupplierUnitsPerCycle} por ciclo).", Muted));
        if (view.Orders.Count == 0)
            _orders.AddChild(Text("No tenés pedidos.", Muted));
        foreach (var o in view.Orders)
        {
            var delivery = o.Delivery switch { SupplierRules.Delivery => "envío al depósito", SupplierRules.ToBusiness => "envío al negocio", _ => "retiro" };
            var state = o.Status switch
            {
                "entregado" => o.Delivery switch { SupplierRules.Delivery => "entregado en el depósito", SupplierRules.ToBusiness => "entregado en el negocio", _ => "retirado" },
                "cancelado" => "cancelado",
                _ when o.Ready && o.Delivery == SupplierRules.Pickup => "¡listo para retirar!",
                _ when o.Ready => $"en camino · llega {o.DeliverAt.ToLocalTime():HH:mm}",
                _ => $"preparando · {(SupplierRules.IsShipped(o.Delivery) ? $"llega {o.DeliverAt.ToLocalTime():HH:mm}" : $"listo {o.ReadyAt.ToLocalTime():HH:mm}")}",
            };
            _orders.AddChild(Text($"{o.Quantity} × {o.ItemName} — {delivery}", o.Status == "preparando" ? null : Muted));
            _orders.AddChild(Text($"{EconomyCatalog.FormatMoney(o.GoodsCents + o.DeliveryCents)} · {state}", o.Ready && o.Status == "preparando" ? Good : Muted));
            if (o.Status == "preparando")
            {
                var row = new HBoxContainer();
                if (o.Ready && o.Delivery == SupplierRules.Pickup)
                {
                    var pickup = new Button { Text = "Retirar" };
                    var id = o.Id;
                    pickup.Pressed += () => _inventory.PickupOrder(_npc, id);
                    row.AddChild(pickup);
                }
                if (!o.Ready)
                {
                    var cancel = new Button { Text = "Cancelar" };
                    var id = o.Id;
                    cancel.Pressed += () => _inventory.CancelOrder(_npc, id);
                    row.AddChild(cancel);
                }
                if (row.GetChildCount() > 0)
                    _orders.AddChild(row);
            }
            _orders.AddChild(new HSeparator());
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
