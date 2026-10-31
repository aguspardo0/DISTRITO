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
/// <param name="Mode">Cliente (con ventana) o servidor dedicado.</param>
/// <param name="AutoConnect">El cliente se conecta solo a <paramref name="Host"/> sin mostrar el menú.</param>
/// <param name="SmokeTest">Arranca, informa y sale. Para CI.</param>
/// <param name="Bot">El cliente se mueve solo (sin teclado). Para pruebas de red.</param>
/// <param name="TestSeconds">Si es mayor que 0, el cliente verifica la conexión después de ese tiempo y sale.</param>
/// <param name="ExpectPlayers">Cantidad de jugadores que la prueba espera ver.</param>
public sealed record LaunchOptions(
    LaunchMode Mode,
    string Host,
    int Port,
    bool AutoConnect,
    bool SmokeTest,
    bool Bot,
    int TestSeconds,
    int ExpectPlayers)
{
    public static LaunchOptions Default { get; } = new(
        LaunchMode.Client,
        NetworkDefaults.DefaultHost,
        NetworkDefaults.DefaultPort,
        AutoConnect: false,
        SmokeTest: false,
        Bot: false,
        TestSeconds: 0,
        ExpectPlayers: 1);

    public bool IsSelfTest => TestSeconds > 0;

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
                    options = options with { Mode = LaunchMode.Client, AutoConnect = true };
                    break;
                case "--smoke-test":
                    options = options with { SmokeTest = true };
                    break;
                case "--bot":
                    options = options with { Bot = true };
                    break;
                case "--host":
                    options = options with { Host = RequireValue(args, ref i) };
                    break;
                case "--port":
                    options = options with { Port = ParseInt(RequireValue(args, ref i), args[i - 1], 1, 65535) };
                    break;
                case "--test-seconds":
                    options = options with { TestSeconds = ParseInt(RequireValue(args, ref i), args[i - 1], 1, 600) };
                    break;
                case "--expect-players":
                    options = options with { ExpectPlayers = ParseInt(RequireValue(args, ref i), args[i - 1], 1, NetworkDefaults.MaxPlayersPerZone) };
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

    private static int ParseInt(string value, string flag, int min, int max)
    {
        if (!int.TryParse(value, out var number) || number < min || number > max)
            throw new ArgumentException($"Valor inválido para '{flag}': '{value}'. Debe estar entre {min} y {max}.");
        return number;
    }
}
