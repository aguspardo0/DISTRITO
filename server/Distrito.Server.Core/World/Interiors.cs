using System.Numerics;

namespace Distrito.Server.Core.World;

// Interiores (pedido del equipo, 2026-11-02). Desde el 2026-11-09 cada salón está adentro de su edificio de la Calle
// Principal: la puerta es un objeto con física que se abre y se cierra con E, y se entra caminando (sin pasar a otro
// lugar ni pantalla de carga). Antes eran salones armados aparte, lejos de la ciudad. Primero el Mercado: mayoristas
// en galpones y tiendas minoristas. El aspecto es provisorio (greybox): cada mueble es un «lugar» con un tipo; si existe
// res://assets/interiores/<tipo>.glb se usa ese modelo, si no, la forma provisoria. Ver docs/INTERIORES.md.

/// <summary>Mueble fijo (con colisión). Coordenadas locales: (0,0) es el centro del salón; la puerta está en z = +Depth/2.</summary>
public sealed record InteriorItem(string Type, float X, float Z, float Rotation = 0f, string? Label = null);

/// <summary>Objeto con física compartida (cajón, barril, silla…): lo simula el servidor y lo ven todos igual.</summary>
public sealed record PhysicsProp(string Type, float X, float Z, float Y = 0f, float Rotation = 0f);

public sealed record InteriorPlan(
    string Code, string Name, string Kind, Vector2 Door, Vector2 Outward, int Slot,
    float Width, float Depth, float Height, string Floor, string Wall, float DoorX,
    IReadOnlyList<InteriorItem> Items, IReadOnlyList<PhysicsProp> Props)
{
    /// <summary>Eje x del salón en el mundo (a lo largo de la pared de la puerta).</summary>
    public Vector2 Right => new(Outward.Y, -Outward.X);

    /// <summary>
    /// Centro del salón en el mundo: adentro de su edificio de la Calle Principal, con la pared de la puerta sobre la línea
    /// de edificación (desde el 2026-11-09 los salones están en su lugar: se entra caminando, sin pasar a otro lado).
    /// </summary>
    public Vector2 Origin => Door - Outward * (Depth / 2 + InteriorLayout.WallThickness) - Right * DoorX;

    /// <summary>Giro del salón (yaw): con 0 la puerta mira a +z.</summary>
    public float Yaw => MathF.Atan2(Outward.X, Outward.Y);

    public Vector2 ToWorld(float x, float z) => Origin + Right * x + Outward * z;

    /// <summary>¿Está adentro del salón (sin contar las paredes)?</summary>
    public bool Contains(float x, float z)
    {
        var d = new Vector2(x, z) - Origin;
        return MathF.Abs(Vector2.Dot(d, Right)) <= Width / 2 + 0.05f && MathF.Abs(Vector2.Dot(d, Outward)) <= Depth / 2 + 0.05f;
    }

    /// <summary>La puerta, del lado de adentro.</summary>
    public Vector2 InsideDoor => ToWorld(DoorX, Depth / 2);

    /// <summary>Un paso adentro de la puerta.</summary>
    public Vector2 EntryPoint => ToWorld(DoorX, Depth / 2 - 2.2f);

    /// <summary>Un paso afuera, en la vereda.</summary>
    public Vector2 ExitPoint => Door + Outward * 1.6f;

    /// <summary>Hacia dónde mira quien entra (yaw: con 0 se mira a −Z), hacia el fondo del salón.</summary>
    public float EntryYaw => MathF.Atan2(Outward.X, Outward.Y);

    /// <summary>Hacia dónde mira quien sale (de espaldas a la puerta).</summary>
    public float ExitYaw => MathF.Atan2(-Outward.X, -Outward.Y);

    /// <summary>Ancho del hueco de la puerta (los galpones tienen portón).</summary>
    public float DoorWidth => Kind == InteriorLayout.Wholesale ? 2.6f : 1.6f;
}

