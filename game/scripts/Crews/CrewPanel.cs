using Distrito.Shared.Crews;
using Godot;

namespace Distrito.Game.Crews;

/// <summary>Panel de cuadrilla (tecla C): fundar, invitar, responder invitaciones y salir.</summary>
public partial class CrewPanel : PanelContainer
{
    private CrewNetwork _crews = null!;
    private VBoxContainer _content = null!;
    private ScrollContainer _scroll = null!;

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        var height = Mathf.Min(_content.GetCombinedMinimumSize().Y, GetViewportRect().Size.Y - 220);
        if (!Mathf.IsEqualApprox(_scroll.CustomMinimumSize.Y, height))
            _scroll.CustomMinimumSize = new Vector2(380, height);
    }
    private Label _message = null!;

    public bool IsOpen => Visible;

    public void Initialize(CrewNetwork crews)
    {
        _crews = crews;
        _crews.StatusReceived += (_, _) => { if (Visible) Rebuild(); };
        _crews.MessageReceived += text => _message.Text = text;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(410, 0);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);

        var title = new Label { Text = "Cuadrilla" };
        title.AddThemeFontSizeOverride("font_size", 19);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.45f));
        box.AddChild(title);

        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 4);
        // Con mucho contenido (votación, coalición, permisos) el panel se desplaza en vez de salirse de la pantalla.
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.AddChild(_content);
        box.AddChild(_scroll);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(310, 0) };
        _message.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 0.75f));
        box.AddChild(_message);

        var close = new Button { Text = "Cerrar (C)" };
        close.Pressed += Toggle;
        box.AddChild(close);
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _crews.RequestPanel();
            Rebuild();
        }
        else if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren())
            child.QueueFree();

        var status = _crews.LastStatus;
        if (status is null)
        {
            _content.AddChild(new Label { Text = "Cargando…" });
            return;
        }

        if (!status.InCrew)
        {
            _content.AddChild(new Label { Text = "No estás en ninguna cuadrilla." });
            var name = new LineEdit { PlaceholderText = "Nombre de la cuadrilla nueva" };
            _content.AddChild(name);
            AddButton("Fundar cuadrilla", () => _crews.Create(name.Text));

            if (status.Invitations.Count > 0)
                _content.AddChild(new Label { Text = "Invitaciones:" });
            foreach (var invitation in status.Invitations)
            {
                _content.AddChild(new Label { Text = $"«{invitation.CrewName}» (te invitó {invitation.InvitedBy})" });
                var row = new HBoxContainer();
                row.AddChild(MakeButton("Aceptar", () => _crews.Respond(invitation.CrewId, true)));
                row.AddChild(MakeButton("Rechazar", () => _crews.Respond(invitation.CrewId, false)));
                _content.AddChild(row);
            }
            return;
        }

        _content.AddChild(new Label { Text = $"«{status.CrewName}» — nivel {status.Level} — {status.Members.Count}/{status.Capacity}" });
        _content.AddChild(new Label
        {
            Text = status.IsFormed
                ? "Cuadrilla formada."
                : $"En formación: faltan {CrewRules.MembersToForm - status.Members.Count} para estar formada.",
        });
        foreach (var member in status.Members)
        {
            var mark = member.Role switch { CrewRoles.Leader => "★", CrewRoles.Second => "☆", _ => "•" };
            _content.AddChild(new Label { Text = $"  {mark} {member.Name} — {CrewRoles.Display(member.Role)}" });

            // Herramientas del jefe sobre los demás integrantes.
            if (status.IsLeader && member.Role != CrewRoles.Leader)
            {
                var row = new HBoxContainer();
                var id = member.CharacterId;
                row.AddChild(member.Role == CrewRoles.Second
                    ? MakeButton("Quitar segundo", () => _crews.SetRole(id, CrewRoles.Member))
                    : MakeButton("Nombrar segundo", () => _crews.SetRole(id, CrewRoles.Second)));
                row.AddChild(MakeButton("Expulsar", () => _crews.Kick(id)));
                _content.AddChild(row);
            }
        }

        // Guerras.
        foreach (var war in status.Wars ?? [])
        {
            var when = war.IsActive(DateTimeOffset.UtcNow)
                ? $"EN GUERRA hasta {war.EndsAt.ToLocalTime():dd/MM HH:mm}: se pelea en todo el barrio salvo cerca de los locales"
                : $"empieza {war.StartsAt.ToLocalTime():HH:mm}";
            _content.AddChild(new Label { Text = $"Guerra con «{war.OtherCrew}» ({(war.DeclaredByUs ? "la declaramos" : "nos la declararon")}): {when}.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(310, 0) });
        }
        if (status.IsLeader)
        {
            var row = new HBoxContainer();
            var target = new LineEdit { PlaceholderText = "Cuadrilla enemiga", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(target);
            row.AddChild(MakeButton("Declarar guerra", () => _crews.DeclareWar(target.Text.Trim())));
            _content.AddChild(row);
        }

        // Votación.
        if (status.Vote is { } vote)
        {
            if (vote.Status == "open")
            {
                var mine = vote.MyVote is null ? "todavía no votaste" : vote.MyVote.Value ? "votaste que sí" : "votaste que no";
                _content.AddChild(new Label { Text = $"Votación: {vote.Describe()} — sí {vote.Yes}, no {vote.No} de {vote.Members} ({mine}). Cierra {vote.ClosesAt.ToLocalTime():dd/MM HH:mm}." , AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(310, 0) });
                var row = new HBoxContainer();
                var id = vote.Id;
                row.AddChild(MakeButton("Votar sí", () => _crews.CastVote(id, true)));
                row.AddChild(MakeButton("Votar no", () => _crews.CastVote(id, false)));
                _content.AddChild(row);
            }
            else
            {
                _content.AddChild(new Label { Text = $"Última votación: {vote.Describe()} — {(vote.Status == "passed" ? "aprobada" : "no aprobada")}.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(310, 0) });
            }
        }
        if (status.Vote is not { Status: "open" } && status.Members.Count > 1)
        {
            _content.AddChild(new Label { Text = "Abrir votación (gana con más de la mitad):" });
            foreach (var member in status.Members)
            {
                var row = new HBoxContainer();
                var id = member.CharacterId;
                row.AddChild(new Label { Text = member.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
                if (member.Role != CrewRoles.Leader)
                    row.AddChild(MakeButton("Jefe", () => _crews.OpenVote(CrewVotes.Leader, id, 0)));
                if (status.TreasuryCents >= 500)
                    row.AddChild(MakeButton("Darle $5", () => _crews.OpenVote(CrewVotes.Payout, id, 500)));
                if (row.GetChildCount() > 1)
                    _content.AddChild(row);
            }
        }

        // Caja común.
        _content.AddChild(new Label { Text = $"Caja: {Distrito.Shared.Economy.EconomyCatalog.FormatMoney(status.TreasuryCents)}" });
        var put = new HBoxContainer();
        put.AddChild(new Label { Text = "Poner:" });
        foreach (var (label, cents) in MoneySteps)
            put.AddChild(MakeButton(label, () => _crews.Deposit(cents)));
        _content.AddChild(put);
        if (status.CanWithdraw)
        {
            var take = new HBoxContainer();
            take.AddChild(new Label { Text = "Sacar:" });
            foreach (var (label, cents) in MoneySteps)
                take.AddChild(MakeButton(label, () => _crews.Withdraw(cents)));
            _content.AddChild(take);
        }

        var rules = status.Rules;
        if (status.IsLeader)
        {
            _content.AddChild(new Label { Text = "Permisos (solo el jefe):" });
            AddToggle("El segundo puede sacar de la caja", rules.SecondCanWithdraw, on => _crews.SetSettings(rules with { SecondCanWithdraw = on }));
            AddToggle("El segundo puede invitar", rules.SecondCanInvite, on => _crews.SetSettings(rules with { SecondCanInvite = on }));
            AddToggle("Los miembros pueden invitar", rules.MemberCanInvite, on => _crews.SetSettings(rules with { MemberCanInvite = on }));
            var tithe = new HBoxContainer();
            tithe.AddChild(new Label { Text = "Aporte de contratos a la caja:" });
            var picker = new OptionButton();
            for (var pct = 0; pct <= CrewPermissions.MaxTithePercent; pct += 5)
                picker.AddItem($"{pct} %", pct);
            picker.Select(picker.GetItemIndex(rules.TithePercent - rules.TithePercent % 5));
            picker.ItemSelected += index => _crews.SetSettings(rules with { TithePercent = picker.GetItemId((int)index) });
            tithe.AddChild(picker);
            _content.AddChild(tithe);
        }
        else if (rules.TithePercent > 0)
        {
            _content.AddChild(new Label { Text = $"Aporte de contratos a la caja: {rules.TithePercent} %" });
        }

        // Coalición.
        if (status.Coalition is { Name: { } coalitionName } coalition)
        {
            _content.AddChild(new Label { Text = $"Coalición «{coalitionName}»: {string.Join(", ", coalition.Crews)}" });
            if (status.IsLeader)
                AddButton("Salir de la coalición", () => _crews.LeaveCoalition());
        }
        else if (status.IsLeader)
        {
            _content.AddChild(new Label { Text = status.Coalition?.CooldownUntil is { } until
                ? $"Coalición: podés entrar a otra desde {until.ToLocalTime():dd/MM HH:mm}."
                : "Coalición: invitá a otra cuadrilla (hasta 3)." });
            var row = new HBoxContainer();
            var target = new LineEdit { PlaceholderText = "Cuadrilla", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var name = new LineEdit { PlaceholderText = "Nombre de la coalición", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(target);
            row.AddChild(name);
            row.AddChild(MakeButton("Invitar", () => _crews.InviteCoalition(target.Text.Trim(), name.Text.Trim())));
            _content.AddChild(row);
        }
        if (status.IsLeader && status.Coalition?.Name is { } && status.Coalition.Crews.Count < CoalitionRules.MaxCrews)
        {
            var row = new HBoxContainer();
            var target = new LineEdit { PlaceholderText = "Invitar otra cuadrilla", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(target);
            row.AddChild(MakeButton("Invitar", () => _crews.InviteCoalition(target.Text.Trim(), "")));
            _content.AddChild(row);
        }
        foreach (var invite in status.CoalitionInvites ?? [])
        {
            _content.AddChild(new Label { Text = $"«{invite.FromCrewName}» los invita a la coalición «{invite.CoalitionName}»" });
            if (status.IsLeader)
            {
                var row = new HBoxContainer();
                var from = invite.FromCrewId;
                row.AddChild(MakeButton("Aceptar", () => _crews.RespondCoalition(from, true)));
                row.AddChild(MakeButton("Rechazar", () => _crews.RespondCoalition(from, false)));
                _content.AddChild(row);
            }
        }

        // Ampliación del cupo: por contratos cumplidos y reputación de todos los integrantes.
        if (status.NextExpansion is { } next)
        {
            _content.AddChild(new Label
            {
                Text = $"Para ampliar a {next.Capacity}: contratos {status.Contracts}/{next.Contracts} · reputación {status.Reputation}/{next.Reputation}",
            });
            if (status.IsLeader && status.CanExpand)
                AddButton($"Ampliar la cuadrilla a {next.Capacity}", () => _crews.Expand());
        }

        if (status.CanInvite)
        {
            _content.AddChild(new Label { Text = "Invitar a alguien conectado:" });
            if (_crews.LastCandidates.Count == 0)
                _content.AddChild(new Label { Text = "  (no hay nadie más conectado con cuenta)" });
            foreach (var candidate in _crews.LastCandidates)
                AddButton($"Invitar a {candidate.Name}", () => _crews.Invite(candidate.Peer));
        }

        AddButton("Salir de la cuadrilla", () => _crews.Leave());

        if (status.History is { Count: > 0 } history)
        {
            _content.AddChild(new Label { Text = "Historial:" });
            foreach (var e in history.Take(5))
                _content.AddChild(new Label { Text = $"  {e.When.ToLocalTime():dd/MM HH:mm} · {CrewRules.Describe(e)}" });
        }
    }

    private static readonly (string Label, long Cents)[] MoneySteps = [("$1", 100), ("$5", 500), ("$10", 1_000), ("Todo", 0)];

    private void AddToggle(string text, bool value, Action<bool> onToggled)
    {
        var toggle = new CheckButton { Text = text, ButtonPressed = value };
        toggle.Toggled += on => onToggled(on);
        _content.AddChild(toggle);
    }

    private void AddButton(string text, Action onPressed) => _content.AddChild(MakeButton(text, onPressed));

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += onPressed;
        return button;
    }
}
