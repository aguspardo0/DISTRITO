using System.Numerics;

namespace Distrito.Server.Core.World;

/// <summary>
/// Un lote de la Calle Principal: de <see cref="MinX"/> a <see cref="MaxX"/>, del lado norte o sur. <see cref="Kind"/>:
/// «interior» (se entra: la puerta la pone el interior con su código), «club», «casino», «fachada» (solo se ve) o
/// «plaza» (sin edificio).
/// </summary>
public sealed record StreetLot(string Code, string Sign, float MinX, float MaxX, bool North, float Height, int Tone, string Kind)
{
    public float Width => MaxX - MinX;
    public float CenterX => (MinX + MaxX) / 2;

    /// <summary>Planta del edificio: el frente sobre la línea de vereda y el fondo antes del pasaje de servicio.</summary>
    public MapBuilding Plan => new(CenterX, (North ? -1 : 1) * (MainStreet.Front + MainStreet.LotDepth / 2), Width, MainStreet.LotDepth);

    /// <summary>Hacia dónde mira el frente (hacia la calle).</summary>
    public Vector2 Facing => new(0, North ? 1 : -1);

    /// <summary>Centro del frente, sobre la línea de edificación.</summary>
    public Vector2 FrontCenter => new(CenterX, North ? -MainStreet.Front : MainStreet.Front);
}

/// <summary>
/// La Calle Principal (pedido del equipo, 2026-11-09): «que cuadre con la ciudad, que los negocios estén más separados
/// y que tenga una lógica». Es la calle comercial de un pueblo de EE.UU. de los 50, sobre el eje z = 0:
///
///   Av. Oeste (Moretti) ─┬─ calzada de 12 m con veredas de 5 m ─┬─ x = 114 (Centro) ── Avenida del Puerto ── Los Muelles
///                        │ los negocios a los dos lados, cada uno en su lote, con pasajes de 3 a 4 m entre lotes
///                 Av. Norte (Valenti) y Av. Sur (Caruso) cruzan la calle por sus huecos
///
/// Cada familia tiene su local en la boca de su avenida: el Galpón Moretti donde entra la Avenida Oeste, la Oficina
/// Valenti en la esquina de la Avenida Norte y el Taller Caruso en la de la Avenida Sur (las puertas de los dos últimos
/// dan a su avenida). En la esquina de la Avenida Sur queda La Esquina, una plazoleta con el puesto de café y el
/// kiosco; más al este, el casino, el club de jazz y los comercios.
/// </summary>
public static class MainStreet
{
    /// <summary>Media calzada (12 m de calzada).</summary>
    public const float RoadHalf = 6f;
    /// <summary>Línea de edificación: a 11 m del eje (veredas de 5 m).</summary>
    public const float Front = 11f;
    /// <summary>Fondo de los lotes (16 m); detrás queda un pasaje de servicio de 3 m hasta el borde.</summary>
    public const float LotDepth = 16f;

    public static readonly IReadOnlyList<StreetLot> Lots =
    [
        // Lado norte (de oeste a este).
        new("galpon-moretti", "GALPÓN MORETTI", -28, -8, true, 7f, 0, "interior"),
        new("botica", "BOTICA FERRI", -4, 8, true, 8f, 3, "interior"),
        new("oficina-valenti", "OFICINA VALENTI", 11, 19, true, 11f, 1, "interior"),
        // (19…31: la Avenida Norte cruza hacia el Barrio Valenti)
        new("sastreria", "SASTRERÍA BIANCHI", 31, 45, true, 9.5f, 2, "interior"),
        new("ramos-generales", "RAMOS GENERALES LA ESPERANZA", 49, 65, true, 8f, 5, "interior"),
        new("hotel-roma", "HOTEL ROMA", 69, 81, true, 14.5f, 1, "fachada"),
        new("club-jazz", "CLUB DE JAZZ", 85, 103, true, 9f, 2, "club"),
        // Lado sur.
        new("diner", "DINER LA ESTRELLA", -28, -12, false, 6f, 3, "interior"),
        new("taller-caruso", "TALLER CARUSO", -8, 7.5f, false, 7f, 4, "interior"),
        // (7,5…20: la Avenida Sur cruza hacia el Barrio Caruso)
        new("la-esquina", "LA ESQUINA", 20, 36, false, 0f, 0, "plaza"),
        new("casino", "EL TRÉBOL", 38, 56, false, 7f, 0, "casino"),
        new("barberia", "BARBERÍA", 60, 72, false, 8f, 0, "fachada"),
        new("ferreteria", "FERRETERÍA", 76, 89, false, 10.5f, 5, "fachada"),
        new("correo", "CORREO", 93, 103, false, 12f, 1, "fachada"),
    ];

    public static StreetLot Lot(string code) => Lots.First(l => l.Code == code);

    /// <summary>Edificios de la calle (todos menos la plazoleta). El casino lo construye su propio armador.</summary>
    public static readonly IReadOnlyList<StreetLot> Buildings = Lots.Where(l => l.Kind != "plaza").ToList();

    /// <summary>Faroles: cada 12 m por el borde de las dos veredas.</summary>
    public static IEnumerable<Vector2> Lamps()
    {
        for (var x = CityLayout.Market.MinX + 4; x < CityLayout.Market.MaxX - 2; x += 12)
        {
            yield return new Vector2(x, -(RoadHalf + 0.5f));
            yield return new Vector2(x, RoadHalf + 0.5f);
        }
    }
}
