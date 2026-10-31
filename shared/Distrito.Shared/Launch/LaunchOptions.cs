using Distrito.Shared.Networking;

namespace Distrito.Shared.Launch;

public enum LaunchMode
{
    Client,
    Server,
}

/// <summary>
/// Opciones de arranque del ejecutable del juego. Se pasan después de "--" en la línea de comandos de Godot:
/// <c>godot --headless -- --server --port 7777</c>.
/// </summary>
public sealed record LaunchOptions(LaunchMode Mode, string Host, int Port, bool SmokeTest)
{
    public static LaunchOptions Default { get; } =
        new(LaunchMode.Client, NetworkDefaults.DefaultHost, NetworkDefaults.DefaultPort, SmokeTest: false);

    /// <summary>Interpreta los argumentos de usuario. Lanza <see cref="ArgumentException"/> si son inválidos.</summary>
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = Default;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--server":
                    options = options with { Mode = LaunchMode.Server };
                    break;
                case "--client":
                    options = options with { Mode = LaunchMode.Client };
                    break;
                case "--smoke-test":
                    options = options with { SmokeTest = true };
                    break;
                case "--host":
                    options = options with { Host = RequireValue(args, ref i) };
                    break;
                case "--port":
                    options = options with { Port = ParsePort(RequireValue(args, ref i)) };
                    break;
                default:
                    throw new ArgumentException($"Argumento desconocido: '{args[i]}'.");
            }
        }

        return options;
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int i)
    {
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Falta el valor para '{args[i]}'.");
        i++;
        return args[i];
    }

    private static int ParsePort(string value)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
            throw new ArgumentException($"Puerto inválido: '{value}'. Debe estar entre 1 y 65535.");
        return port;
    }
}
