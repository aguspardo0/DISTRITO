using Distrito.Game.Network;
using Distrito.Game.UI;
using Distrito.Server.Core.Npcs;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>
/// Cliente: detecta el NPC cercano, abre el diálogo con E y muestra dinero, carga y contrato en el HUD.
/// La distancia se vuelve a verificar en el servidor; esto es solo comodidad.
/// </summary>
public partial class NpcInteractionController : Node
{
    private GameNetwork _network = null!;
    private EconomyNetwork _economy = null!;
    private NpcDialogPanel _dialog = null!;
    private Label _prompt = null!;
    private Label _status = null!;

    public void Initialize(GameNetwork network, EconomyNetwork economy, CanvasLayer hud)
    {
        _network = network;
        _economy = economy;

        _prompt = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.GrowHorizontal = Control.GrowDirection.Both;
        _prompt.Position = new Vector2(-150, -60);
        _prompt.CustomMinimumSize = new Vector2(300, 0);
        _prompt.AddThemeColorOverride("font_shadow_color", Colors.Black);
        hud.AddChild(_prompt);

        _status = new Label();
        _status.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _status.GrowVertical = Control.GrowDirection.Begin;
        _status.Position = new Vector2(12, -12);
        _status.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _status.AddThemeFontSizeOverride("font_size", 16);
        hud.AddChild(_status);

        _dialog = new NpcDialogPanel { Name = "NpcDialog" };
        hud.AddChild(_dialog);

        _dialog.OptionChosen += (npc, option) => _economy.Choose(npc, option);
        _economy.DialogReceived += dialog => _dialog.ShowDialog(dialog);
        _economy.EconomyChanged += ShowEconomy;
    }

    public override void _Process(double delta)
    {
        var npc = NearestNpc();
        _prompt.Text = npc is not null && !_dialog.IsOpen ? $"[E] Hablar con {npc.Name}" : "";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.E } && !_dialog.IsOpen)
        {
            var npc = NearestNpc();
            if (npc is not null)
                _economy.Interact(npc.Code);
        }
    }

    private NpcDefinition? NearestNpc()
    {
        var local = _network.LocalPlayer;
        if (local is null)
            return null;
        var p = local.VisualPosition;
        var position = new System.Numerics.Vector3(p.X, p.Y, p.Z);
        return NpcInteractionRules.All
            .Where(n => NpcInteractionRules.InRange(position, n))
            .OrderBy(n => System.Numerics.Vector3.Distance(n.Position, position))
            .FirstOrDefault();
    }

    private void ShowEconomy(EconomySnapshot s)
    {
        var carried = s.Inventory.Count == 0 ? "nada" : string.Join(", ", s.Inventory.Select(i => $"{i.Name} ×{i.Quantity}"));
        var contract = s.ActiveContracts.Count == 0
            ? "ninguno"
            : string.Join(", ", s.ActiveContracts.Select(c =>
                EconomyCatalog.Templates.TryGetValue(c.TemplateCode, out var t) ? t.Title : c.TemplateCode));
        _status.Text = $"Dinero: {EconomyCatalog.FormatMoney(s.BalanceCents)}\nLlevás: {carried}\nContrato: {contract}\nContratos cumplidos: {s.CompletedContracts}";
    }
}
