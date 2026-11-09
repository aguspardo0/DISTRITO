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
/// <param name="InterestRadius">Servidor: radio de interés de red en metros.</param>
/// <param name="MetricsIntervalSeconds">Servidor: cada cuántos segundos imprime métricas (0 = nunca).</param>
/// <param name="BotGoto">El bot camina hasta este punto (X, Z) y se queda ahí.</param>
/// <param name="BotRoute">El bot recorre estos puntos en orden (--bot-route "X,Z;X,Z;…") y se queda en el último.</param>
/// <param name="ExpectVisibleAtEnd">La prueba exige ver exactamente esta cantidad de jugadores al final (incluido uno mismo).</param>
/// <param name="BotBusiness">El bot abre un puesto de café, lo repone, espera ventas y retira la ganancia.</param>
/// <param name="BotContract">El bot cumple el contrato de prueba (depósito → taller) y la prueba verifica el cobro.</param>
/// <param name="UiSnapshotDir">Prueba visual: guarda capturas de pantalla de la interfaz en esta carpeta y cierra.</param>
/// <param name="Benchmark">Medición de rendimiento: partida local, recorrido fijo por la ciudad en las tres calidades gráficas, registra FPS y cierra.</param>
/// <param name="ExpectMapPlayers">La prueba exige que el mapa muestre esta cantidad de jugadores al final.</param>
/// <param name="MaxPlayers">Servidor: cuántos jugadores entran en la zona (por defecto 64; las pruebas de escala lo suben).</param>
/// <param name="BotContractCode">Qué contrato juega el bot (por defecto, la entrega de repuestos).</param>
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
    bool BotCrewJoin = false,
    float InterestRadius = NetworkDefaults.InterestRadius,
    int MetricsIntervalSeconds = 0,
    (float X, float Z)? BotGoto = null,
    IReadOnlyList<(float X, float Z)>? BotRoute = null,
    int? ExpectVisibleAtEnd = null,
    bool BotBusiness = false,
    string? BotContractCode = null,
    int? ExpectMapPlayers = null,
    string? UiSnapshotDir = null,
    bool Benchmark = false,
    bool BotFight = false,
    string? BotFightWeapon = null,
    int MaxPlayers = NetworkDefaults.MaxPlayersPerZone)
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
                case "--interest-radius":
                    options = options with { InterestRadius = ParseInt(RequireValue(args, ref i), args[i - 1], 1, 10_000) };
                    break;
                case "--max-players":
                    options = options with { MaxPlayers = ParseInt(RequireValue(args, ref i), args[i - 1], 1, NetworkDefaults.MaxTestPlayers) };
                    break;
                case "--metrics-interval":
                    options = options with { MetricsIntervalSeconds = ParseInt(RequireValue(args, ref i), args[i - 1], 1, 3600) };
                    break;
                case "--bot-route":
                    options = options with { Bot = true, BotRoute = RequireValue(args, ref i).Split(';', StringSplitOptions.RemoveEmptyEntries).Select(ParsePoint).ToList() };
                    break;
                case "--bot-goto":
                    options = options with { Bot = true, BotGoto = ParsePoint(RequireValue(args, ref i)) };
                    break;
                case "--bot-fight-weapon":
                    options = options with { Bot = true, BotFight = true, BotFightWeapon = RequireValue(args, ref i) };
                    break;
                case "--bot-fight":
                    options = options with { Bot = true, BotFight = true };
                    break;
                case "--benchmark":
                    options = options with { Benchmark = true };
                    break;
                case "--ui-snapshot":
                    options = options with { UiSnapshotDir = RequireValue(args, ref i) };
                    break;
                case "--expect-map-players":
                    options = options with { ExpectMapPlayers = ParseInt(RequireValue(args, ref i), args[i - 1], 0, NetworkDefaults.MaxTestPlayers) };
                    break;
                case "--expect-visible-end":
                    options = options with { ExpectVisibleAtEnd = ParseInt(RequireValue(args, ref i), args[i - 1], 0, NetworkDefaults.MaxTestPlayers) };
                    break;
                case "--bot-business":
                    options = options with { Bot = true, BotBusiness = true };
                    break;
                case "--bot-contract":
                    options = options with { Bot = true, BotContract = true };
                    break;
                case "--bot-contract-code":
                    options = options with { Bot = true, BotContract = true, BotContractCode = RequireValue(args, ref i) };
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
                    options = options with { ExpectPlayers = ParseInt(RequireValue(args, ref i), args[i - 1], 1, NetworkDefaults.MaxTestPlayers) };
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
