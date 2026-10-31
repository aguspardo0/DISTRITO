using Distrito.Shared.Api;
using Distrito.Shared.Networking;
using Godot;

namespace Distrito.Game.UI;

/// <summary>
/// Menú inicial en tres partes:
///   1. Cuenta (opcional): servidor de cuentas, usuario, contraseña.
///   2. Personaje: elegir o crear (solo con sesión iniciada).
///   3. Partida: unirse a un servidor, o crear una partida local sin cuenta.
/// La lógica (HTTP, red) la hace Main; este control solo muestra y emite eventos.
/// </summary>
public partial class ConnectMenu : Control
{
    private LineEdit _backendUrl = null!;
    private LineEdit _username = null!;
    private LineEdit _password = null!;
    private VBoxContainer _characterSection = null!;
    private OptionButton _characterList = null!;
    private LineEdit _newCharacterName = null!;
    private LineEdit _host = null!;
    private SpinBox _port = null!;
    private Label _status = null!;
    private readonly List<Button> _buttons = [];
    private List<CharacterSummary> _characters = [];

    public string InitialBackendUrl { get; init; } = "";

    public event Action<string, string, string>? LoginRequested;      // url, usuario, contraseña
    public event Action<string, string, string>? RegisterRequested;
    public event Action<string>? CreateCharacterRequested;
    public event Action<int>? HostRequested;
    public event Action<string, int, CharacterSummary?>? JoinRequested; // personaje null = invitado

    public bool IsLoggedIn { get; private set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);

        var title = new Label { Text = "DISTRITO — prototipo", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(title);

        // 1. Cuenta
        box.AddChild(Header("Cuenta (opcional)"));
        _backendUrl = Field(box, "Servidor de cuentas (vacío = jugar sin cuenta)", InitialBackendUrl, "http://servidor:5080");
        _username = Field(box, "Usuario", "", "");
        _password = Field(box, "Contraseña", "", "");
        _password.Secret = true;
        var accountButtons = new HBoxContainer();
        accountButtons.AddChild(MakeButton("Ingresar", () => LoginRequested?.Invoke(_backendUrl.Text.Trim(), _username.Text.Trim(), _password.Text)));
        accountButtons.AddChild(MakeButton("Crear cuenta", () => RegisterRequested?.Invoke(_backendUrl.Text.Trim(), _username.Text.Trim(), _password.Text)));
        box.AddChild(accountButtons);

        // 2. Personaje
        _characterSection = new VBoxContainer { Visible = false };
        _characterSection.AddChild(Header("Personaje"));
        _characterList = new OptionButton();
        _characterSection.AddChild(_characterList);
        var createRow = new HBoxContainer();
        _newCharacterName = new LineEdit { PlaceholderText = "Nombre del personaje nuevo", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        createRow.AddChild(_newCharacterName);
        createRow.AddChild(MakeButton("Crear", () => CreateCharacterRequested?.Invoke(_newCharacterName.Text.Trim())));
        _characterSection.AddChild(createRow);
        box.AddChild(_characterSection);

        // 3. Partida
        box.AddChild(Header("Partida"));
        _host = Field(box, "Dirección del servidor de juego", NetworkDefaults.DefaultHost, "127.0.0.1");
        box.AddChild(new Label { Text = "Puerto" });
        _port = new SpinBox { MinValue = 1, MaxValue = 65535, Value = NetworkDefaults.DefaultPort, Rounded = true };
        box.AddChild(_port);
        box.AddChild(MakeButton("Unirse a partida", OnJoinPressed));
        box.AddChild(MakeButton("Crear partida local (sin cuenta)", () => HostRequested?.Invoke((int)_port.Value)));

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) };
        box.AddChild(_status);
    }

    private void OnJoinPressed()
    {
        CharacterSummary? character = null;
        if (IsLoggedIn)
        {
            var index = _characterList.Selected;
            if (index < 0 || index >= _characters.Count)
            {
                ShowStatus("Elegí o creá un personaje primero.");
                return;
            }
            character = _characters[index];
        }
        JoinRequested?.Invoke(_host.Text.Trim(), (int)_port.Value, character);
    }

    public void ShowLoggedIn(IReadOnlyList<CharacterSummary> characters, CharacterSummary? select = null)
    {
        IsLoggedIn = true;
        _characters = characters.ToList();
        _characterList.Clear();
        foreach (var c in _characters)
            _characterList.AddItem(c.Name);
        if (_characters.Count > 0)
            _characterList.Select(select is null ? 0 : Math.Max(0, _characters.FindIndex(c => c.Id == select.Id)));
        _characterSection.Visible = true;
        _newCharacterName.Text = "";
    }

    public void ShowStatus(string message) => _status.Text = message;

    public void SetBusy(bool busy)
    {
        foreach (var button in _buttons)
            button.Disabled = busy;
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += onPressed;
        _buttons.Add(button);
        return button;
    }

    private static Label Header(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.45f));
        return label;
    }

    private static LineEdit Field(Container parent, string label, string value, string placeholder)
    {
        parent.AddChild(new Label { Text = label });
        var edit = new LineEdit { Text = value, PlaceholderText = placeholder };
        parent.AddChild(edit);
        return edit;
    }
}
