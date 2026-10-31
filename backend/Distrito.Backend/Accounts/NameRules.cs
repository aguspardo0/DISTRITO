using System.Text.RegularExpressions;

namespace Distrito.Backend.Accounts;

/// <summary>Validación de nombres de usuario, contraseñas y personajes. Valores provisionales: ver ADR-002.</summary>
public static partial class NameRules
{
    public const int MaxCharactersPerAccount = 3;

    public static string? ValidateUsername(string? username) =>
        username is not null && UsernamePattern().IsMatch(username)
            ? null
            : "El usuario debe tener entre 3 y 20 caracteres: letras, números o guion bajo.";

    public static string? ValidatePassword(string? password) =>
        password is { Length: >= 8 and <= 128 }
            ? null
            : "La contraseña debe tener entre 8 y 128 caracteres.";

    public static string? ValidateCharacterName(string? name) =>
        name is not null && name == CollapseSpaces(name) && CharacterPattern().IsMatch(name)
            ? null
            : "El nombre debe tener entre 3 y 24 letras, con espacios simples entre palabras.";

    /// <summary>Forma canónica para detectar duplicados sin importar mayúsculas.</summary>
    public static string Normalize(string value) => CollapseSpaces(value).ToUpperInvariant();

    private static string CollapseSpaces(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex(@"^[A-Za-z0-9_]{3,20}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^\p{L}[\p{L} ]{1,22}\p{L}$")]
    private static partial Regex CharacterPattern();
}
