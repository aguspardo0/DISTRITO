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
    private Label _banner = null!;
    private double _bannerRefresh;

    public NpcDialogPanel Dialog => _dialog;

    public void Initialize(GameNetwork network, EconomyNetwork economy, CanvasLayer hud)
    {
        _network = network;
        _economy = economy;

        _prompt = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _prompt.CustomMinimumSize = new Vector2(300, 0);
        _prompt.AddThemeColorOverride("font_shadow_color", Colors.Black);
        hud.AddChild(UiAnchor.Wrap(_prompt, UiAnchor.Center, UiAnchor.End, bottom: 60, name: "PromptArea"));

        _status = new Label();
        _status.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _status.AddThemeFontSizeOverride("font_size", 16);
        hud.AddChild(UiAnchor.Wrap(_status, UiAnchor.Start, UiAnchor.End, name: "StatusArea"));

        // Anuncio de eventos de distrito (arriba, al centro).
        _banner = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _banner.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.45f));
        _banner.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _banner.AddThemeFontSizeOverride("font_size", 20);
        hud.AddChild(UiAnchor.Wrap(_banner, UiAnchor.Center, UiAnchor.Start, top: 80, name: "EventBanner"));

        _dialog = new NpcDialogPanel { Name = "NpcDialog" };
        hud.AddChild(UiAnchor.Wrap(_dialog, UiAnchor.Center, UiAnchor.End, bottom: 30, name: "DialogArea"));

        _dialog.OptionChosen += (npc, option) =>
        {
            if (option == NpcInteractionRules.SupplierOrdersOption)
            {
                _dialog.Close();
                SupplierRequested?.Invoke(npc);
            }
            else if (option == NpcInteractionRules.VehicleShopOption)
            {
                _dialog.Close();
                VehicleShopRequested?.Invoke();
            }
            else
                _economy.Choose(npc, option);
        };
        _economy.DialogReceived += dialog => _dialog.ShowDialog(dialog);
        _economy.EconomyChanged += ShowEconomy;
    }

    /// <summary>El jugador eligió «Pedidos por mayor» en un proveedor: se abre la ventana de pedidos.</summary>
    public event Action<string>? SupplierRequested;

    /// <summary>El jugador eligió «Vehículos» en el Taller Caruso.</summary>
    public event Action? VehicleShopRequested;

    private double _timerRefresh;

    public override void _Process(double delta)
    {
        // Contratos con plazo: la cuenta regresiva se actualiza una vez por segundo.
        _timerRefresh += delta;
        if (_timerRefresh >= 1 && _economy.LastSnapshot is { } snapshot && snapshot.ActiveContracts.Any(c => c.ExpiresAt is not null))
        {
            _timerRefresh = 0;
            ShowEconomy(snapshot);
        }

        _bannerRefresh += delta;
        if (_bannerRefresh >= 1)
        {
            _bannerRefresh = 0;
            _banner.Text = _economy.LastSnapshot?.Events is { } events
                ? Distrito.Shared.Economy.DistrictEvents.Banner(events, DateTimeOffset.UtcNow) ?? ""
                : "";
        }

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

    private static string FormatLeft(int seconds) => seconds <= 0 ? "venció" : $"quedan {seconds / 60}:{seconds % 60:00}";

    private void ShowEconomy(EconomySnapshot s)
    {
        var carried = s.Inventory.Count == 0 ? "nada" : string.Join(", ", s.Inventory.Select(i => $"{i.Name} ×{i.Quantity}"));
        var contract = s.ActiveContracts.Count == 0
            ? "ninguno"
            : string.Join(", ", s.ActiveContracts.Select(c =>
                (EconomyCatalog.Templates.TryGetValue(c.TemplateCode, out var t) ? t.Title : c.TemplateCode) +
                (c.ExpiresAt is null ? "" : $" ({FormatLeft(c.SecondsLeft(DateTimeOffset.UtcNow))})")));
        var reputation = s.Reputation is { Count: > 0 }
            ? string.Join(" · ", s.Reputation.Select(r => $"{r.FamilyName.Replace("Familia ", "")} {r.Value}"))
            : "—";
        var pr = s.ProgressOrEmpty;
        var level = $"Nivel {pr.Level}" + (pr.Level < Distrito.Shared.Progression.ProgressionRules.MaxLevel ? $" ({pr.Xp}/{pr.XpForNext} exp)" : "") +
                    $" · Logística {pr.LogisticsLevel} · Negociación {pr.NegotiationLevel} · Administración {pr.AdministrationLevel}";
        _status.Text = $"{level}\nDinero: {EconomyCatalog.FormatMoney(s.BalanceCents)}\nLlevás: {carried}\nContrato: {contract}\n" +
                       $"Contratos cumplidos: {s.CompletedContracts}\nReputación: {reputation}" +
                       string.Concat((s.Businesses ?? (s.Business is { } only ? new List<BusinessView> { only } : new List<BusinessView>())).Select(b =>
                           $"\n{b.Type.Name} ({b.OperatingState}): stock {b.Stock} · caja {EconomyCatalog.FormatMoney(b.CashCents)} · ganancia {EconomyCatalog.FormatMoney(b.ProfitCents)}"));
    }
}
