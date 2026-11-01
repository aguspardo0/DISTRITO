using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Casino;

/// <summary>Mesas del casino El Trébol: ruleta, blackjack y dados, con la apuesta elegida y el resultado de cada jugada.</summary>
public partial class CasinoPanel : PanelContainer
{
    private static readonly Color Felt = new(0.07f, 0.2f, 0.12f, 0.96f);
    private static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    private static readonly Color Muted = new(0.75f, 0.8f, 0.72f);

    private CasinoNetwork _casino = null!;
    private Func<long> _balance = () => 0;
    private string _game = "ruleta";
    private SpinBox _stake = null!;
    private VBoxContainer _table = null!;
    private Label _result = null!;
    private Label _money = null!;
    private BlackjackHand? _hand;
    private long _maxBet = CasinoRules.MaxBetCents;

    public bool IsOpen => Visible;

    public void Initialize(CasinoNetwork casino, Func<long> balance)
    {
        _casino = casino;
        _balance = balance;
        _casino.ResultReceived += r =>
        {
            _result.Text = r.Message;
            _result.AddThemeColorOverride("font_color", r.PaidCents > 0 ? new Color(0.6f, 0.95f, 0.55f) : new Color(0.95f, 0.6f, 0.5f));
            _maxBet = r.MaxBetCents;
            if (_game == "blackjack")
                _hand = r.Hand;
            Rebuild();
        };
        _casino.MessageReceived += text => _result.Text = text;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(560, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Felt, BorderColor = Gold,
            BorderWidthTop = 3, BorderWidthBottom = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);

        var header = new HBoxContainer();
        var title = new Label { Text = "♣ El Trébol", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", Gold);
        header.AddChild(title);
        _money = new Label();
        _money.AddThemeColorOverride("font_color", Muted);
        header.AddChild(_money);
        box.AddChild(header);

        var games = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        foreach (var (code, name) in new[] { ("ruleta", "Ruleta"), ("blackjack", "Blackjack"), ("dados", "Dados") })
        {
            var b = new Button { Text = name, ToggleMode = true, CustomMinimumSize = new Vector2(140, 0) };
            b.Pressed += () => { _game = code; _result.Text = ""; Rebuild(); };
            games.AddChild(b);
        }
        box.AddChild(games);

        var stakeRow = new HBoxContainer();
        stakeRow.AddChild(new Label { Text = "Apuesta $" });
        _stake = new SpinBox { MinValue = CasinoRules.MinBetCents / 100, MaxValue = CasinoRules.VipMaxBetCents / 100, Value = 5, Rounded = true, CustomMinimumSize = new Vector2(100, 0) };
        stakeRow.AddChild(_stake);
        foreach (var quick in new[] { 1, 5, 20, 50 })
        {
            var q = new Button { Text = $"${quick}" };
            q.Pressed += () => _stake.Value = quick;
            stakeRow.AddChild(q);
        }
        box.AddChild(stakeRow);

        _table = new VBoxContainer();
        _table.AddThemeConstantOverride("separation", 6);
        box.AddChild(_table);

        _result = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 44), HorizontalAlignment = HorizontalAlignment.Center };
        _result.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(_result);