public static class InteriorLayout
{
    /// <summary>Espesor de las paredes de los locales (el salón queda adentro de ellas).</summary>
    public const float WallThickness = 0.3f;

    /// <summary>Distancia a la puerta para abrirla o cerrarla con E.</summary>
    public const float DoorRange = 2.5f;

    public const string Wholesale = "mayorista", Retail = "minorista", Workshop = "taller", Office = "oficina", Bar = "bar";

    // Puestos de atención adentro (los mismos NPC que afuera atienden también en el mostrador interior).
    public static Vector2 CounterOf(string interior) => interior switch
    {
        "galpon-moretti" => Find("galpon-moretti")!.ToWorld(-8.5f, 4.6f),
        "taller-caruso" => Find("taller-caruso")!.ToWorld(-5.5f, 3.4f),
        "oficina-valenti" => Find("oficina-valenti")!.ToWorld(0f, -0.8f),
        "ramos-generales" => Find("ramos-generales")!.ToWorld(3.5f, -1.6f),
        "botica" => Find("botica")!.ToWorld(0f, -1.4f),
        "sastreria" => Find("sastreria")!.ToWorld(-2.5f, -1.8f),
        "diner" => Find("diner")!.ToWorld(-1f, -2.6f),
        _ => throw new ArgumentException(interior),
    };

