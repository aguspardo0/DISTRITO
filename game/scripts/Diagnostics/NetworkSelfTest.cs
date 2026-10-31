using System.Globalization;
using Distrito.Game.Network;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Prueba automática del cliente (CI). Después de N segundos verifica:
/// conectado, ve a los jugadores esperados, el servidor movió al personaje,
/// y (opcional) apareció donde se había guardado.
/// Imprime NET_TEST_OK / NET_TEST_FAIL con la posición final (pos=X,Z) y cierra el proceso.
/// </summary>
public partial class NetworkSelfTest : Node
{
    private const float MinimumDisplacement = 1.0f;
    private const float SpawnTolerance = 0.5f;

    private GameNetwork _network = null!;
    private int _expectedPlayers;
    private bool _expectMovement;
    private (float X, float Z)? _expectedSpawn;
    private double _remaining;
    private Vector3? _firstPosition;
    private float _maxDisplacement;
    private int _maxPlayersSeen;
    private ContractBot? _contractBot;
    private double _graceAfterContract = 1.0;

    private CrewBot? _crewBot;
    private int? _expectVisibleAtEnd;
    private double _rttSum;
    private int _rttSamples;
    private double _rttMax;

    /// <summary>Si se asigna, la prueba exige ver exactamente esta cantidad de jugadores al terminar.</summary>
    public void ExpectVisibleAtEnd(int count) => _expectVisibleAtEnd = count;

    /// <summary>Si se asigna, la prueba exige que el bot termine en una cuadrilla de 2.</summary>
    public void ExpectCrew(CrewBot bot) => _crewBot = bot;

    /// <summary>Si se asigna, la prueba exige que el bot cumpla el contrato y cobre lo correcto.</summary>
    public void ExpectContract(ContractBot bot) => _contractBot = bot;

    public void Configure(GameNetwork network, int seconds, int expectedPlayers, bool expectMovement, (float X, float Z)? expectedSpawn)
    {
        _network = network;
        _remaining = seconds;
        _expectedPlayers = expectedPlayers;
        _expectMovement = expectMovement;
        _expectedSpawn = expectedSpawn;
    }

    public override void _Process(double delta)
    {
        var local = _network.LocalPlayer;
        if (local is not null)
        {
            _firstPosition ??= local.GlobalPosition;
            _maxDisplacement = Mathf.Max(_maxDisplacement, FlatDistance(_firstPosition.Value, local.GlobalPosition));
        }
        _maxPlayersSeen = Math.Max(_maxPlayersSeen, _network.PlayerCount);
        SampleRtt();

        _remaining -= delta;
        var scriptedDone = (_contractBot ?? (object?)_crewBot) is not null
                           && (_contractBot is null || _contractBot.Completed)
                           && (_crewBot is null || _crewBot.Completed);
        if (scriptedDone)
            _graceAfterContract -= delta;
        if (_remaining > 0 && _graceAfterContract > 0)
            return;

        SetProcess(false);
        Finish(local);
    }

    private void Finish(Player.PlayerCharacter? local)
    {
        var connected = Multiplayer.MultiplayerPeer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
        var final = local?.GlobalPosition ?? Vector3.Zero;
        var first = _firstPosition ?? Vector3.Zero;
        var spawnError = _expectedSpawn is { } e ? new Vector2(first.X - e.X, first.Z - e.Z).Length() : 0f;

        var visibleNow = _network.PlayerCount;
        var rttInfo = _rttSamples == 0 ? "" : $" ping_ms_prom={_rttSum / _rttSamples:0} ping_ms_max={_rttMax:0}";
        var contractInfo = rttInfo + $" visibles_al_final={visibleNow}" + (_contractBot is null
            ? ""
            : $" contrato={_contractBot.StepName} saldo={_contractBot.StartBalance}->{_contractBot.FinalBalance}")
            + (_crewBot is null ? "" : $" cuadrilla=\"{_crewBot.State}\"");
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"id={Multiplayer.GetUniqueId()} nombre=\"{local?.DisplayName}\" conectado={connected} " +
            $"jugadores_max={_maxPlayersSeen}/{_expectedPlayers} desplazamiento={_maxDisplacement:0.00}m " +
            $"aparicion={first.X:0.00},{first.Z:0.00} pos={final.X:0.00},{final.Z:0.00}{contractInfo}");

        var expectedGain = Distrito.Shared.Economy.EconomyCatalog.SparePartsDelivery.RewardCents
                           - Distrito.Shared.Economy.EconomyCatalog.SparePartsPriceCents;

        string? failure =
            !connected ? "no está conectado" :
            local is null ? "no apareció el personaje propio" :
            _maxPlayersSeen < _expectedPlayers ? "no ve a todos los jugadores" :
            _expectMovement && _maxDisplacement < MinimumDisplacement ? "el servidor no movió al personaje" :
            spawnError > SpawnTolerance ? $"apareció a {spawnError:0.00}m del lugar guardado" :
            _expectVisibleAtEnd is { } v && visibleNow != v ? $"al final ve {visibleNow} jugadores y se esperaban {v}" :
            _crewBot is { Completed: false } ? $"la cuadrilla no se formó (quedó: {_crewBot.State})" :
            _contractBot is { Completed: false } ? $"el contrato no se completó (quedó en {_contractBot.StepName})" :
            _contractBot is not null && _contractBot.FinalBalance - _contractBot.StartBalance != expectedGain
                ? $"el saldo cambió {_contractBot.FinalBalance - _contractBot.StartBalance} y se esperaba {expectedGain}" :
            null;

        if (failure is null)
        {
            GD.Print($"[DISTRITO] NET_TEST_OK {summary}");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"[DISTRITO] NET_TEST_FAIL ({failure}) {summary}");
            GetTree().Quit(1);
        }
    }

    private void SampleRtt()
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer enet || Multiplayer.IsServer())
            return;
        var server = enet.GetPeer(1);
        if (server is null)
            return;
        var rtt = server.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
        if (rtt <= 0)
            return;
        _rttSum += rtt;
        _rttSamples++;
        _rttMax = Math.Max(_rttMax, rtt);
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
