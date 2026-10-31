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
    public void Interpreta_host_del_cliente()
    {
        var options = LaunchOptions.Parse(["--client", "--host", "10.0.0.5"]);

        Assert.Equal(LaunchMode.Client, options.Mode);
        Assert.Equal("10.0.0.5", options.Host);
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