    public static readonly IReadOnlyList<InteriorPlan> All =
    [
        // Galpón mayorista de los Moretti: oficina con mostrador a la entrada, tres filas de estanterías altas con la
        // mercadería real, la zona de pedidos listos y el portón de carga al fondo.
        new("galpon-moretti", "Galpón Moretti", Wholesale, new(-25.4f, -11f), new(0, 1), 0, 26f, 18f, 7f, "hormigon", "ladrillo", -9f,
        [
            new("mostrador", -8.5f, 3.6f), new("caja-registradora", -9.6f, 3.6f), new("escritorio", -11.2f, 6.2f, 90), new("archivo", -12.4f, 1.6f, 90),
            new("cartel", -9f, 8.85f, 180, "OFICINA · PEDIDOS POR MAYOR"),
            new("estante-alto", -2f, -1.5f), new("estante-alto", 4f, -1.5f), new("estante-alto", 10f, -1.5f),
            new("estante-alto", -2f, -5.5f), new("estante-alto", 4f, -5.5f), new("estante-alto", 10f, -5.5f),
            new("stock-estantes", -4f, 3.2f), new("stock-pedidos", 3f, 4.5f),
            new("porton", 2f, -8.9f), new("pallet", -6f, -7.5f), new("pallet", 9f, -7.5f), new("carretilla", 6.5f, -7.4f, 30),
            new("lampara-colgante", -4f, 0f), new("lampara-colgante", 6f, 0f), new("lampara-colgante", -9f, 5f),
        ],
        [
            new("cajon", -6f, -7.5f, 0.15f), new("cajon", -6f, -7.5f, 0.75f), new("cajon", -5.3f, -6.6f, 0.15f), new("barril", 12f, -7.8f),
            new("barril", 11.2f, -7.6f), new("bolsa", 8.2f, -7.2f, 0.15f), new("bolsa", 9.4f, -7.5f, 0.15f), new("caja", 1f, 6f), new("caja", 1.6f, 6.6f),
        ]),

        // Taller Caruso: un auto en el elevador, banco de trabajo, pared de herramientas, mostrador y la puerta del fondo.
        new("taller-caruso", "Taller Caruso", Workshop, new(2.2f, 11f), new(0, -1), 1, 18f, 14f, 6f, "hormigon", "chapa", -4f,
        [
            new("mostrador", -5.5f, 2.4f), new("caja-registradora", -6.4f, 2.4f), new("elevador-auto", 3.5f, -1.5f),
            new("banco-trabajo", 7.6f, 3.5f, 90), new("pared-herramientas", 8.9f, -2f, 270), new("puerta-trasera", -6f, -6.9f, 0, "SOLO PERSONAL"),
            new("cartel", -4f, 6.85f, 180, "TALLER CARUSO · REPARACIONES Y VENTAS"), new("lampara-colgante", 3.5f, -1.5f), new("lampara-colgante", -5f, 2f),
        ],
        [
            new("neumatico", 7.5f, -5.5f), new("neumatico", 7.5f, -5.5f, 0.3f), new("neumatico", 6.6f, -5.6f), new("barril", -7.8f, -5f),
            new("barril", -7.6f, -4.1f), new("cajon", 1f, 5.4f, 0.15f),
        ]),

        // Oficina Valenti: recepción con mostrador, sillones de espera, archivos y el despacho del fondo.
        new("oficina-valenti", "Oficina Valenti", Office, new(12.7f, -11f), new(0, 1), 2, 12f, 9f, 4f, "parquet", "empapelado", 0f,
        [
            new("mostrador", 0f, -1.8f), new("escritorio", -3.5f, -3f), new("archivo", -5.4f, -1f, 90), new("archivo", -5.4f, 0.2f, 90),
            new("sillon", 3.8f, 2.2f, 270), new("sillon", 3.8f, 0.8f, 270), new("planta", 5.3f, 3.8f), new("caja-fuerte", 5.2f, -3.6f, 270),
            new("cuadro", 0f, -4.45f, 0, "Don Valenti"), new("cartel", 0f, 4.35f, 180, "OFICINA VALENTI · LICENCIAS"), new("lampara-colgante", 0f, 0f),
        ],
        []),

        // Ramos generales: estanterías en las paredes, mostrador con balanza y caja, heladera, barriles y bolsas.
        new("ramos-generales", "Ramos generales La Esperanza", Retail, new(51.8f, -11f), new(0, 1), 3, 13f, 9f, 4.5f, "baldosa", "madera", 0f,
        [
            new("mostrador", 3.5f, -0.6f), new("balanza", 2.6f, -0.6f), new("caja-registradora", 4.6f, -0.6f), new("heladera", -5.8f, -3f, 90),
            new("estante", -3f, -4.1f), new("estante", 0f, -4.1f), new("estante", 3f, -4.1f), new("estante", -6.1f, 1f, 90),
            new("cartel", 0f, 4.35f, 180, "RAMOS GENERALES LA ESPERANZA"), new("lampara-colgante", -2f, 0f), new("lampara-colgante", 3f, 0f),
        ],
        [new("barril", -4.5f, 2.6f), new("barril", -3.6f, 2.9f), new("bolsa", 5.5f, 3f, 0.15f), new("bolsa", 5.8f, 2.3f, 0.15f), new("caja", -1f, 3.4f)]),

        // Botica: estanterías de frascos, mostrador con balanza de precisión, vidriera y la trastienda.
        new("botica", "Botica Ferri", Retail, new(66.8f, -11f), new(0, 1), 4, 9f, 7f, 4f, "baldosa", "azulejo", 0f,
        [
            new("mostrador", 0f, -0.6f), new("balanza", -0.8f, -0.6f), new("estante-frascos", -2.5f, -3.1f), new("estante-frascos", 2.5f, -3.1f),
            new("vidriera", -3.8f, 1.6f, 90), new("puerta-trasera", 4.1f, -2f, 270, "LABORATORIO"),
            new("cartel", 0f, 3.35f, 180, "BOTICA FERRI · RECETAS"), new("lampara-colgante", 0f, 0f),
        ],
        [new("caja", 3f, 2.4f)]),

        // Sastrería: maniquíes, percheros, espejo, mesa de corte, probador y mostrador.
        new("sastreria", "Sastrería Bianchi", Retail, new(36.3f, -11f), new(0, 1), 5, 10f, 8f, 4f, "parquet", "empapelado", 0f,
        [
            new("mostrador", -2.5f, -0.8f), new("mesa-corte", 2f, -2.5f), new("maniqui", -4f, 2.5f), new("maniqui", -2.8f, 2.8f),
            new("perchero", 4.2f, 1.5f, 90), new("perchero", 4.2f, -0.5f, 90), new("espejo", -4.85f, -1.5f, 90), new("probador", 3.8f, -3.2f),
            new("cartel", 0f, 3.85f, 180, "SASTRERÍA BIANCHI · TRAJES A MEDIDA"), new("lampara-colgante", 0f, 0f),
        ],
        [new("caja", -1f, 3f)]),

        // Diner: barra larga con taburetes, reservados contra la ventana, rocola y dos mesas con sillas sueltas.
        new("diner", "Diner La Estrella", Bar, new(-21.2f, 11f), new(0, -1), 6, 15f, 9f, 4f, "damero", "cromado", 0f,
        [
            new("barra", -1f, -3.4f), new("caja-registradora", 3.5f, -3.4f), new("taburete", -5f, -2.3f), new("taburete", -3.5f, -2.3f),
            new("taburete", -2f, -2.3f), new("taburete", -0.5f, -2.3f), new("taburete", 1f, -2.3f), new("taburete", 2.5f, -2.3f),
            new("reservado", -6.3f, 1.5f, 90), new("reservado", -6.3f, -0.3f, 90), new("rocola", 6.9f, 2.5f, 270),
            new("mesa", 2.5f, 1.8f), new("mesa", 5f, 0f), new("cartel", 0f, 4.35f, 180, "DINER LA ESTRELLA · ABIERTO"),
            new("lampara-colgante", -3f, -1f), new("lampara-colgante", 3f, 1f),
        ],
        [
            new("silla", 2.5f, 1f), new("silla", 2.5f, 2.6f, 0, 180), new("silla", 5f, -0.8f), new("silla", 5f, 0.8f, 0, 180),
            new("botella", -3f, -3.4f, 1.2f), new("botella", 0.5f, -3.4f, 1.2f),
        ]),
    ];

