using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Godot;

namespace Distrito.Game.Crews;

/// <summary>
/// Tablero de la cuadrilla (tecla J): prestigio y rango con sus beneficios, la sede y las misiones del turno.
/// Las misiones de entrega, acopio y cobranza se cumplen con un botón estando en el lugar; las demás, haciendo lo que piden.
/// </summary>
public partial class CrewBoardPanel : PanelContainer
{
    private static readonly Color Gold = new(0.95f, 0.8f, 0.45f);
    private static readonly Color Muted = new(0.72f, 0.68f, 0.6f);
    private static readonly Color Good = new(0.6f, 0.85f, 0.55f);

    private BaseNetwork _bases = null!;
    private Func<Vector3?> _myPosition = () => null;
    private VBoxContainer _left = null!;
    private VBoxContainer _right = null!;
    private Label _message = null!;

    public bool IsOpen => Visible;

    public void Initialize(BaseNetwork bases, Func<Vector3?> myPosition)
    {
        _bases = bases;
        _myPosition = myPosition;
        _bases.BoardReceived += () => { if (Visible) Rebuild(); };
        _bases.MessageReceived += text => { if (Visible) _message.Text = text; };
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(720, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.07f, 0.95f), BorderColor = new Color(0.55f, 0.43f, 0.22f),
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
        var title = new Label { Text = "Tablero de la cuadrilla" };
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", Gold);
        box.AddChild(title);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 14);
        box.AddChild(columns);
        // Las dos columnas se desplazan: con todos los beneficios y la sede, la izquierda no entra en pantallas chicas.
        var leftScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(260, 380) };
        _left = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        _left.AddThemeConstantOverride("separation", 5);
        leftScroll.AddChild(_left);
        columns.AddChild(leftScroll);
        columns.AddChild(new VSeparator());
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(420, 380) };
        _right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _right.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_right);
        columns.AddChild(scroll);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(680, 0) };
        _message.AddThemeColorOverride("font_color", Good);
        box.AddChild(_message);
        var close = new Button { Text = "Cerrar (J)" };
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
            _bases.RequestBoard();
            Rebuild();
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private Label Text(string text, Color? color = null, int width = 240, int size = 0)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width, 0) };
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        if (size > 0) label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private void Rebuild()
    {
        foreach (var child in _left.GetChildren()) child.QueueFree();
        foreach (var child in _right.GetChildren()) child.QueueFree();
        var board = _bases.Board;
        if (board is null)
        {
            _left.AddChild(Text(Multiplayer.IsServer() ? "La cuadrilla necesita un servidor con cuentas." : "Cargando…", Muted));
            return;
        }
        if (!board.InCrew)
        {
            _left.AddChild(Text("No estás en ninguna cuadrilla. Formá una con C.", Muted));
            if (board.Families is { } outside)
                AddFamilies(outside);
            return;
        }

        // ---- Prestigio ----
        _left.AddChild(Text($"«{board.CrewName}»", Gold, size: 17));
        _left.AddChild(Text($"Prestigio {board.Prestige} · rango {board.Rank}" + (board.Rank < PrestigeRules.MaxRank ? $" (próximo a {board.NextRankAt})" : " (máximo)")));
        var bar = new ProgressBar
        {
            MinValue = PrestigeRules.ForRank(board.Rank), MaxValue = Math.Max(board.NextRankAt, PrestigeRules.ForRank(board.Rank) + 1),
            Value = board.Prestige, ShowPercentage = false, CustomMinimumSize = new Vector2(240, 10),
        };
        _left.AddChild(bar);
        foreach (var perk in PrestigeRules.Perks)
            _left.AddChild(Text($"{(board.Rank >= perk.Rank ? "✔" : "·")} Rango {perk.Rank}: {perk.Text}", board.Rank >= perk.Rank ? Good : Muted));

        // ---- Sede ----
        _left.AddChild(new HSeparator());
        if (board.Shed is { } shed)
        {
            _left.AddChild(Text($"Sede: galpón {shed + 1} de Los Muelles", Gold));
            _left.AddChild(Text($"Adentro es zona segura, ahí reaparecen y está el depósito ({CrewBaseRules.DepotKg + PrestigeRules.ExtraDepotKg(board.Rank):0} kg). "
                                + $"Alquiler {EconomyCatalog.FormatMoney(CrewBaseRules.RentCents)} por ciclo de la caja.", Muted));
            if (board.BaseDebtCents > 0)
                _left.AddChild(Text($"Se deben {EconomyCatalog.FormatMoney(board.BaseDebtCents)} de alquiler: pongan plata en la caja.", new Color(0.92f, 0.5f, 0.38f)));
        }
        else
        {
            _left.AddChild(Text("Sin sede todavía.", Gold));
            _left.AddChild(Text($"En Los Muelles hay galpones en venta: {EconomyCatalog.FormatMoney(CrewBaseRules.PriceCents)} de la caja y "
                                + $"{EconomyCatalog.FormatMoney(CrewBaseRules.RentCents)} por ciclo. El jefe la compra parado en la puerta.", Muted));
            var here = _myPosition() is { } p ? CrewBaseRules.ShedAt(p.X, p.Z, 3.5f) : null;
            if (board.IsLeader && here is { } at && !board.TakenSheds.Contains(at))
            {
                var buy = new Button { Text = $"Comprar el galpón {at + 1} como sede" };
                buy.Pressed += () => _bases.BuyBase(at);
                _left.AddChild(buy);
            }
            else if (here is { } taken && board.TakenSheds.Contains(taken))
                _left.AddChild(Text("Este galpón ya es sede de otra cuadrilla.", Muted));
        }

        if (board.Families is { } families)
            AddFamilies(families);

        // ---- Operación del día ----
        if (board.Operation is { } op)
            AddOperation(op);

        // ---- Misiones ----
        var minutes = Math.Max(0, (int)(board.NextRefresh - DateTimeOffset.UtcNow).TotalMinutes);
        _right.AddChild(Text($"Misiones (tablero nuevo en {minutes / 60} h {minutes % 60} min)", Gold, 400, 16));
        foreach (var m in board.Missions)
        {
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 2);
            row.AddChild(Text(m.Title, m.Done ? Muted : null, 400));
            row.AddChild(Text(m.Detail, Muted, 400));
            var progress = m.Kind == MissionKinds.Guard ? $"{m.Progress / 2.0:0.#}/{m.Target / 2.0:0.#} min" : $"{m.Progress}/{m.Target}";
            row.AddChild(Text(m.Done ? "Cumplida ✔" : $"Paga {EconomyCatalog.FormatMoney(m.RewardCents)} a la caja · +{m.Prestige} de prestigio"
                                       + (MissionKinds.IsTurnIn(m.Kind) ? "" : $" · progreso {progress}"), m.Done ? Good : Gold, 400));
            if (!m.Done && MissionKinds.IsTurnIn(m.Kind))
            {
                var label = m.Kind switch { MissionKinds.Delivery => "Entregar (en el lugar)", MissionKinds.Gather => "Entregar el acopio (en la sede)", _ => "Cobrar (en el lugar, con la cuadrilla)" };
                var turnIn = new Button { Text = label };
                var id = m.Id;
                turnIn.Pressed += () => _bases.TurnIn(id);
                row.AddChild(turnIn);
            }
            _right.AddChild(row);
            _right.AddChild(new HSeparator());
        }
    }

    /// <summary>La operación del día: quién la encarga, las tres etapas (hechas, la actual y las que faltan) y la paga.</summary>
    private void AddOperation(CrewOperationView op)
    {
        var accent = Distrito.Game.Economy.EncargoLook.FamilyColor(op.Family);
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Distrito.Game.Economy.EncargoLook.Panel(0.6f, accent));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 3);
        card.AddChild(box);
        var left = Math.Max(0, (int)(op.EndsAt - DateTimeOffset.UtcNow).TotalMinutes);
        box.AddChild(Text($"Operación del día: {op.Title}", accent.Lightened(0.3f), 390, 16));
        box.AddChild(Text(op.Intro, Muted, 390));
        box.AddChild(Text($"Paga al terminar: {EconomyCatalog.FormatMoney(op.RewardCents)} a la caja y +{op.Prestige} de prestigio · "
                          + $"quedan {left / 60} h {left % 60} min", Gold, 390));
        for (var i = 0; i < op.Stages.Count; i++)
        {
            var stage = op.Stages[i];
            var mark = stage.Done ? "✔" : stage.Current ? "▶" : "·";
            box.AddChild(Text($"{mark} {i + 1}. {stage.Title}", stage.Done ? Good : stage.Current ? null : Muted, 390));
            if (stage.Current)
                box.AddChild(Text(stage.Detail, Muted, 380));
        }
        switch (op.Status)
        {
            case CrewOperations.Available:
                var start = new Button { Text = $"Empezar la operación (hacen falta {op.MembersNeeded} de la cuadrilla)" };
                start.Pressed += _bases.StartOperation;
                box.AddChild(start);
                break;
            case CrewOperations.Done:
                box.AddChild(Text("Operación cumplida ✔ Mañana hay otra.", Good, 390));
                break;
            default:
                if (op.Current is { } m)
                {
                    var progress = m.Kind == MissionKinds.Guard ? $"{m.Progress / 2.0:0.#}/{m.Target / 2.0:0.#} min" : $"{m.Progress}/{m.Target}";
                    if (MissionKinds.IsTurnIn(m.Kind))
                    {
                        var label = m.Kind switch { MissionKinds.Gather => "Entregar el acopio (en la sede)", _ => "Cumplir (en el lugar, con la cuadrilla)" };
                        var turnIn = new Button { Text = label };
                        var id = m.Id;
                        turnIn.Pressed += () => _bases.TurnIn(id);
                        box.AddChild(turnIn);
                    }
                    else
                        box.AddChild(Text($"Progreso {progress}", Gold, 390));
                }
                break;
        }
        _right.AddChild(card);
        _right.AddChild(new HSeparator());
    }

    /// <summary>Familias de jugadores: cuántas hay de las 5 posibles, si se pueden fundar y qué le falta a tu cuadrilla.</summary>
    private void AddFamilies(PlayerFamiliesView f)
    {
        _left.AddChild(new HSeparator());
        _left.AddChild(Text($"Familias de jugadores: {f.Families.Count} de {f.Max}", Gold));
        foreach (var family in f.Families)
            _left.AddChild(Text($"«{family.Name}» — Don {family.DonName} · {string.Join(", ", family.Subfactions)}", Muted));
        if (f.MyFamily is { } mine)
        {
            _left.AddChild(Text($"Tu cuadrilla es una subfacción de «{mine}».", Good));
            return;
        }
        if (!f.FoundingEnabled)
        {
            _left.AddChild(Text("Todavía no se pueden fundar. Van a ser pocas (como mucho cinco): estar en una va a valer.", Muted));
            _left.AddChild(Text($"Para fundar una hará falta: prestigio de rango {PlayerFamilyRules.CrewRank}, el jefe nivel {PlayerFamilyRules.DonLevel} "
                                + $"y {EconomyCatalog.FormatMoney(PlayerFamilyRules.CostCents)} de la caja.", Muted));
            return;
        }
        if (f.Full)
            _left.AddChild(Text("Ya están las cinco. No entra ninguna más.", Muted));
        else if (f.Missing.Count > 0)
            _left.AddChild(Text($"Para fundar una falta: {string.Join(", ", f.Missing)}.", Muted));
        else
        {
            var name = new LineEdit { PlaceholderText = "Nombre de la familia", MaxLength = 30, CustomMinimumSize = new Vector2(240, 0) };
            _left.AddChild(name);
            var found = new Button { Text = $"Fundar la familia ({EconomyCatalog.FormatMoney(PlayerFamilyRules.CostCents)} de la caja)" };
            found.Pressed += () => _bases.FoundFamily(name.Text);
            _left.AddChild(found);
        }
    }
}
