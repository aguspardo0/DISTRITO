using System.Globalization;
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
/// <param name="BackendUrl">Servidor de cuentas. En el servidor de juego activa la autenticación; null = modo invitado.</param>
/// <param name="Account">Usuario para iniciar sesión automáticamente (bots de prueba).</param>
/// <param name="Password">Contraseña de <paramref name="Account"/>.</param>
/// <param name="Character">Personaje a usar (se crea si no existe).</param>
/// <param name="ExpectSpawn">La prueba verifica que el personaje aparezca cerca de este punto (X, Z).</param>
/// <param name="BotCrewCreate">El bot funda una cuadrilla con este nombre e invita al otro jugador.</param>
/// <param name="BotCrewJoin">El bot espera una invitación a cuadrilla y la acepta.</param>
/// <param name="BotContract">El bot cumple el contrato de prueba (depósito → taller) y la prueba verifica el cobro.</param>
public sealed record LaunchOptions(
    LaunchMode Mode,
    string Host,
    int Port,
    bool AutoConnect,
    bool SmokeTest,
    bool Bot,
    int TestSeconds,
    int ExpectPlayers,
    string? BackendUrl = null,
    string? Account = null,
    string? Password = null,
    string? Character = null,
    (float X, float Z)? ExpectSpawn = null,
    bool BotContract = false,
    string? BotCrewCreate = null,
    bool BotCrewJoin = false)
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
                case "--bot-crew-create":
                    options = options with { Bot = true, BotCrewCreate = RequireValue(args, ref i) };
                    break;
                case "--bot-crew-join":
                    options = options with { Bot = true, BotCrewJoin = true };
                    break;
                case "--bot-contract":
                    options = options with { Bot = true, BotContract = true };
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
                case "--backend":
                    options = options with { BackendUrl = ParseUrl(RequireValue(args, ref i)) };
                    break;
                case "--account":
                    options = options with { Account = RequireValue(args, ref i) };
                    break;
                case "--password":
                    options = options with { Password = RequireValue(args, ref i) };
                    break;
                case "--character":
                    options = options with { Character = RequireValue(args, ref i) };
                    break;
                case "--expect-spawn":
                    options = options with { ExpectSpawn = ParsePoint(RequireValue(args, ref i)) };
                    break;
                default:
                    throw new ArgumentException($"Argumento desconocido: '{args[i]}'.");
            }
        }

        if (options.Account is not null && (options.Password is null || options.Character is null || options.BackendUrl is null))
            throw new ArgumentException("'--account' requiere también '--password', '--character' y '--backend'.");

        return options;
    }

    private static string ParseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"URL de backend inválida: '{value}'.");
        return value;
    }

    private static (float X, float Z) ParsePoint(string value)
    {
        var parts = value.Split(',');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            && float.IsFinite(x) && float.IsFinite(z))
            return (x, z);
        throw new ArgumentException($"Punto inválido: '{value}'. Formato: X,Z (por ejemplo 3.5,-2).");
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
