using Distrito.Game.Crews;
using Distrito.Game.Network;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Bot de prueba de cuadrillas. Modo "fundador": funda la cuadrilla e invita al primer candidato que aparezca.
/// Modo "recluta": espera una invitación y la acepta. Termina cuando la cuadrilla tiene 2 miembros.
/// </summary>
public partial class CrewBot : Node
{
    private const double PollSeconds = 1.0;

    private GameNetwork _network = null!;
    private CrewNetwork _crews = null!;
    private string? _createName;
    private double _untilPoll = PollSeconds;
    private bool _created;
    private readonly HashSet<long> _invited = new();
    private bool _responded;

    public bool Completed { get; private set; }
    public string State { get; private set; } = "esperando";

    public void Initialize(GameNetwork network, CrewNetwork crews, string? createName)
    {
        _network = network;
        _crews = crews;
        _createName = createName;
        _crews.StatusReceived += (status, candidates) => Decide();
        _crews.MessageReceived += text => GD.Print($"[DISTRITO] [bot-cuadrilla] mensaje: {text}");
    }

    public override void _Process(double delta)
    {
        if (Completed || _network.LocalPlayer is null)
            return;
        _untilPoll -= delta;
        if (_untilPoll > 0)
            return;
        _untilPoll = PollSeconds;
        _crews.RequestPanel(); // refresca estado y candidatos
    }

    private void Decide()
    {
        var status = _crews.LastStatus;
        if (status is null || Completed)
            return;

        if (status.InCrew && status.Members.Count >= 2)
        {
            Completed = true;
            State = $"en «{status.CrewName}» con {status.Members.Count} miembros ({(status.IsLeader ? "jefe" : "miembro")})";
            GD.Print($"[DISTRITO] [bot-cuadrilla] listo: {State}");
            return;
        }

        if (_createName is not null)
        {
            if (!status.InCrew && !_created)
            {
                _created = true;
                State = "fundando";
                _crews.Create(_createName);
            }
            else if (status.InCrew)
            {
                foreach (var candidate in _crews.LastCandidates.Where(c => _invited.Add(c.Peer)))
                {
                    State = $"invitando a {candidate.Name}";
                    _crews.Invite(candidate.Peer);
                }
            }
        }
        else if (!status.InCrew && !_responded && status.Invitations.Count > 0)
        {
            _responded = true;
            State = $"aceptando «{status.Invitations[0].CrewName}»";
            _crews.Respond(status.Invitations[0].CrewId, accept: true);
        }
    }
}