        var close = new Button { Text = "Salir del casino (Esc)" };
        close.Pressed += Close;
        box.AddChild(close);
    }

    public void Open()
    {
        Visible = true;
        _result.Text = "";
        Input.MouseMode = Input.MouseModeEnum.Visible;
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

    public override void _Process(double delta)
    {
        if (Visible)
            _money.Text = $"Tenés {EconomyCatalog.FormatMoney(_balance())} · máx. {EconomyCatalog.FormatMoney(_maxBet)}";
    }

    private long Stake => (long)_stake.Value * 100;

    private Label Note(string text)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) };
        l.AddThemeColorOverride("font_color", Muted);
        return l;
    }

    private void Rebuild()
    {
        foreach (var child in _table.GetChildren()) child.QueueFree();
        switch (_game)
        {
            case "ruleta":
            {
                _table.AddChild(Note("Ruleta europea (0 al 36). Color, par/impar o mitad pagan 1 a 1; un número, 35 a 1. Con el 0 pierden las apuestas sencillas."));
                var row = new HFlowContainer();
                foreach (var (bet, label) in new[] { ("rojo", "Rojo"), ("negro", "Negro"), ("par", "Par"), ("impar", "Impar"), ("bajo", "1–18"), ("alto", "19–36") })
                {
                    var b = new Button { Text = label, CustomMinimumSize = new Vector2(80, 0) };
                    if (bet == "rojo") b.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.35f));
                    b.Pressed += () => _casino.Roulette(Stake, bet, 0);
                    row.AddChild(b);
                }
                _table.AddChild(row);
                var single = new HBoxContainer();
                single.AddChild(new Label { Text = "Al número" });
                var number = new SpinBox { MinValue = 0, MaxValue = 36, Value = 17, Rounded = true };
                single.AddChild(number);
                var go = new Button { Text = "Apostar al número" };
                go.Pressed += () => _casino.Roulette(Stake, "numero", (int)number.Value);
                single.AddChild(go);
                _table.AddChild(single);
                break;
            }
            case "dados":
            {
                _table.AddChild(Note("Línea de pase: 7 u 11 en la primera tirada gana; 2, 3 o 12 pierde. Cualquier otro número es el punto: se tira hasta repetirlo (gana) o sacar 7 (pierde). Paga 1 a 1."));
                var roll = new Button { Text = "Tirar los dados" };
                roll.Pressed += () => _casino.Dice(Stake);
                _table.AddChild(roll);
                break;
            }
            default:
            {
                _table.AddChild(Note("El crupier se planta en 17. Blackjack paga 3 a 2. Empate: recuperás la apuesta."));
                if (_hand is { } h)
                {
                    string Cards(List<int> cards, bool hideSecond) =>
                        string.Join("  ", cards.Select((c, i) => hideSecond && i == 1 ? "🂠" : CasinoRules.CardName(c)));
                    var dealer = new Label { Text = $"Crupier: {Cards(h.Dealer, h.DealerHidden)}" + (h.DealerHidden ? "" : $"  ({CasinoRules.HandValue(h.Dealer)})") };
                    dealer.AddThemeFontSizeOverride("font_size", 18);
                    _table.AddChild(dealer);
                    var mine = new Label { Text = $"Vos: {Cards(h.Player, false)}  ({CasinoRules.HandValue(h.Player)})" };
                    mine.AddThemeFontSizeOverride("font_size", 18);
                    mine.AddThemeColorOverride("font_color", Gold);
                    _table.AddChild(mine);
                }
                var row = new HBoxContainer();
                if (_hand is { Status: "jugando" })
                {
                    var hit = new Button { Text = "Pedir carta", CustomMinimumSize = new Vector2(140, 0) };
                    hit.Pressed += () => _casino.Blackjack("pedir", 0);
                    row.AddChild(hit);
                    var stand = new Button { Text = "Plantarse", CustomMinimumSize = new Vector2(140, 0) };
                    stand.Pressed += () => _casino.Blackjack("plantarse", 0);
                    row.AddChild(stand);
                }
                else
                {
                    var deal = new Button { Text = _hand is null ? "Repartir" : "Otra mano", CustomMinimumSize = new Vector2(140, 0) };
                    deal.Pressed += () => _casino.Blackjack("repartir", Stake);
                    row.AddChild(deal);
                }
                _table.AddChild(row);
                break;
            }
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

    /// <summary>Solo para la prueba visual.</summary>
    public void Preview()
    {
        _game = "blackjack";
        _hand = new BlackjackHand(Guid.NewGuid(), 500, [10, 7], [1, 9], "jugando", true);
        _result.Text = "Repartido. ¿Pedís o te plantás?";
        Rebuild();
    }
}
