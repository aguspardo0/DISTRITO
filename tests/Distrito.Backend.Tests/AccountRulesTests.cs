using Distrito.Backend.Accounts;
using Distrito.Backend.Persistence;

namespace Distrito.Backend.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new(1_000);

    [Fact]
    public void Verifica_la_contrasena_correcta_y_rechaza_otra()
    {
        var hash = _hasher.Hash("contraseña-segura");

        Assert.True(PasswordHasher.Verify("contraseña-segura", hash));
        Assert.False(PasswordHasher.Verify("contraseña-segurA", hash));
    }

    [Fact]
    public void Dos_hashes_de_la_misma_contrasena_son_distintos_por_la_sal()
    {
        Assert.NotEqual(_hasher.Hash("igual-igual"), _hasher.Hash("igual-igual"));
    }

    [Fact]
    public void Guarda_las_iteraciones_en_el_hash()
    {
        Assert.StartsWith("pbkdf2-sha256$1000$", _hasher.Hash("algo-largo"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("texto-plano")]
    [InlineData("pbkdf2-sha256$10$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$5000$no-base64$no-base64")]
    public void Hashes_malformados_no_verifican(string stored)
    {
        Assert.False(PasswordHasher.Verify("lo-que-sea", stored));
    }

    [Fact]
    public void El_valor_por_defecto_sigue_la_recomendacion_de_owasp()
    {
        Assert.True(PasswordHasher.DefaultIterations >= 600_000);
    }
}

public class NameRulesTests
{
    [Theory]
    [InlineData("agus")]
    [InlineData("Jugador_01")]
    public void Usuarios_validos(string username) => Assert.Null(NameRules.ValidateUsername(username));

    [Theory]
    [InlineData(null)]
    [InlineData("ab")]
    [InlineData("con espacio")]
    [InlineData("demasiado_largo_para_usuario")]
    [InlineData("ñandú")]
    public void Usuarios_invalidos(string? username) => Assert.NotNull(NameRules.ValidateUsername(username));

    [Theory]
    [InlineData("Vito Corleone")]
    [InlineData("Ñoño")]
    [InlineData("Al")]
    public void Nombres_de_personaje(string name)
    {
        var valid = name.Length >= 3;
        Assert.Equal(valid, NameRules.ValidateCharacterName(name) is null);
    }

    [Theory]
    [InlineData("Lucky  Luciano")]
    [InlineData(" Capone")]
    [InlineData("Agente 007")]
    [InlineData("Nombre_con_guion")]
    public void Nombres_de_personaje_invalidos(string name) => Assert.NotNull(NameRules.ValidateCharacterName(name));

    [Fact]
    public void Normalizar_ignora_mayusculas()
    {
        Assert.Equal(NameRules.Normalize("vito corleone"), NameRules.Normalize("Vito Corleone"));
    }
}

public class MigrationDiscoveryTests
{
    [Fact]
    public void Las_migraciones_del_repo_tienen_versiones_unicas_y_consecutivas()
    {
        var versions = MigrationRunner.Discover(TestBackend.MigrationsPath).Select(m => m.Version).ToList();

        Assert.NotEmpty(versions);
        Assert.Equal(Enumerable.Range(1, versions.Count), versions);
    }
}
