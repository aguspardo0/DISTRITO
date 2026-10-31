using Distrito.Shared.Crews;
using Godot;

namespace Distrito.Game.Crews;

/// <summary>Panel de cuadrilla (tecla C): fundar, invitar, responder invitaciones y salir.</summary>
public partial class CrewPanel : PanelContainer
{
    private CrewNetwork _crews = null!;
    private VBoxContainer _content = null!;
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
        SetAnchorsPreset(LayoutPreset.CenterRight);
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.Both;
        Position = new Vector2(-20, 0);
        CustomMinimumSize = new Vector2(340, 0);

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

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);
        box.AddChild(_content);

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

    private void AddButton(string text, Action onPressed) => _content.AddChild(MakeButton(text, onPressed));

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += onPressed;
        return button;
    }
}
