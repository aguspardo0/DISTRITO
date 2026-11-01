using Distrito.Game.Economy;
using Distrito.Shared.Progression;
using Godot;

namespace Distrito.Game.Player;

/// <summary>
/// Árbol de habilidades (tecla K): tres ramas con escalones (nivel 1, 5 y 10). Se elige un nodo para ver qué hace, qué
/// pide y cuántos rangos tiene, y se aprende con un punto. Los puntos salen del nivel (uno por nivel desde el 2).
/// </summary>
public partial class SkillTreePanel : PanelContainer
{
    private static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    private static readonly Color Muted = new(0.68f, 0.64f, 0.56f);
    private static readonly Color Learned = new(0.55f, 0.85f, 0.5f);

    private SkillNetwork _skills = null!;
    private EconomyNetwork _economy = null!;
    private HBoxContainer _branches = null!;
    private VBoxContainer _detail = null!;
    private Label _points = null!;
    private Label _message = null!;
    private string? _selected;

    public bool IsOpen => Visible;

    public void Initialize(SkillNetwork skills, EconomyNetwork economy)
    {
        _skills = skills;
        _economy = economy;
        _skills.MessageReceived += text => { if (Visible) _message.Text = text; };
        _economy.EconomyChanged += _ => { if (Visible) Rebuild(); };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(820, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.075f, 0.07f, 0.96f), BorderColor = new Color(0.55f, 0.43f, 0.22f),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);

        var header = new HBoxContainer();
        var title = new Label { Text = "Habilidades", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", Gold);
        header.AddChild(title);
        _points = new Label();
        _points.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(_points);
        box.AddChild(header);

        _branches = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _branches.AddThemeConstantOverride("separation", 16);
        box.AddChild(_branches);

        box.AddChild(new HSeparator());
        _detail = new VBoxContainer { CustomMinimumSize = new Vector2(780, 96) };
        _detail.AddThemeConstantOverride("separation", 4);
        box.AddChild(_detail);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(780, 0) };
        _message.AddThemeColorOverride("font_color", Learned);
        box.AddChild(_message);
        var close = new Button { Text = "Cerrar (K)" };
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
            Rebuild();
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>Para la prueba visual: un personaje de ejemplo.</summary>
    public Func<(int Level, Dictionary<string, int> Ranks)>? PreviewData { get; set; }

    private (int Level, IReadOnlyDictionary<string, int> Ranks) State()
    {
        if (PreviewData is { } preview)
            return preview();
        var s = _economy.LastSnapshot;
        return (s?.ProgressOrEmpty.Level ?? 1, s?.Skills ?? new Dictionary<string, int>());
    }

    private void Rebuild()
    {
        foreach (var child in _branches.GetChildren()) child.QueueFree();
        foreach (var child in _detail.GetChildren()) child.QueueFree();
        var (level, ranks) = State();
        var free = SkillTree.PointsFor(level) - SkillTree.Spent(ranks);
        _points.Text = $"Nivel {level} · {free} punto{(free == 1 ? "" : "s")} libre{(free == 1 ? "" : "s")}";
        _points.AddThemeColorOverride("font_color", free > 0 ? Learned : Muted);
        _selected ??= SkillTree.Nodes[0].Code;

        foreach (var (code, name, blurb) in SkillTree.Branches)
        {
            var column = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
            column.AddThemeConstantOverride("separation", 6);
            var head = new Label { Text = name.ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center };
            head.AddThemeColorOverride("font_color", Gold);
            head.AddThemeFontSizeOverride("font_size", 16);
            column.AddChild(head);
            var sub = new Label { Text = blurb, HorizontalAlignment = HorizontalAlignment.Center };
            sub.AddThemeColorOverride("font_color", Muted);
            column.AddChild(sub);
            foreach (var tier in SkillTree.Nodes.Where(n => n.Branch == code).GroupBy(n => n.Tier).OrderBy(g => g.Key))
            {
                var tierLabel = new Label { Text = $"— nivel {SkillTree.TierLevel(tier.Key)} —", HorizontalAlignment = HorizontalAlignment.Center };
                tierLabel.AddThemeColorOverride("font_color", level >= SkillTree.TierLevel(tier.Key) ? Muted : new Color(0.5f, 0.4f, 0.35f));
                tierLabel.AddThemeFontSizeOverride("font_size", 12);
                column.AddChild(tierLabel);
                var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
                foreach (var node in tier)
                {
                    var rank = ranks.GetValueOrDefault(node.Code);
                    var available = SkillTree.WhyNot(node.Code, level, ranks) is null;
                    var button = new Button
                    {
                        Text = $"{node.Name}\n{rank}/{node.MaxRank}", ToggleMode = true, ButtonPressed = node.Code == _selected,
                        CustomMinimumSize = new Vector2(120, 52),
                    };
                    button.AddThemeColorOverride("font_color", rank >= node.MaxRank ? Learned : available ? Gold : rank > 0 ? Learned : Muted);
                    var c = node.Code;
                    button.Pressed += () => { _selected = c; Rebuild(); };
                    row.AddChild(button);
                }
                column.AddChild(row);
            }
            _branches.AddChild(column);
        }

        if (SkillTree.Find(_selected!) is { } selected)
        {
            var rank = ranks.GetValueOrDefault(selected.Code);
            var name = new Label { Text = $"{selected.Name} — rango {rank}/{selected.MaxRank}" };
            name.AddThemeFontSizeOverride("font_size", 17);
            name.AddThemeColorOverride("font_color", Gold);
            _detail.AddChild(name);
            _detail.AddChild(new Label { Text = selected.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(760, 0) });
            var req = $"Pide nivel {SkillTree.TierLevel(selected.Tier)}" + (selected.Requires is { } r ? $" y {SkillTree.Find(r)!.Name}" : "") + ".";
            var reqLabel = new Label { Text = req };
            reqLabel.AddThemeColorOverride("font_color", Muted);
            _detail.AddChild(reqLabel);
            var why = SkillTree.WhyNot(selected.Code, level, ranks);
            var learn = new Button { Text = why is null ? "Aprender (1 punto)" : why, Disabled = why is not null };
            var code = selected.Code;
            learn.Pressed += () => _skills.Learn(code);
            _detail.AddChild(learn);
        }
    }
}
