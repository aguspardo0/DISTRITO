using System.Net;
using Distrito.Shared.Api;

namespace Distrito.Backend.Tests;

/// <summary>Flujo completo contra PostgreSQL real, usando el mismo cliente HTTP que usa el juego.</summary>
[Trait("Category", "Database")]
public class AccountsAndCharactersApiTests
{
    private const string Password = "una-contraseña-larga";

    private static async Task<(DistritoApiClient Api, string Token)> NewLoggedInAccount(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        Assert.True((await api.RegisterAsync(user, Password)).IsSuccess);
        var login = await api.LoginAsync(user, Password);
        Assert.True(login.IsSuccess, login.Error?.Message);
        return (api, login.Value!.Token);
    }

    [Fact]
    public async Task Registro_login_y_usuario_duplicado()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var user = TestBackend.UniqueUser();

        var first = await api.RegisterAsync(user, Password);
        Assert.Equal(HttpStatusCode.Created, first.Status);

        var duplicated = await api.RegisterAsync(user.ToUpperInvariant(), Password);
        Assert.Equal(HttpStatusCode.Conflict, duplicated.Status);
        Assert.Equal("username_taken", duplicated.Error!.Code);

        var badPassword = await api.LoginAsync(user, "otra-contraseña");
        Assert.Equal(HttpStatusCode.Unauthorized, badPassword.Status);

        var unknown = await api.LoginAsync(TestBackend.UniqueUser(), Password);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);

        var login = await api.LoginAsync(user, Password);
        Assert.True(login.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(login.Value!.Token));
        Assert.True(login.Value.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Registro_rechaza_datos_invalidos()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await api.RegisterAsync("x", Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.RegisterAsync(TestBackend.UniqueUser(), "corta")).Status);
    }

    [Fact]
    public async Task Personajes_requieren_sesion_valida()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.ListCharactersAsync("token-inventado")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateCharacterAsync("token-inventado", "Nadie Nunca")).Status);
    }

    [Fact]
    public async Task Crear_y_listar_personajes_con_nombre_unico_y_limite()
    {
        await using var factory = TestBackend.Create();
        var (api, token) = await NewLoggedInAccount(TestBackend.Api(factory));

        Assert.Empty((await api.ListCharactersAsync(token)).Value!);

        var name = TestBackend.UniqueCharacterName();
        var created = await api.CreateCharacterAsync(token, name);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(name, created.Value!.Name);
        Assert.Equal(ZoneIds.TestNeighborhood, created.Value.ZoneId);

        // Mismo nombre en otra cuenta: rechazado (los nombres son globales).
        var (_, otherToken) = await NewLoggedInAccount(api);
        var taken = await api.CreateCharacterAsync(otherToken, name.ToLowerInvariant());
        Assert.Equal("name_taken", taken.Error!.Code);

        Assert.True((await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).IsSuccess);
        Assert.True((await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).IsSuccess);
        var overLimit = await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName());
        Assert.Equal("limit_reached", overLimit.Error!.Code);

        var list = (await api.ListCharactersAsync(token)).Value!;
        Assert.Equal(3, list.Count);
        Assert.Equal(name, list[0].Name);
    }

    [Fact]
    public async Task Api_interna_resuelve_sesion_y_guarda_posicion()
    {
        await using var factory = TestBackend.Create();
        var (api, token) = await NewLoggedInAccount(TestBackend.Api(factory));
        var character = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!;

        var resolved = await api.ResolveSessionAsync(TestBackend.ServerKey, token, character.Id);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(character.Name, resolved.Value!.CharacterName);
        Assert.Equal(ZoneIds.NewCharacterSpawn, resolved.Value.State);

        var newState = new CharacterState(12.5, 1, -7.25, 1.5);
        Assert.True((await api.SaveCharacterStateAsync(TestBackend.ServerKey, character.Id, newState)).IsSuccess);

        var reloaded = await api.ResolveSessionAsync(TestBackend.ServerKey, token, character.Id);
        Assert.Equal(newState, reloaded.Value!.State);
    }

    [Fact]
    public async Task Api_interna_no_entrega_personajes_de_otra_cuenta()
    {
        await using var factory = TestBackend.Create();
        var (api, ownerToken) = await NewLoggedInAccount(TestBackend.Api(factory));
        var character = (await api.CreateCharacterAsync(ownerToken, TestBackend.UniqueCharacterName())).Value!;
        var (_, intruderToken) = await NewLoggedInAccount(api);

        var result = await api.ResolveSessionAsync(TestBackend.ServerKey, intruderToken, character.Id);

        Assert.Equal(HttpStatusCode.NotFound, result.Status);
    }

    [Fact]
    public async Task Api_interna_exige_la_clave_del_servidor()
    {
        await using var factory = TestBackend.Create();
        var (api, token) = await NewLoggedInAccount(TestBackend.Api(factory));
        var character = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!;

        var wrongKey = await api.ResolveSessionAsync("clave-falsa", token, character.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.Status);

        var save = await api.SaveCharacterStateAsync("clave-falsa", character.Id, new CharacterState(0, 0, 0, 0));
        Assert.Equal(HttpStatusCode.Unauthorized, save.Status);
    }

    [Fact]
    public async Task Api_interna_apagada_si_no_hay_clave_configurada()
    {
        await using var factory = TestBackend.Create(serverKey: null);
        var api = TestBackend.Api(factory);

        var result = await api.ResolveSessionAsync("", "token", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.Status);
    }

    [Fact]
    public async Task Migraciones_se_pueden_aplicar_dos_veces()
    {
        await using var first = TestBackend.Create();
        _ = first.CreateClient();
        await using var second = TestBackend.Create();
        var response = await second.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