    /// <summary>Cajones, barriles y basura en las veredas y pasajes de la Calle Principal (física compartida), lejos de las puertas y de la calzada.</summary>
    public static readonly IReadOnlyList<PhysicsProp> MarketProps =
    [
        new("cajon", -12f, -10.2f, 0.15f), new("cajon", -12f, -10.2f, 0.75f), new("cajon", -10.9f, -10.2f, 0.15f), new("barril", -6f, -10.3f),
        new("tacho", 0.5f, -12.5f), new("tacho", 43.3f, -12f), new("barril", -12.2f, 12.5f), new("barril", -12.3f, 13.4f),
        new("neumatico", 9f, 25f), new("cajon", -15f, 10.3f, 0.15f), new("tacho", -12.3f, 15f), new("caja", 59f, 10.3f),
        new("cajon", 73.3f, -12f, 0.15f), new("tacho", 91f, 21f), new("barril", 83f, -10.3f),
    ];

    public static InteriorPlan? Find(string code) => All.FirstOrDefault(p => p.Code == code);

    /// <summary>El salón en el que está un punto, si hay.</summary>
    public static InteriorPlan? At(float x, float z) => All.FirstOrDefault(p => p.Contains(x, z));

    /// <summary>Puerta cercana (de afuera o de adentro) y de qué lado está el punto.</summary>
    public static (InteriorPlan Plan, bool Inside)? DoorNear(float x, float z)
    {
        var here = new Vector2(x, z);
        foreach (var p in All)
            if (Vector2.Distance(here, p.Door) <= DoorRange || Vector2.Distance(here, p.InsideDoor) <= DoorRange)
                return (p, p.Contains(x, z));
        return null;
    }

    /// <summary>Para el mapa y las zonas: quien está adentro figura en la puerta del edificio.</summary>
    public static Vector2 MapPoint(float x, float z) => At(x, z) is { } plan ? plan.Door : new Vector2(x, z);
}
