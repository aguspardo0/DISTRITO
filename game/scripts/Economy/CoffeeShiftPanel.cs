using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Economy;

/// <summary>
/// La barra de la Esquina del café durante un turno: los pedidos que esperan, la taza (se llena manteniendo Espacio
/// bajo la canilla de la urna), la leche, la crema y los terrones, y la urna (cuántas tazas le quedan; con R se
/// recarga). Enter entrega. Mientras dura el turno el personaje no camina (el mouse queda libre).
/// </summary>
public partial class CoffeeShiftPanel : PanelContainer
{
    private CoffeeShiftNetwork _net = null!;
    private Label _title = null!, _orders = null!, _cup = null!, _urn = null!, _feedback = null!;
    private ProgressBar _gauge = null!;
    private readonly List<CoffeeOrder> _waiting = new();
    private int _selected;
    private double _fill, _endsAt, _clock;
    private int _sugar;
    private string _milk = "";
    private int _cups = CoffeeShiftRules.UrnCups;
    private bool _refilling;
    private AudioStreamPlayer? _pour;

    public bool IsOpen => Visible;

    public void Initialize(CoffeeShiftNetwork net)
    {
        _net = net;
        net.Started += seconds =>
        {
            _waiting.Clear();
            _selected = 0;
            ResetCup();
            _endsAt = _clock + seconds;
            _feedback.Text = "Llegan los primeros clientes…";
            Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        };
        net.OrderArrived += order => { _waiting.Add(order); Render(); };
        net.Served += (id, score, note) =>
        {
            _waiting.RemoveAll(o => o.Id == id);
            _selected = Math.Clamp(_selected, 0, Math.Max(0, _waiting.Count - 1));
            _feedback.Text = $"{note} ({score * 100:0} %)";
            Render();
        };
        net.OrderLost += id =>
        {
            var gone = _waiting.FirstOrDefault(o => o.Id == id);
            _waiting.RemoveAll(o => o.Id == id);
            _selected = Math.Clamp(_selected, 0, Math.Max(0, _waiting.Count - 1));
            _feedback.Text = $"{gone?.Customer ?? "Un cliente"} se cansó de esperar y se fue.";
            Render();
        };
        net.UrnChanged += (cups, refilling) => { _cups = cups; _refilling = refilling; Render(); };
        net.Finished += text =>
        {
            _feedback.Text = text;
            _title.Text = "Turno terminado";
            _waiting.Clear();
            Render();
            GetTree().CreateTimer(6).Timeout += Close;
        };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(560, 0);
        AddThemeStyleboxOverride("panel", EncargoLook.Panel(0.92f, EncargoLook.Gold));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        _title = EncargoLook.Text("Turno en la Esquina del café", EncargoLook.Gold, 540, 17);
        box.AddChild(_title);
        _orders = EncargoLook.Text("", null, 540, 14);
        box.AddChild(_orders);
        _gauge = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(540, 22) };
        box.AddChild(_gauge);
        _cup = EncargoLook.Text("", null, 540, 14);
        box.AddChild(_cup);
        _urn = EncargoLook.Text("", EncargoLook.Muted, 540, 13);
        box.AddChild(_urn);
        _feedback = EncargoLook.Text("", EncargoLook.Good, 540, 14);
        box.AddChild(_feedback);
        box.AddChild(EncargoLook.Text(
            "Mantené ESPACIO para servir de la urna y soltá en la línea · 1 leche · 2 crema · 3 un terrón · 0 tirar y empezar de nuevo · " +
            "Tab cambiar de pedido · ENTER entregar · R recargar la urna · Esc dejar el turno (no se cobra)", EncargoLook.Muted, 540, 12));
    }

    private void ResetCup()
    {
        _fill = 0;
        _sugar = 0;
        _milk = "";
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (!Visible)
            return;
        var pouring = Input.IsPhysicalKeyPressed(Key.Space) && !_refilling && _waiting.Count > 0;
        if (pouring)
            _fill = Math.Min(1.05, _fill + delta * 0.42); // unos 2 segundos hasta la línea
        // El chorro de la urna suena mientras se sirve.
        if (Distrito.Game.Audio.SoundPlayer.Enabled)
        {
            _pour ??= AddPourPlayer();
            if (pouring && !_pour.Playing) _pour.Play();
            else if (!pouring && _pour.Playing) _pour.Stop();
        }
        Render();
    }

    private void Render()
    {
        if (_title is null)
            return;
        var left = Math.Max(0, _endsAt - _clock);
        if (_title.Text != "Turno terminado")
            _title.Text = $"Turno en la Esquina del café · quedan {(int)left / 60}:{(int)left % 60:00}";
        _orders.Text = _waiting.Count == 0
            ? "No hay nadie esperando."
            : string.Join("\n", _waiting.Select((o, i) => $"{(i == _selected ? "▶" : "  ")} {o.Customer}: café {o.Text}"));
        _gauge.Value = Math.Min(1, _fill);
        var line = _fill >= CoffeeShiftRules.Spill ? "¡Se rebalsa!" : Math.Abs(_fill - CoffeeShiftRules.TargetFill) <= 0.05 ? "justo en la línea" : _fill < CoffeeShiftRules.TargetFill ? "falta" : "pasado";
        _cup.Text = $"Taza: {_fill * 100:0} % ({line}; la línea está en {CoffeeShiftRules.TargetFill * 100:0} %) · " +
                    $"{(_milk == "" ? "negro" : $"con {_milk}")} · {_sugar} terrón{(_sugar == 1 ? "" : "es")}";
        _urn.Text = _refilling ? "Urna: recargando… (esperá unos segundos)" : $"Urna: {_cups} de {CoffeeShiftRules.UrnCups} tazas{(_cups == 0 ? " — ¡vacía! El que sirvas sale aguado: recargá con R" : "")}";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.PhysicalKeycode)
        {
            case Key.Key1: _milk = _milk == CoffeeShiftRules.WithMilk ? "" : CoffeeShiftRules.WithMilk; break;
            case Key.Key2: _milk = _milk == CoffeeShiftRules.WithCream ? "" : CoffeeShiftRules.WithCream; break;
            case Key.Key3: _sugar = Math.Min(3, _sugar + 1); break;
            case Key.Key0 or Key.Backspace: ResetCup(); break;
            case Key.Tab when _waiting.Count > 0: _selected = (_selected + 1) % _waiting.Count; break;
            case Key.R when !_refilling: _net.Refill(); _feedback.Text = "Cargás café molido y agua en la urna…"; break;
            case Key.Enter or Key.KpEnter when _waiting.Count > 0 && _fill > 0:
                _net.Serve(new CoffeeServe(_waiting[_selected].Id, Math.Round(_fill, 3), _sugar, _milk));
                if (Distrito.Game.Audio.SoundPlayer.Enabled && GetViewport().GetCamera3D() is { } cam)
                    Distrito.Game.Audio.SoundPlayer.At(Distrito.Game.Audio.Sfx.Clink, cam.GlobalPosition, -6, Distrito.Game.Audio.SoundPlayer.Vary(), 10);
                ResetCup();
                break;
            case Key.Escape:
                if (_title.Text != "Turno terminado")
                    _net.Quit();
                Close();
                break;
            default:
                return;
        }
        Render();
        GetViewport().SetInputAsHandled();
    }

    private AudioStreamPlayer AddPourPlayer()
    {
        var p = new AudioStreamPlayer { Stream = Distrito.Game.Audio.Sfx.Pour, Bus = Distrito.Game.Audio.SoundPlayer.Bus, VolumeDb = -8 };
        AddChild(p);
        return p;
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        _pour?.Stop();
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
