using System.Globalization;
using Distrito.Game.Combat;
using Distrito.Game.Network;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Inventory;

/// <summary>
/// Inventario (tecla I). Pestaña «Encima»: lo que llevás, agrupado por clase, con peso y volumen; el objeto elegido
/// se equipa, se suelta, se da en mano o se guarda en el depósito. Pestaña «Depósito»: lo guardado por la cuadrilla
/// (retirar, permisos del jefe, historial de movimientos).
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    private static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    private static readonly Color Muted = new(0.72f, 0.68f, 0.6f);
    private static readonly Color Warn = new(0.92f, 0.5f, 0.38f);

    private enum Tab { Carried, Warehouse }

    private InventoryNetwork _inventory = null!;
    private CombatNetwork _combat = null!;
    private GameNetwork _network = null!;
    private HBoxContainer _tabs = null!;
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private Label _totals = null!;
    private Label _message = null!;
    private Tab _tab = Tab.Carried;
    private string? _selected;
    private bool _showHistory;
    private bool _showPerms;

    public bool IsOpen => Visible;

    public void Initialize(InventoryNetwork inventory, CombatNetwork combat, GameNetwork network, Distrito.Game.Economy.EconomyNetwork economy)
    {
        _inventory = inventory;
        _combat = combat;
        _network = network;
        _inventory.InventoryReceived += () => { if (Visible) Rebuild(); };
        _inventory.MessageReceived += text => _message.Text = text;
        // Si cambia lo que llevás (compraste, levantaste algo, te dieron), se vuelve a pedir.
        economy.EconomyChanged += _ => { if (Visible) _inventory.Request(); };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(580, 0);
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

        var header = new HBoxContainer();
        var title = new Label { Text = "Inventario" };
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", Gold);
        header.AddChild(title);
        _tabs = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        header.AddChild(_tabs);
        _totals = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _totals.AddThemeColorOverride("font_color", Muted);
        header.AddChild(_totals);
        box.AddChild(header);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        box.AddChild(columns);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(260, 300) };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_list);
        columns.AddChild(scroll);
        columns.AddChild(new VSeparator());
        _detail = new VBoxContainer { CustomMinimumSize = new Vector2(270, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 5);
        columns.AddChild(_detail);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) };
        _message.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 0.75f));
        box.AddChild(_message);

        var close = new Button { Text = "Cerrar (I)" };
        close.Pressed += Toggle;
        box.AddChild(close);
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _message.Text = "";
            _inventory.Request();
            Rebuild();
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>Para la prueba visual: muestra la pestaña del depósito.</summary>
    public void ShowWarehouseTab()
    {
        _tab = Tab.Warehouse;
        _selected = null;
        Rebuild();
    }

    private static string Kg(double v) => v.ToString(v < 10 ? "0.##" : "0.#", CultureInfo.InvariantCulture);

    private static string Totals(double kg, double? capKg, double l, double? capL) =>
        $"{Kg(kg)} kg{(capKg is { } ck ? $" / {Kg(ck)}" : "")} · {Kg(l)} L{(capL is { } cl ? $" / {Kg(cl)}" : "")}"
        + (capKg is null && capL is null ? " · sin límite" : "");

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren()) child.QueueFree();
    }

    private Label Muted_(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) };
        label.AddThemeColorOverride("font_color", Muted);
        return label;
    }

    private void Rebuild()
    {
        Clear(_tabs);
        Clear(_list);
        Clear(_detail);

        var warehouse = _inventory.Warehouse;
        if (warehouse is null && _tab == Tab.Warehouse)
            _tab = Tab.Carried;
        if (warehouse is not null)
        {
            AddTab("Encima", Tab.Carried);
            AddTab(_inventory.AtWarehouse ? "Depósito ●" : "Depósito", Tab.Warehouse);
        }

        if (_tab == Tab.Warehouse && warehouse is not null)
            BuildWarehouse(warehouse);
        else
            BuildCarried();
    }

    private void AddTab(string text, Tab tab)
    {
        var button = new Button { Text = text, ToggleMode = true, ButtonPressed = _tab == tab, Flat = _tab != tab };
        button.Pressed += () => { _tab = tab; _selected = null; _showHistory = false; _showPerms = false; Rebuild(); };
        _tabs.AddChild(button);
    }

    /// <summary>Lista agrupada por clase; devuelve el objeto elegido (o null).</summary>
    private InventoryEntry? BuildList(List<InventoryEntry> items, string empty)
    {
        if (items.Count == 0)
        {
            _list.AddChild(Muted_(empty));
            _selected = null;
            return null;
        }
        if (_selected is not null && items.All(i => i.ItemCode != _selected))
            _selected = null;
        _selected ??= items[0].ItemCode;

        foreach (var group in items.GroupBy(i => i.Kind))
        {
            var kind = new Label { Text = ItemKinds.Of(group.Key).Name.ToUpperInvariant() };
            kind.AddThemeFontSizeOverride("font_size", 12);
            kind.AddThemeColorOverride("font_color", Muted);
            _list.AddChild(kind);
            foreach (var item in group)
            {
                var equipped = _tab == Tab.Carried && _network.LocalPlayer?.Weapon == item.ItemCode;
                var button = new Button
                {
                    Text = $"{item.Name}{(equipped ? "  (en la mano)" : "")}   ×{item.Quantity}",
                    Alignment = HorizontalAlignment.Left,
                    ToggleMode = true,
                    ButtonPressed = item.ItemCode == _selected && !_showHistory && !_showPerms,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                var code = item.ItemCode;
                button.Pressed += () => { _selected = code; _showHistory = false; _showPerms = false; Rebuild(); };
                _list.AddChild(button);
            }
        }
        return items.FirstOrDefault(i => i.ItemCode == _selected);
    }

    // ---------------- Encima ----------------

    private void BuildCarried()
    {
        var view = _inventory.Last;
        if (view is null)
        {
            _totals.Text = "";
            _list.AddChild(Muted_(Multiplayer.IsServer() ? "En una partida local sin cuentas no hay inventario guardado." : "Cargando…"));
            return;
        }
        _totals.Text = Totals(view.WeightKg, view.CapacityKg, view.VolumeL, view.CapacityL);
        if (BuildList(view.Items, "No llevás nada encima.") is not { } item)
            return;

        var behavior = ItemKinds.Of(item.Kind);
        ItemFacts(item);

        if (Weapons.Find(item.ItemCode) is not null)
        {
            var inHand = _network.LocalPlayer?.Weapon == item.ItemCode;
            AddButton(inHand ? "Guardar (piñas)" : "Equipar", () =>
            {
                _combat.RequestWeapon(inHand ? Weapons.Fists : item.ItemCode);
                // El arma en la mano se replica enseguida; se redibuja un instante después.
                GetTree().CreateTimer(0.3).Timeout += () => { if (Visible) Rebuild(); };
            });
        }
        if (!behavior.Tradable)
        {
            _detail.AddChild(Muted_("Objeto de misión: no se suelta ni se pasa."));
            return;
        }

        var qty = QuantityRow(item.Quantity);
        AddButton("Soltar en la calle", () => _inventory.Drop(item.ItemCode, (int)qty.Value));

        if (_inventory.Warehouse is { Exists: true, CanDeposit: true } && _inventory.AtWarehouse)
            AddButton("Guardar en el depósito", () => _inventory.MoveWarehouse(item.ItemCode, (int)qty.Value, deposit: true));

        if (_inventory.Nearby.Count == 0)
        {
            _detail.AddChild(Muted_("Para dárselo a alguien, acercate (a menos de 3 m) y volvé a abrir el inventario."));
            return;
        }
        var giveRow = new HBoxContainer();
        var who = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var n in _inventory.Nearby)
            who.AddItem(n.Name);
        giveRow.AddChild(who);
        var give = new Button { Text = "Dar" };
        give.Pressed += () =>
        {
            if (who.Selected >= 0 && who.Selected < _inventory.Nearby.Count)
                _inventory.Give(_inventory.Nearby[who.Selected].Peer, item.ItemCode, (int)qty.Value);
        };
        giveRow.AddChild(give);
        _detail.AddChild(giveRow);
    }

    // ---------------- Depósito ----------------

    private void BuildWarehouse(WarehouseView w)
    {
        if (!w.Exists)
        {
            _totals.Text = "";
            _list.AddChild(Muted_($"«{w.CrewName}» todavía no tiene depósito."));
            _detail.AddChild(new Label
            {
                Text = $"Valenti alquila un depósito en {w.PlaceName}: {EconomyCatalog.FormatMoney(w.OpenCostCents)} de la caja para abrirlo y "
                       + $"{EconomyCatalog.FormatMoney(w.RentCents)} por ciclo mientras haya algo guardado."
                       + (w.CapacityKg is { } kg ? $" Entran {Kg(kg)} kg." : ""),
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0),
            });
            if (!w.CanAdmin)
                _detail.AddChild(Muted_("Lo abre el jefe de la cuadrilla."));
            else if (!_inventory.AtWarehouse)
                _detail.AddChild(Muted_($"Andá a {w.PlaceName} para abrirlo."));
            else
                AddButton("Abrir depósito", _inventory.OpenWarehouse);
            return;
        }
        if (!w.CanView)
        {
            _totals.Text = "";
            _list.AddChild(Muted_("Tu jefe no te dejó ver el depósito."));
            return;
        }

        _totals.Text = Totals(w.WeightKg, w.CapacityKg, w.VolumeL, w.CapacityL);
        var item = BuildList(w.Items, "El depósito está vacío.");
        var history = new Button { Text = "Historial", ToggleMode = true, ButtonPressed = _showHistory };
        history.Pressed += () => { _showHistory = !_showHistory; _showPerms = false; Rebuild(); };
        _list.AddChild(new HSeparator());
        _list.AddChild(history);
        if (w.CanAdmin)
        {
            var perms = new Button { Text = "Permisos", ToggleMode = true, ButtonPressed = _showPerms };
            perms.Pressed += () => { _showPerms = !_showPerms; _showHistory = false; Rebuild(); };
            _list.AddChild(perms);
        }

        if (w.DebtCents > 0)
        {
            var debt = new Label { Text = $"Se deben {EconomyCatalog.FormatMoney(w.DebtCents)} de alquiler: no se puede retirar hasta que la caja pague.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) };
            debt.AddThemeColorOverride("font_color", Warn);
            _detail.AddChild(debt);
        }

        if (_showHistory)
        {
            BuildHistory(w);
            return;
        }
        if (_showPerms && w.CanAdmin)
        {
            BuildPermissions(w);
            return;
        }
        if (item is null)
        {
            _detail.AddChild(Muted_(_inventory.AtWarehouse
                ? "Para guardar algo, elegilo en la pestaña «Encima»."
                : $"El depósito está en {w.PlaceName}: para guardar o retirar tenés que estar ahí."));
            return;
        }
        ItemFacts(item);
        if (!w.CanWithdraw)
        {
            _detail.AddChild(Muted_("Tu jefe no te dejó retirar del depósito."));
            return;
        }
        if (!_inventory.AtWarehouse)
        {
            _detail.AddChild(Muted_($"Para retirar tenés que estar en {w.PlaceName}."));
            return;
        }
        var qty = QuantityRow(item.Quantity);
        AddButton("Retirar", () => _inventory.MoveWarehouse(item.ItemCode, (int)qty.Value, deposit: false));
    }

    private void BuildHistory(WarehouseView w)
    {
        var title = new Label { Text = "Últimos movimientos" };
        title.AddThemeColorOverride("font_color", Gold);
        _detail.AddChild(title);
        if (w.History.Count == 0)
            _detail.AddChild(Muted_("Todavía no hubo movimientos."));
        foreach (var m in w.History)
            _detail.AddChild(Muted_($"{m.At.ToLocalTime():dd/MM HH:mm} · {m.Who} {(m.Deposit ? "guardó" : "retiró")} {m.Quantity} × {m.ItemName}"));
    }

    private void BuildPermissions(WarehouseView w)
    {
        var title = new Label { Text = "Quién puede usar el depósito" };
        title.AddThemeColorOverride("font_color", Gold);
        _detail.AddChild(title);
        _detail.AddChild(Muted_("El jefe siempre puede todo. El segundo siempre puede ver y guardar."));
        var p = w.Permissions;
        var view = Check("Los miembros ven lo guardado", p.MemberCanView);
        var deposit = Check("Los miembros guardan", p.MemberCanDeposit);
        var withdraw = Check("Los miembros retiran", p.MemberCanWithdraw);
        var second = Check("El segundo retira", p.SecondCanWithdraw);
        AddButton("Guardar permisos", () => _inventory.SetWarehousePermissions(
            new WarehousePermissions(view.ButtonPressed, deposit.ButtonPressed, withdraw.ButtonPressed, second.ButtonPressed)));
    }

    private CheckBox Check(string text, bool value)
    {
        var box = new CheckBox { Text = text, ButtonPressed = value };
        _detail.AddChild(box);
        return box;
    }

    // ---------------- Comunes ----------------

    private void ItemFacts(InventoryEntry item)
    {
        var behavior = ItemKinds.Of(item.Kind);
        var name = new Label { Text = item.Name };
        name.AddThemeFontSizeOverride("font_size", 17);
        name.AddThemeColorOverride("font_color", Gold);
        _detail.AddChild(name);
        if (item.Description.Length > 0)
            _detail.AddChild(new Label { Text = item.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) });
        var legal = item.LegalStatus switch { "illegal" => "Ilegal", "restricted" => "Restringido", _ => "Legal" };
        _detail.AddChild(Muted_($"{behavior.Name} · {legal}\n"
                                + $"Cantidad: {item.Quantity}\n"
                                + $"Peso: {Kg(item.UnitWeightKg)} kg c/u · {Kg(item.WeightKg)} kg en total\n"
                                + $"Volumen: {Kg(item.UnitVolumeL)} L c/u · {Kg(item.VolumeL)} L en total\n"
                                + $"Valor de referencia: {EconomyCatalog.FormatMoney(item.ReferencePriceCents)} c/u"
                                + (behavior.Consumable ? "\nSe gasta al usarlo." : "")));
    }

    private SpinBox QuantityRow(int max)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Cantidad:" });
        var qty = new SpinBox { MinValue = 1, MaxValue = Math.Max(1, max), Value = 1, Rounded = true, CustomMinimumSize = new Vector2(90, 0) };
        row.AddChild(qty);
        var all = new Button { Text = "Todo" };
        all.Pressed += () => qty.Value = max;
        row.AddChild(all);
        _detail.AddChild(row);
        return qty;
    }

    private void AddButton(string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        _detail.AddChild(button);
    }
}
