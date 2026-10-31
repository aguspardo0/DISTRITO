using Distrito.Shared.Launch;
using Godot;

namespace Distrito.Game;

/// <summary>
/// Punto de entrada. Lee las opciones de arranque y decide si el proceso corre como cliente o como servidor.
/// En esta etapa solo informa el modo; la red se agrega en la etapa D.
/// </summary>
public partial class Main : Node3D
{
    public LaunchOptions Options { get; private set; } = LaunchOptions.Default;

    public override void _Ready()
    {
        try
        {
            Options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        }
        catch (ArgumentException ex)
        {
            GD.PushError($"[DISTRITO] {ex.Message}");
            GetTree().Quit(2);
            return;
        }

        GD.Print($"[DISTRITO] Arranque OK — modo={Options.Mode} host={Options.Host} puerto={Options.Port} godot={Engine.GetVersionInfo()["string"]}");

        if (Options.SmokeTest)
        {
            GD.Print("[DISTRITO] SMOKE_TEST_OK");
            GetTree().Quit(0);
        }
    }
}
