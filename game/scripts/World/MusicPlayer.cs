using Godot;

namespace Distrito.Game.World;

/// <summary>
/// Música del juego: discos de jazz de dominio público (grabaciones de 1925 o antes; ver audio/musica/CREDITOS.md).
/// Toca los archivos que haya en res://audio/musica (ogg o mp3) en orden aleatorio, con una pausa entre tema y tema,
/// como una radio. Solo en los clientes; el volumen se elige en Esc (Opciones). Sin archivos, no hace nada.
/// </summary>
public partial class MusicPlayer : Node
{
    public const string Folder = "res://audio/musica";

    private AudioStreamPlayer _player = null!;
    private readonly List<string> _tracks = new();
    private int _next;
    private double _wait = 3;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            SetProcess(false);
            return;
        }
        _player = new AudioStreamPlayer { Name = "Radio" };
        AddChild(_player);
        _player.Finished += () => _wait = 6;
        using var dir = DirAccess.Open(Folder);
        if (dir is not null)
            foreach (var name in dir.GetFiles())
            {
                // En el juego exportado los archivos aparecen como «tema.ogg.import»: se carga el original.
                var file = name.EndsWith(".import") ? name[..^7] : name;
                if ((file.EndsWith(".ogg") || file.EndsWith(".mp3")) && !_tracks.Contains(file))
                    _tracks.Add(file);
            }
        var rng = new Random();
        for (var i = _tracks.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (_tracks[i], _tracks[j]) = (_tracks[j], _tracks[i]);
        }
        Distrito.Game.UI.GameSettings.MusicVolumeChanged += ApplyVolume;
        ApplyVolume();
        GD.Print($"[DISTRITO] MUSICA temas={_tracks.Count}");
    }

    public override void _ExitTree() => Distrito.Game.UI.GameSettings.MusicVolumeChanged -= ApplyVolume;

    private void ApplyVolume()
    {
        var v = Distrito.Game.UI.GameSettings.MusicVolume;
        _player.VolumeDb = v <= 0.001f ? -80 : Mathf.LinearToDb(v * 0.6f); // la música queda por debajo del juego
    }

    public override void _Process(double delta)
    {
        if (_tracks.Count == 0 || _player.Playing)
            return;
        _wait -= delta;
        if (_wait > 0)
            return;
        var file = _tracks[_next++ % _tracks.Count];
        if (ResourceLoader.Load<AudioStream>($"{Folder}/{file}") is { } stream)
        {
            _player.Stream = stream;
            _player.Play();
        }
        _wait = 6;
    }
}
