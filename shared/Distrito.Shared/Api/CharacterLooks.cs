namespace Distrito.Shared.Api;

/// <summary>
/// Aspecto de un personaje (las cuatro láminas de diseño del equipo, 2026-10-31). Colores en hex, sacados de las
/// paletas de cada lámina. El cliente arma el modelo con estas piezas hasta que haya modelos 3D definitivos.
/// </summary>
public sealed record CharacterLook(
    string Code,
    string Name,
    string Skin,
    string Hair,
    string Shirt,
    string Pants,
    string Top,        // saco / chaleco / campera
    string Accent,     // corbata, tiradores o detalle
    string Shoes,
    string? Hat,       // gorra (null = sin sombrero)
    bool LongCoat = false,
    bool Vest = false,
    bool Suspenders = false,
    bool Jacket = false,
    bool Mustache = false);

public static class CharacterLooks
{
    public const string Default = "trabajador";

    public static readonly CharacterLook Ejecutivo = new("ejecutivo", "El Ejecutivo",
        Skin: "#c99a7a", Hair: "#1c1612", Shirt: "#e8dcc8", Pants: "#1d1d22", Top: "#26262c", Accent: "#6e1a24", Shoes: "#141210",
        Hat: null, LongCoat: true, Vest: true);

    public static readonly CharacterLook Operativo = new("operativo", "El Operativo",
        Skin: "#c08f6c", Hair: "#2a2018", Shirt: "#ddd2bf", Pants: "#2e2620", Top: "#2e2620", Accent: "#4a3022", Shoes: "#5a3a24",
        Hat: "#4d463e", Suspenders: true);

    public static readonly CharacterLook Trabajador = new("trabajador", "El Trabajador",
        Skin: "#b98466", Hair: "#2a2018", Shirt: "#2f3c44", Pants: "#3a3028", Top: "#4a3626", Accent: "#3c3a2a", Shoes: "#5a3a24",
        Hat: "#5a4a3a", Jacket: true, Mustache: true);

    public static readonly CharacterLook Asociado = new("asociado", "El Asociado",
        Skin: "#c49474", Hair: "#141010", Shirt: "#e2d6c2", Pants: "#3a2f26", Top: "#2a2826", Accent: "#4f5048", Shoes: "#1a1512",
        Hat: "#3c3a36", Vest: true);

    public static readonly IReadOnlyList<CharacterLook> All = [Ejecutivo, Operativo, Trabajador, Asociado];

    public static CharacterLook Find(string? code) => All.FirstOrDefault(l => l.Code == code) ?? Trabajador;

    public static bool IsValid(string? code) => All.Any(l => l.Code == code);

    /// <summary>Aspecto para invitados (sin cuenta): fijo según el id de red.</summary>
    public static CharacterLook ForGuest(long peerId) => All[(int)(Math.Abs(peerId) % All.Count)];
}
