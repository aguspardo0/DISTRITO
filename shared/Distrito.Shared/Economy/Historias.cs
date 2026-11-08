namespace Distrito.Shared.Economy;

/// <summary>Un capítulo de una historia: un encargo con texto propio que da el capo.</summary>
/// <see cref="Family"/>: quién paga este capítulo (si no, la familia de la historia).
public sealed record StoryStep(string Kind, string Title, string Detail, string? Item, int Quantity, string? Target, int Goal,
    long RewardCents, int Xp, string Closing, string? Family = null);

/// <summary>La historia de una familia: cuatro capítulos y un premio único al final.</summary>
/// <remarks>
/// Las misiones del café (2026-11-08) son una historia más: <see cref="Key"/> «cafe», la da <see cref="Giver"/> (Don Aldo,
/// en la Esquina del café) solo a quien tiene un puesto, cada capítulo lo paga la familia que lo encarga y suma fama al
/// puesto (<see cref="FamePerStep"/>).
/// </remarks>
public sealed record FamilyStory(string Family, string Name, string Intro, string Ending, string Perk, IReadOnlyList<StoryStep> Steps,
    string? Key = null, string? Giver = null, bool NeedsCoffeeStand = false, double FamePerStep = 0)
{
    /// <summary>Con qué se guarda la historia (la familia, o «cafe»).</summary>
    public string StoryKey => Key ?? Family;
}

/// <summary>
/// Historias de las tres familias (propuesta aprobada por el equipo, 2026-11-01): las da el capo, que solo recibe desde
/// 50 de reputación. Cuatro capítulos cada una (~$150 y 800 de experiencia en total) y un premio único: 5 % menos en el
/// Almacén Moretti, 10 % menos en los vehículos del Taller Caruso y la mesa VIP de El Trébol sin esperar al rango 6.
/// Cada capítulo usa un tipo de encargo que ya existe, así que se juega igual (seguimiento, marcadores, libreta).
/// </summary>
public static class FamilyStories
{
    public const int MorettiDiscountPercent = 5;
    public const int CarusoVehicleDiscountPercent = 10;
    public const int ReputationPerStep = 5;
    public static readonly TimeSpan ActiveFor = TimeSpan.FromMinutes(90);

    public static readonly FamilyStory Moretti = new(EconomyCatalog.Moretti, "Mercadería que no llega",
        "Don Moretti cierra la puerta. «Desde hace un mes, la mitad de lo que mando al Mercado no llega. Alguien me está robando "
        + "y en esta familia eso no se perdona. Vos sos nuevo: nadie sospecha de vos. Averiguá quién es.»",
        "Don Moretti sirve dos copas de grappa. «Los Caruso creyeron que un viejo no se daba cuenta. Ahora saben. Desde hoy "
        + "sos de la familia: en el Almacén se te cobra como a un Moretti.»",
        $"{MorettiDiscountPercent} % menos en todo lo que compres en el Almacén Moretti, para siempre",
        [
            new(EncargoKinds.Vigilar, "Mercadería que no llega (1/4): ojos en el taller",
                "«Los camiones salen llenos y llegan vacíos. Andá al Taller Caruso y mirá quién entra y quién sale. Dos minutos, sin que te vean.»",
                null, 0, EconomyCatalog.WorkshopNpc, 4, 3_000, 150,
                "«¿Cajones con mi sello en el taller de los Caruso? Lo sabía. Esos sucios.»"),
            new(EncargoKinds.Cobro, "Mercadería que no llega (2/4): la cuenta",
                "«Lo que se llevaron, lo pagan. Andá al Taller Caruso y cobrá la mercadería robada. Que entiendan que sabemos.»",
                null, 0, EconomyCatalog.WorkshopNpc, 1, 3_500, 200,
                "«Pagaron sin chistar. Eso quiere decir que hay más. Mucho más.»"),
            new(EncargoKinds.Asalto, "Mercadería que no llega (3/4): ojo por ojo",
                "«Pagar no alcanza. Vaciales la caja del taller a punta de pistola. Que los Caruso sientan lo que es perder.»",
                null, 0, EconomyCatalog.WorkshopNpc, 1, 4_000, 200,
                "«Me contaron que el viejo Caruso rompió una silla de la bronca. Bien.»"),
            new(EncargoKinds.Paquete, "Mercadería que no llega (4/4): lo que es nuestro",
                "«Mi gente encontró adónde llevaban lo robado: un barco en Los Muelles. Gaetano te lo entrega. Traelo a la Villa, entero.»",
                null, 0, EncargoRules.DocksContact, 1, 4_500, 250,
                "«Todo en orden. Y vos, ahora, también.»"),
        ]);

