using System.Text.Json;
using Godot;

namespace Distrito.Game.Backend;

/// <summary>
/// Dirección del servidor oficial, para que el menú venga completo.
/// El archivo res://server_defaults.json lo genera la CI al exportar el cliente (no está en el repo).
/// </summary>
public sealed record ServerDefaults(string BackendUrl, string GameHost, string Version = "")
{
    private const string FilePath = "res://server_defaults.json";

    public static ServerDefaults Empty { get; } = new("", "", "");

    public static ServerDefaults Load()
    {
        if (!Godot.FileAccess.FileExists(FilePath))
            return Empty;

        try
        {
            var text = Godot.FileAccess.GetFileAsString(FilePath);
            return JsonSerializer.Deserialize<ServerDefaults>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? Empty;
        }
        catch (JsonException ex)
        {
            GD.PushWarning($"[DISTRITO] server_defaults.json inválido: {ex.Message}");
            return Empty;
        }
    }
}
