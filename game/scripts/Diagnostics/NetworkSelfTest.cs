using Distrito.Game.Network;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Prueba automática del cliente (CI): después de N segundos verifica que está conectado,
/// que ve a la cantidad esperada de jugadores y que su personaje se movió por orden del servidor.
/// Imprime NET_TEST_OK o NET_TEST_FAIL y cierra el proceso.
/// </summary>
public partial class NetworkSelfTest : Node
{
    private const float MinimumDisplacement = 1.0f;

    private GameNetwork _network = null!;
    private int _expectedPlayers;
    private double _remaining;
    private Vector3? _firstPosition;
    private float _maxDisplacement;
    private int _maxPlayersSeen;

    public void Configure(GameNetwork network, int seconds, int expectedPlayers)
    {
        _network = network;
        _remaining = seconds;
        _expectedPlayers = expectedPlayers;
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

        _remaining -= delta;
        if (_remaining > 0)
            return;

        SetProcess(false);
        Finish(local);
    }

    private void Finish(Player.PlayerCharacter? local)
    {
        var connected = Multiplayer.MultiplayerPeer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
        // Se usa el máximo visto: otro cliente de la prueba puede terminar y desconectarse antes.
        var players = _maxPlayersSeen;
        var summary = $"id={Multiplayer.GetUniqueId()} conectado={connected} jugadores_max={players}/{_expectedPlayers} " +
                      $"desplazamiento={_maxDisplacement:0.00}m";

        string? failure =
            !connected ? "no está conectado" :
            local is null ? "no apareció el personaje propio" :
            players < _expectedPlayers ? "no ve a todos los jugadores" :
            _maxDisplacement < MinimumDisplacement ? "el servidor no movió al personaje" :
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

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
