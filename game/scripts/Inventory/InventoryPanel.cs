using System.Globalization;
using Distrito.Game.Combat;
using Distrito.Game.Network;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Inventory;

/// <summary>
/// Inventario personal (tecla I): lo que llevás encima agrupado por clase, peso y volumen, y el detalle del objeto
/// elegido con sus acciones (equipar, soltar en la calle, dar en mano a alguien cerca).
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    private static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    private static readonly Color Muted = new(0.72f, 0.68f, 0.6f);

    private InventoryNetwork _inventory = null!;
    private CombatNetwork _combat = null!;
    private GameNetwork _network = null!;
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private ScrollContainer _scroll = null!;
    private Label _totals = null!;
    private Label _message = null!;
    private string? _selected;

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
        CustomMinimumSize = new Vector2(560, 0);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        var header = new HBoxContainer();
        var title = new Label { Text = "Inventario", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", Gold);
        header.AddChild(title);
        _totals = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _totals.AddThemeColorOverride("font_color", Muted);
        header.AddChild(_totals);
        box.AddChild(header);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        box.AddChild(columns);

        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(250, 300) };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_list);
        columns.AddChild(_scroll);
        columns.AddChild(new VSeparator());
        _detail = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 5);
        columns.AddChild(_detail);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0) };
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

    private static string Kg(double v) => v.ToString(v < 10 ? "0.##" : "0.#", CultureInfo.InvariantCulture);

    private void Rebuild()
    {
        foreach (var child in _list.GetChildren()) child.QueueFree();
        foreach (var child in _detail.GetChildren()) child.QueueFree();

        var view = _inventory.Last;
        if (view is null)
        {
            _totals.Text = "";
            _list.AddChild(new Label { Text = Multiplayer.IsServer() ? "En una partida local sin cuentas no hay inventario guardado." : "Cargando…",
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) });
            return;
        }
        _totals.Text = $"{Kg(view.WeightKg)} kg{(view.CapacityKg is { } kg ? $" / {Kg(kg)}" : "")} · "
                       + $"{Kg(view.VolumeL)} L{(view.CapacityL is { } l ? $" / {Kg(l)}" : "")}"
                       + (view.CapacityKg is null && view.CapacityL is null ? " · sin límite" : "");

        if (view.Items.Count == 0)
        {
            _list.AddChild(new Label { Text = "No llevás nada encima.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            _selected = null;
        }
        if (_selected is not null && view.Items.All(i => i.ItemCode != _selected))
            _selected = null;
        _selected ??= view.Items.FirstOrDefault()?.ItemCode;

        foreach (var group in view.Items.GroupBy(i => i.Kind))
        {
            var kind = new Label { Text = ItemKinds.Of(group.Key).Name.ToUpperInvariant() };
            kind.AddThemeFontSizeOverride("font_size", 12);
            kind.AddThemeColorOverride("font_color", Muted);
            _list.AddChild(kind);
            foreach (var item in group)
            {
                var equipped = _network.LocalPlayer?.Weapon == item.ItemCode;
                var button = new Button
                {
                    Text = $"{item.Name}{(equipped ? "  (en la mano)" : "")}   ×{item.Quantity}",
                    Alignment = HorizontalAlignment.Left,
                    ToggleMode = true,
                    ButtonPressed = item.ItemCode == _selected,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                var code = item.ItemCode;
                button.Pressed += () => { _selected = code; Rebuild(); };
                _list.AddChild(button);
            }
        }

        if (view.Items.FirstOrDefault(i => i.ItemCode == _selected) is { } selected)
            BuildDetail(selected);
    }

    private void BuildDetail(InventoryEntry item)
    {
        var behavior = ItemKinds.Of(item.Kind);
        var name = new Label { Text = item.Name };
        name.AddThemeFontSizeOverride("font_size", 17);
        name.AddThemeColorOverride("font_color", Gold);
        _detail.AddChild(name);
        if (item.Description.Length > 0)
            _detail.AddChild(new Label { Text = item.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) });

        var legal = item.LegalStatus switch { "illegal" => "Ilegal", "restricted" => "Restringido", _ => "Legal" };
        var facts = new Label
        {
            Text = $"{behavior.Name} · {legal}\n"
                   + $"Cantidad: {item.Quantity}\n"
                   + $"Peso: {Kg(item.UnitWeightKg)} kg c/u · {Kg(item.WeightKg)} kg en total\n"
                   + $"Volumen: {Kg(item.UnitVolumeL)} L c/u · {Kg(item.VolumeL)} L en total\n"
                   + $"Valor de referencia: {EconomyCatalog.FormatMoney(item.ReferencePriceCents)} c/u"
                   + (behavior.Consumable ? "\nSe gasta al usarlo." : ""),
        };
        facts.AddThemeColorOverride("font_color", Muted);
        _detail.AddChild(facts);

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
            _detail.AddChild(new Label { Text = "Objeto de misión: no se suelta ni se pasa." });
            return;
        }

        var qtyRow = new HBoxContainer();
        qtyRow.AddChild(new Label { Text = "Cantidad:" });
        var qty = new SpinBox { MinValue = 1, MaxValue = Math.Max(1, item.Quantity), Value = Math.Min(item.Quantity, 1), Rounded = true, CustomMinimumSize = new Vector2(90, 0) };
        qtyRow.AddChild(qty);
        var all = new Button { Text = "Todo" };
        all.Pressed += () => qty.Value = item.Quantity;
        qtyRow.AddChild(all);
        _detail.AddChild(qtyRow);

        AddButton("Soltar en la calle", () => _inventory.Drop(item.ItemCode, (int)qty.Value));

        if (_inventory.Nearby.Count == 0)
        {
            var hint = new Label { Text = "Para dárselo a alguien, acercate (a menos de 3 m) y volvé a abrir el inventario.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) };
            hint.AddThemeColorOverride("font_color", Muted);
            _detail.AddChild(hint);
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

    private void AddButton(string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        _detail.AddChild(button);
    }
}
