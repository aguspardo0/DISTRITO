using Distrito.Server.Core.Npcs;
using Godot;

namespace Distrito.Game.UI;

/// <summary>Ventana de diálogo con un NPC: texto y opciones que mandó el servidor.</summary>
public partial class NpcDialogPanel : PanelContainer
{
    private Label _title = null!;
    private Label _text = null!;
    private VBoxContainer _options = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _body = null!;
    private string _npcCode = "";

    public event Action<string, string>? OptionChosen; // npc, opción
    public event Action? Closed;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(520, 0);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 19);
        _title.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.45f));
        box.AddChild(_title);

        // Texto y opciones dentro de un panel desplazable: con muchas opciones (contratos, acuerdos, pedidos…)
        // la ventana no se sale de la pantalla y todo queda a mano.
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(_scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_body);

        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0) };
        _body.AddChild(_text);

        _options = new VBoxContainer();
        _options.AddThemeConstantOverride("separation", 4);
        _body.AddChild(_options);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        // Alto: lo que haga falta, hasta lo que entra en la pantalla (dejando lugar al título y al borde).
        var height = Mathf.Min(_body.GetCombinedMinimumSize().Y, GetViewportRect().Size.Y - 160);
        if (!Mathf.IsEqualApprox(_scroll.CustomMinimumSize.Y, height))
            _scroll.CustomMinimumSize = new Vector2(490, height);
    }

    public void ShowDialog(NpcDialog dialog)
    {
        _npcCode = dialog.NpcCode;
        _title.Text = dialog.Title;
        _text.Text = dialog.Text;

        foreach (var child in _options.GetChildren())
            child.QueueFree();
        foreach (var option in dialog.Options)
        {
            // Las opciones largas se parten en renglones en vez de ensanchar la ventana.
            var button = new Button
            {
                Text = option.Label, Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(470, 0),
            };
            var id = option.Id;
            button.Pressed += () => OptionChosen?.Invoke(_npcCode, id);
            _options.AddChild(button);
        }
        var close = new Button { Text = "Chau (Esc)", Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(470, 0) };
        close.Pressed += Close;
        _options.AddChild(close);

        Visible = true;
        _scroll.ScrollVertical = 0;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        Closed?.Invoke();
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