    public static readonly FamilyStory Valenti = new(EconomyCatalog.Valenti, "Números rojos",
        "Don Valenti no levanta la vista de los libros. «Alguien le vende a los Moretti los números del casino. Cada jugada que "
        + "pierdo, ellos la saben antes. Los números no mienten, la gente sí. Encontrame al que miente.»",
        "Don Valenti cierra el libro mayor. «Las cuentas cierran. Usted también tiene un lugar en la mesa grande de El Trébol: "
        + "la casa lo invita.»",
        "La mesa VIP de El Trébol (apuestas hasta $200), sin esperar a que tu cuadrilla llegue al rango 6",
        [
            new(EncargoKinds.Recado, "Números rojos (1/4): una oferta",
                "«Antes de buscar al traidor, aseguremos a los amigos. Llévele este sobre al Club de jazz: les ofrecemos protección. No lo abra.»",
                null, 0, EconomyCatalog.JazzClubNpc, 1, 3_000, 150,
                "«Aceptaron. En el club se oye todo; y lo que se oye, ahora es nuestro.»"),
            new(EncargoKinds.Vigilar, "Números rojos (2/4): la filtración",
                "«El dueño del club dice que nuestro croupier va seguido al Almacén Moretti. Vigile el almacén y confírmelo.»",
                null, 0, EconomyCatalog.DepositNpc, 4, 3_500, 200,
                "«Entró con un sobre y salió sin él. Ya sabemos quién es. Ahora, que los Moretti paguen el favor.»"),
            new(EncargoKinds.Asalto, "Números rojos (3/4): la cuenta de los Moretti",
                "«Los Moretti compraron mis números con plata de su reparto. Asalte el camión de los Moretti cuando salga: es un ajuste contable.»",
                null, 0, EncargoRules.Truck, 1, 4_000, 200,
                "«Contabilidad hecha. El croupier ya no trabaja acá… ni en ningún lado.»"),
            new(EncargoKinds.Entrega, "Números rojos (4/4): la gala",
                "«Vamos a celebrar que las cuentas cierran. Para la gala del Palazzo hacen falta seis cajones de bebidas. Que no se note de dónde salen.»",
                EconomyCatalog.DrinksCrateItem, 6, null, 6, 4_500, 250,
                "«Salud. Por los números que cierran y por la gente que no miente.»"),
        ]);

    public static readonly FamilyStory Caruso = new(EconomyCatalog.Caruso, "Hierro y sudor",
        "Don Caruso tiene las manos negras de grasa. «Los Valenti les cobran ‘protección’ a mis obreros. A MIS obreros, en MI barrio. "
        + "Yo no tengo abogados ni contadores: tengo gente que trabaja. ¿Vos trabajás?»",
        "Don Caruso te da la mano, y es la primera vez que lo ves sonreír. «Acá no regalamos nada, pero a los nuestros no se les "
        + "cobra de más. Cualquier fierro del taller, para vos, diez por ciento menos.»",
        $"{CarusoVehicleDiscountPercent} % menos en los vehículos del Taller Caruso",
        [
            new(EncargoKinds.Paquete, "Hierro y sudor (1/4): repuestos sin papeles",
                "«Para pelear primero hay que trabajar. Gaetano tiene repuestos que llegaron sin papeles. Traémelos sin que nadie pregunte.»",
                null, 0, EncargoRules.DocksContact, 1, 3_000, 150,
                "«Buen fierro. Con esto los muchachos tienen trabajo un mes.»"),
            new(EncargoKinds.Entrega, "Hierro y sudor (2/4): el taller no para",
                "«El taller necesita repuestos para cumplir con los pedidos. Seis cajas, compradas en el Almacén. Sí, a los Moretti: el enemigo de mi enemigo…»",
                EconomyCatalog.SparePartsItem, 6, null, 6, 3_500, 200,
                "«Las máquinas andan. Ahora vamos por lo que nos sacaron.»"),
            new(EncargoKinds.Cobro, "Hierro y sudor (3/4): la plata de los obreros",
                "«Andá a la Oficina Valenti y recuperá lo que les cobraron a mis obreros. Cada centavo. Y mirales la cara cuando paguen.»",
                null, 0, EconomyCatalog.ValentiOfficeNpc, 1, 4_000, 200,
                "«Cada centavo de vuelta a quien lo sudó. Pero esos de traje van a volver.»"),
            new(EncargoKinds.Asalto, "Hierro y sudor (4/4): que no vuelvan",
                "«El cobrador de los Valenti sigue haciendo la ronda como si nada. Asaltalo. Que en todo Bellmont se sepa que al sur no se viene a cobrar.»",
                null, 0, EncargoRules.Collector, 1, 4_500, 250,
                "«Ahora sí. El barrio respira.»"),
        ]);

