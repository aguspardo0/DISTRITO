using Distrito.Game.Network;
using Distrito.Server.Core.Npcs;
using Godot;

namespace Distrito.Game.Diagnostics;

/// <summary>
/// Prueba visual (CI con pantalla virtual): saca capturas del menú, el juego y cada panel abierto,
/// para revisar que todo se vea y esté ubicado en ventana y en pantalla completa.
/// </summary>
public partial class UiSnapshot : Node
{
    private readonly List<(string Name, Action Prepare)> _steps = [];
    private string _dir = "";
    private int _index = -1;
    private double _wait = 2.0;

    public void Initialize(string dir, IEnumerable<(string Name, Action Prepare)> steps)
    {
        _dir = dir;
        _steps.AddRange(steps);
        DirAccess.MakeDirRecursiveAbsolute(dir);
    }

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0)
            return;

        if (_index >= 0)
        {
            var image = GetViewport().GetTexture().GetImage();
            var path = $"{_dir}/{_index:00}-{_steps[_index].Name}.png";
            image.SavePng(path);
            GD.Print($"[DISTRITO] UI_SNAPSHOT {path} {image.GetWidth()}x{image.GetHeight()}");
        }

        _index++;
        if (_index >= _steps.Count)
        {
            GD.Print("[DISTRITO] UI_SNAPSHOT_DONE");
            GetTree().Quit(0);
            return;
        }
        _steps[_index].Prepare();
        _wait = 1.0;
    }
}
