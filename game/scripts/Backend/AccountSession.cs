using Distrito.Shared.Api;

namespace Distrito.Game.Backend;

/// <summary>
/// Lado cliente de las cuentas: login, registro y personajes contra el backend.
/// Se usa desde el menú y desde los bots de prueba.
/// </summary>
public sealed class AccountSession : IDisposable
{
    private readonly HttpClient _http;

    public AccountSession(string backendUrl)
    {
        var baseUrl = backendUrl.EndsWith('/') ? backendUrl : backendUrl + "/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
        Api = new DistritoApiClient(_http);
    }

    public DistritoApiClient Api { get; }
    public string? Token { get; private set; }
    public List<CharacterSummary> Characters { get; private set; } = [];

    public async Task<string?> LoginAsync(string username, string password)
    {
        var result = await Api.LoginAsync(username, password);
        if (!result.IsSuccess)
            return result.Error!.Message;

        Token = result.Value!.Token;
        return await RefreshCharactersAsync();
    }

    /// <summary>Crea la cuenta y entra. Si ya existe, intenta entrar con esa contraseña.</summary>
    public async Task<string?> RegisterAndLoginAsync(string username, string password, bool acceptExisting)
    {
        var register = await Api.RegisterAsync(username, password);
        if (!register.IsSuccess && !(acceptExisting && register.Error!.Code == "username_taken"))
            return register.Error!.Message;
        return await LoginAsync(username, password);
    }

    public async Task<string?> RefreshCharactersAsync()
    {
        var list = await Api.ListCharactersAsync(Token ?? "");
        if (!list.IsSuccess)
            return list.Error!.Message;
        Characters = list.Value!;
        return null;
    }

    public async Task<(CharacterSummary? Character, string? Error)> CreateCharacterAsync(string name)
    {
        var result = await Api.CreateCharacterAsync(Token ?? "", name);
        if (!result.IsSuccess)
            return (null, result.Error!.Message);
        Characters.Add(result.Value!);
        return (result.Value, null);
    }

    /// <summary>Busca el personaje por nombre o lo crea (bots de prueba).</summary>
    public async Task<(CharacterSummary? Character, string? Error)> EnsureCharacterAsync(string name)
    {
        var existing = Characters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return existing is not null ? (existing, null) : await CreateCharacterAsync(name);
    }

    public void Dispose() => _http.Dispose();
}