    public const string CafeKey = "cafe";
    public const double CafeFamePerStep = 60;

    /// <summary>
    /// Misiones del café (aprobado 2026-11-08: plata y fama del puesto). Don Aldo se las da a los que ya tienen su puesto.
    /// Pagan entre $5 y $12 cada una (lo paga la familia que encarga) y +60 de fama al puesto.
    /// </summary>
    public static readonly FamilyStory Cafe = new(EconomyCatalog.Valenti, "El café de Don Aldo",
        "Don Aldo se seca las manos en el delantal. «Así que ahora tenés tu puesto. Bien: el café es un oficio, pero en Bellmont también es "
        + "política. Los que mandan toman café, y el que se los sirve se entera de todo. Tengo unos trabajos para alguien de confianza.»",
        "Don Aldo te sirve una taza y no te la cobra. «Ya sos del gremio. En el Mercado se corre la voz: el que quiere buen café, "
        + "sabe dónde está tu puesto.»",
        "fama para tu puesto (+60 por capítulo) y la confianza del gremio",
        [
            new(EncargoKinds.Paquete, "El café de Don Aldo (1/4): granos del puerto",
                "«Los Moretti me consiguen granos que llegan por barco, de los buenos, sin pasar por la aduana. Gaetano los tiene en Los Muelles. Traémelos.»",
                null, 0, EncargoRules.DocksContact, 1, 500, 100,
                "«Huelen a Brasil. Con esto la urna canta.»", EconomyCatalog.Moretti),
            new(EncargoKinds.Entrega, "El café de Don Aldo (2/4): la reunión de los Valenti",
                "«Los Valenti tienen reunión en la oficina y quieren café de verdad, no esa agua de La Perla. Conseguime dos bolsas en el Almacén; yo se las mando.»",
                EconomyCatalog.CoffeeBagItem, 2, null, 2, 1_200, 150,
                "«Vittorio dijo que nunca tomó un café así. Y Vittorio no dice esas cosas.»", EconomyCatalog.Valenti),
            new(EncargoKinds.Recado, "El café de Don Aldo (3/4): noches de jazz",
                "«En el club quieren servir café en las noches de jazz. Llevales este sobre con mi propuesta. Si los Valenti preguntan, es un negocio limpio.»",
                null, 0, EconomyCatalog.JazzClubNpc, 1, 800, 150,
                "«Aceptaron. Los músicos toman más café que whisky, aunque no lo parezca.»", EconomyCatalog.Valenti),
            new(EncargoKinds.Vigilar, "El café de Don Aldo (4/4): ¿quién se robó la urna?",
                "«Anoche me robaron la urna vieja. El diariero del kiosco ve todo lo que pasa en la calle: quedate cerca un par de minutos y escuchá lo que se dice.»",
                null, 0, Distrito.Shared.Economy.MarketInfoRules.KioskNpc, 4, 1_000, 200,
                "«¿Los de La Perla? Me lo imaginaba. Dejalo: la mejor venganza es servir mejor café que ellos.»", EconomyCatalog.Caruso),
        ],
        Key: CafeKey, Giver: EconomyCatalog.CoffeeCornerNpc, NeedsCoffeeStand: true, FamePerStep: CafeFamePerStep);

    /// <summary>Las historias de las familias (las da el capo).</summary>
    public static readonly IReadOnlyList<FamilyStory> All = [Moretti, Valenti, Caruso];

    /// <summary>Todas las historias: las de las familias y las misiones del café.</summary>
    public static readonly IReadOnlyList<FamilyStory> Every = [.. All, Cafe];

    public static FamilyStory Of(string key) => Every.First(s => s.StoryKey == key);

    /// <summary>Id estable de la oferta de un capítulo (cambia con cada intento, por si se abandonó o venció el anterior).</summary>
    public static Guid OfferId(Guid characterId, string family, int step, int attempt) =>
        EncargoRules.StableId(characterId, $"historia-{family}-{step}", attempt);

    /// <summary>El capítulo como encargo: lo da (y lo cobra) el capo de la familia.</summary>
    public static EncargoGenerator.Draft ToDraft(Guid id, string key, int step)
    {
        var story = Of(key);
        var s = story.Steps[step];
        return new EncargoGenerator.Draft(id, s.Kind, story.Giver ?? FamilyPeople.BossOf(story.Family), s.Family ?? story.Family, s.Title, s.Detail,
            s.Item, s.Quantity, s.Target, s.Goal, 0, s.RewardCents, s.Xp, ReputationPerStep, false);
    }

    /// <summary>De qué historia es un capítulo (por su título), si es de alguna.</summary>
    public static FamilyStory? OfTitle(string title) => Every.FirstOrDefault(s => s.Steps.Any(x => x.Title == title));
}
