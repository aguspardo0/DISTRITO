using Distrito.Game.Network;
using Distrito.Game.UI;
using Distrito.Server.Core.World;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Medición de rendimiento (<c>--benchmark</c>): partida local con lluvia y escenas, y un recorrido fijo por la Calle
/// Principal, adentro del diner, una esquina del Centro y Los Muelles, en las tres calidades gráficas. En cada parada
/// espera 2 segundos y mide 4: cuadros por segundo, el tiempo del cuadro más lento (5 % peor), llamadas de dibujo,
/// objetos y triángulos. Escribe una línea <c>BENCH</c> por parada y cierra.
/// </summary>
public partial class Benchmark : Node
{
    private const double Settle = 2, Measure = 4;

    private GameNetwork _network = null!;
    private readonly List<(string Name, Vector3 At, float Yaw)> _stops = [];
    private int _quality, _stop = -1;
    private double _clock;
    private bool _started;
    private readonly List<double> _frames = [];
    private long _drawCalls, _objects, _primitives;

    public void Initialize(GameNetwork network)
    {
        _network = network;
        var diner = InteriorLayout.Find("diner")!;
        _stops.AddRange(
        [
            ("calle", new Vector3(-10, 0.2f, 2), -Mathf.Pi / 2),
            ("interior", new Vector3(diner.EntryPoint.X, 0.25f, diner.EntryPoint.Y), diner.EntryYaw),
            ("centro", new Vector3(114, 0.2f, 114), Mathf.Pi / 4),
            ("puerto", new Vector3(258, 0.2f, 2), -Mathf.Pi / 2),
        ]);
    }

    public override void _Process(double delta)
    {
        if (_network.LocalPlayer is not { } me)
            return;
        if (!_started)
        {
            _started = true;
            // Lo más pesado que puede pasar a la vez: lluvia, un músico con público y una discusión.
            World.SceneNetwork.Instance?.Trigger("lluvia", null, 60);
            World.SceneNetwork.Instance?.Trigger("musico", 0, 30);
            World.SceneNetwork.Instance?.Trigger("discusion", 0, 20);
            GameSettings.PreviewQuality(0);
            Next(me);
            return;
        }
        _clock += delta;
        if (_clock > Settle)
        {
            _frames.Add(delta);
            _drawCalls += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            _objects += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
            _primitives += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        }
        if (_clock < Settle + Measure)
            return;
        Report();
        Next(me);
    }

    private void Next(Player.PlayerCharacter me)
    {
        _stop++;
        if (_stop >= _stops.Count)
        {
            _stop = 0;
            _quality++;
            if (_quality > 2)
            {
                GD.Print("[DISTRITO] BENCH_DONE");
                GetTree().Quit(0);
                return;
            }
            GameSettings.PreviewQuality(_quality);
        }
        var (_, at, yaw) = _stops[_stop];
        me.GlobalPosition = at;
        me.GetNodeOrNull<Player.LocalPlayerController>("LocalController")?.LookToward(yaw);
        _clock = 0;
        _frames.Clear();
        _drawCalls = _objects = _primitives = 0;
    }

    private void Report()
    {
        var n = Math.Max(1, _frames.Count);
        var fps = _frames.Count / Math.Max(0.001, _frames.Sum());
        var sorted = _frames.OrderBy(f => f).ToList();
        var p95 = sorted.Count > 0 ? sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))] * 1000 : 0;
        GD.Print(FormattableString.Invariant(
            $"[DISTRITO] BENCH calidad={World.GraphicsQuality.Levels[_quality].Name} lugar={_stops[_stop].Name} fps={fps:0.0} cuadro_lento_ms={p95:0.0} dibujos={_drawCalls / n} objetos={_objects / n} triangulos={_primitives / n}"));
    }
}
