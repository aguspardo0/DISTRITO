using Distrito.Shared.Launch;
using Distrito.Shared.Networking;

namespace Distrito.Shared.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Sin_argumentos_arranca_como_cliente_con_valores_por_defecto()
    {
        var options = LaunchOptions.Parse([]);

        Assert.Equal(LaunchMode.Client, options.Mode);
        Assert.Equal(NetworkDefaults.DefaultHost, options.Host);
        Assert.Equal(NetworkDefaults.DefaultPort, options.Port);
        Assert.False(options.SmokeTest);
    }

    [Fact]
    public void Interpreta_modo_servidor_puerto_y_smoke_test()
    {
        var options = LaunchOptions.Parse(["--server", "--port", "9000", "--smoke-test"]);

        Assert.Equal(LaunchMode.Server, options.Mode);
        Assert.Equal(9000, options.Port);
        Assert.True(options.SmokeTest);
    }

    [Fact]
    public void Interpreta_host_del_cliente_y_activa_conexion_automatica()
    {
        var options = LaunchOptions.Parse(["--client", "--host", "10.0.0.5"]);

        Assert.Equal(LaunchMode.Client, options.Mode);
        Assert.Equal("10.0.0.5", options.Host);
        Assert.True(options.AutoConnect);
    }

    [Fact]
    public void Sin_argumentos_muestra_menu_y_no_es_prueba()
    {
        var options = LaunchOptions.Parse([]);

        Assert.False(options.AutoConnect);
        Assert.False(options.IsSelfTest);
        Assert.False(options.Bot);
    }

    [Fact]
    public void Interpreta_opciones_de_prueba_de_red()
    {
        var options = LaunchOptions.Parse(["--client", "--bot", "--test-seconds", "8", "--expect-players", "2"]);

        Assert.True(options.Bot);
        Assert.True(options.IsSelfTest);
        Assert.Equal(8, options.TestSeconds);
        Assert.Equal(2, options.ExpectPlayers);
    }

    [Theory]
    [InlineData("--test-seconds", "0")]
    [InlineData("--expect-players", "0")]
    [InlineData("--expect-players", "1000")]
    public void Rechaza_valores_de_prueba_fuera_de_rango(string flag, string value)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse([flag, value]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    public void Rechaza_puertos_invalidos(string port)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--port", port]));
    }

    [Fact]
    public void Rechaza_argumento_sin_valor()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--port", "--server"]));
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--host"]));
    }

    [Fact]
    public void Rechaza_argumentos_desconocidos()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--modo-dios"]));
    }
}

public class LaunchOptionsAccountTests
{
    [Fact]
    public void Interpreta_cuenta_de_bot_y_backend()
    {
        var options = LaunchOptions.Parse([
            "--client", "--backend", "http://localhost:5080/", "--account", "bot1",
            "--password", "secreta123", "--character", "Bot Uno", "--expect-spawn", "4.5,-2"]);

        Assert.Equal("http://localhost:5080/", options.BackendUrl);
        Assert.Equal("bot1", options.Account);
        Assert.Equal("secreta123", options.Password);
        Assert.Equal("Bot Uno", options.Character);
        Assert.Equal((4.5f, -2f), options.ExpectSpawn);
    }

    [Fact]
    public void Cuenta_sin_el_resto_de_datos_es_invalida()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--account", "bot1"]));
    }

    [Theory]
    [InlineData("localhost:5080")]
    [InlineData("ftp://servidor")]
    public void Rechaza_urls_de_backend_invalidas(string url)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--backend", url]));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("a,b")]
    [InlineData("1,2,3")]
    public void Rechaza_puntos_invalidos(string point)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--expect-spawn", point]));
    }
}

public class LaunchOptionsContractBotTests
{
    [Fact]
    public void Bot_de_contrato_activa_tambien_el_bot()
    {
        var options = LaunchOptions.Parse(["--bot-contract"]);
        Assert.True(options.Bot);
        Assert.True(options.BotContract);
    }
}
