using System.Numerics;
using Distrito.Shared.Economy;

namespace Distrito.Server.Core.Npcs;

public sealed record NpcOption(string Id, string Label);

public sealed record NpcDialog(string NpcCode, string Title, string Text, IReadOnlyList<NpcOption> Options);

public enum NpcActionKind { AcceptContract, Buy, Deliver, BuyLicense, Restock, Withdraw, HireHelper, FireHelper, SignPact, Sell, Encargo, MarketBuy }

public sealed record NpcAction(NpcActionKind Kind, string Code, Guid? ContractId = null, int Quantity = 1);

/// <summary>
/// NPC fijo. <see cref="Counters"/>: otros mostradores donde atiende lo mismo (por ejemplo, adentro de su galpón).
/// </summary>
public sealed record NpcDefinition(string Code, string Name, Vector3 Position, IReadOnlyList<Vector3>? Counters = null)
{
    public IEnumerable<Vector3> Positions => Counters is null ? [Position] : Counters.Prepend(Position);

    /// <summary>Dónde figura en el mapa: si atiende adentro de un edificio, en su puerta.</summary>
    public Vector2 MapPosition => World.InteriorLayout.MapPoint(Position.X, Position.Z);
}

/// <summary>
/// Qué ofrece cada NPC según el estado económico del jugador, y qué opciones son válidas.
/// El servidor usa esto para armar el diálogo y para verificar que la opción elegida
/// realmente estaba disponible (el cliente no puede inventar opciones).
/// </summary>
public static class NpcInteractionRules
{
    /// <summary>Distancia máxima (metros) entre el jugador y el NPC para interactuar. La verifica el servidor.</summary>
    public const float InteractionRange = 3.5f;

    /// <summary>Opción que no va al servidor: el cliente abre la ventana de pedidos del proveedor.</summary>
    public const string SupplierOrdersOption = "ui:pedidos";

    /// <summary>Opción que no va al servidor: el cliente abre la ventana de compra de vehículos.</summary>
    public const string VehicleShopOption = "ui:vehiculos";

    /// <summary>Opción que no va al servidor: el cliente abre las mesas del casino.</summary>
    public const string CasinoOption = "ui:casino";

    /// <summary>Opciones que no van al servidor: el cliente abre el diario, el informe de Nino o el libro de cuentas (tecla P).</summary>
    public const string NewspaperOption = "ui:diario";
    public const string InformantOption = "ui:informe";
    public const string LedgerOption = "ui:libro";

    // Los tres de las familias atienden en la vereda y también en el mostrador de adentro (galpón, taller y oficina).
    public static readonly NpcDefinition Deposit = new(EconomyCatalog.DepositNpc, "Almacén Moretti", new Vector3(-8.5f, 0, -8.5f), [Inside("galpon-moretti")]);
    public static readonly NpcDefinition Workshop = new(EconomyCatalog.WorkshopNpc, "Taller Caruso", new Vector3(10f, 0, 3f), [Inside("taller-caruso")]);

    public static readonly NpcDefinition ValentiOffice = new(EconomyCatalog.ValentiOfficeNpc, "Oficina Valenti", new Vector3(-11.5f, 0, -1.5f), [Inside("oficina-valenti")]);

    private static Vector3 Inside(string interior)
    {
        var c = World.InteriorLayout.CounterOf(interior);
        return new Vector3(c.X, 0, c.Y);
    }

    // Tiendas minoristas del Mercado (2026-11-02): atienden adentro. Los productos se activan cuando el equipo los apruebe.
    public const string RamosNpc = "npc:ramos-generales", BoticaNpc = "npc:botica", SastreriaNpc = "npc:sastreria", DinerNpc = "npc:diner";
    public static readonly NpcDefinition Ramos = new(RamosNpc, "Don Pepe (Ramos generales)", Inside("ramos-generales"));
    public static readonly NpcDefinition Botica = new(BoticaNpc, "El boticario Ferri", Inside("botica"));
    public static readonly NpcDefinition Sastreria = new(SastreriaNpc, "Bianchi, el sastre", Inside("sastreria"));
    public static readonly NpcDefinition Diner = new(DinerNpc, "Mabel (Diner La Estrella)", Inside("diner"));
    public static readonly IReadOnlyList<NpcDefinition> Shops = [Ramos, Botica, Sastreria, Diner];
    public static readonly NpcDefinition CoffeeCorner = new(EconomyCatalog.CoffeeCornerNpc, "Esquina del café", new Vector3(-3f, 0, 8f));

    public static readonly NpcDefinition JazzClub = new(EconomyCatalog.JazzClubNpc, "Club de jazz", new Vector3(8f, 0, -7f));

    /// <summary>Casino «El Trébol» de la familia Valenti (aprobado 2026-11-01), en la puerta del local.</summary>
    public static readonly NpcDefinition Casino = new(CasinoRules.Npc, CasinoRules.Name, new Vector3(24f, 0, 15.6f));

    /// <summary>Kiosco de diarios (2026-11-02): vende La Gaceta de Bellmont, en la vereda oeste de la calle principal.</summary>
    public static readonly NpcDefinition Kiosk = new(MarketInfoRules.KioskNpc, MarketInfoRules.KioskName, new Vector3(-18.5f, 0, -5.5f));

    /// <summary>Nino, el informante: en el callejón detrás de los galpones del Mercado.</summary>
    public static readonly NpcDefinition Informant = new(MarketInfoRules.InformantNpc, MarketInfoRules.InformantName, new Vector3(20.5f, 0, -6.5f));

    // Jefes de familia: esperan en la plaza de su barrio, frente al cuartel.
    public static readonly NpcDefinition MorettiBoss = Boss(EconomyCatalog.MorettiBossNpc, World.CityLayout.Moretti);
    public static readonly NpcDefinition ValentiBoss = Boss(EconomyCatalog.ValentiBossNpc, World.CityLayout.Valenti);
    public static readonly NpcDefinition CarusoBoss = Boss(EconomyCatalog.CarusoBossNpc, World.CityLayout.Caruso);

    private static NpcDefinition Boss(string code, World.BarrioPlan b) => new(code, b.Boss, new Vector3(b.BossSpot.X, 0, b.BossSpot.Y));

    public static readonly IReadOnlyList<NpcDefinition> Bosses = [MorettiBoss, ValentiBoss, CarusoBoss];

    /// <summary>La gente de cada familia: uno en la entrada del barrio (calle) y otro en la plaza (negocios).</summary>
    public static readonly IReadOnlyList<NpcDefinition> FamilyContacts = FamilyPeople.Contacts.Select(c =>
    {
        var barrio = World.CityLayout.BarrioOf(c.Family)!;
        var spot = c.Role == "calle" ? barrio.ToWorld(10, 8) : barrio.ToWorld(56, -8);
        return new NpcDefinition(c.Code, c.Name, new Vector3(spot.X, 0, spot.Y));
    }).ToList();

    /// <summary>Gaetano, el marinero de Los Muelles: entrega los paquetes de los encargos.</summary>
    public static readonly NpcDefinition DocksContact = new(EncargoRules.DocksContact, "Gaetano, el marinero", new Vector3(47f, 0, 3.4f));

    public static readonly IReadOnlyList<NpcDefinition> All = [Deposit, Workshop, ValentiOffice, CoffeeCorner, JazzClub, Casino, Kiosk, Informant, .. Shops, .. Bosses, .. FamilyContacts, DocksContact];

    /// <summary>NPC del Mercado (proveedores y comercios), sin la gente de los barrios ni el marinero.</summary>
    public static readonly IReadOnlyList<NpcDefinition> Market = All.Except(Bosses).Except(FamilyContacts).Except([DocksContact]).ToList();

    /// <summary>Nombre para mostrar en textos de encargos («Tano Ricci», «el Taller Caruso»…).</summary>
    public static string DisplayName(string code) => code switch
    {
        EconomyCatalog.DepositNpc => "el Almacén Moretti",
        EconomyCatalog.WorkshopNpc => "el Taller Caruso",
        EconomyCatalog.ValentiOfficeNpc => "la Oficina Valenti",
        EconomyCatalog.CoffeeCornerNpc => "la Esquina del café",
        EconomyCatalog.JazzClubNpc => "el Club de jazz",
        CasinoRules.Npc => "el portero de El Trébol",
        _ => Find(code)?.Name ?? code,
    };

    public static NpcDefinition? Find(string code) => All.FirstOrDefault(n => n.Code == code);

    public static bool InRange(Vector3 player, NpcDefinition npc)
    {
        foreach (var at in npc.Positions)
            if (new Vector2(player.X - at.X, player.Z - at.Z).Length() <= InteractionRange && MathF.Abs(player.Y - at.Y) < 3f)
                return true;
        return false;
    }

    /// <summary>Distancia al mostrador más cercano de ese NPC.</summary>
    public static float DistanceTo(Vector3 player, NpcDefinition npc) =>
        npc.Positions.Min(at => new Vector2(player.X - at.X, player.Z - at.Z).Length());

    private static (string, List<NpcOption>) ShopDialog(string code) => code switch
    {
        RamosNpc => ("Don Pepe acomoda latas en el estante. «Bienvenido a La Esperanza. Estamos terminando de acomodar: en unos días "
                     + "tengo de todo, desde fideos hasta cigarrillos. Pase cuando quiera.»", new List<NpcOption>()),
        BoticaNpc => ("El boticario limpia un frasco a contraluz. «Botica Ferri, para servirle. Vendas, tónicos y remedios… en cuanto llegue "
                      + "el pedido de la droguería. Vuelva pronto.»", new List<NpcOption>()),
        SastreriaNpc => ("Bianchi tiene alfileres en la boca y una cinta métrica al cuello. «Trajes a medida, sombreros, camperas. "
                         + "Todavía estoy cortando las telas de la temporada: dentro de poco lo visto como un señor.»", new List<NpcOption>()),
        _ => ("Mabel seca un vaso detrás de la barra. «¡Bienvenido a La Estrella! La cocina abre pronto: café, sándwiches y la mejor tarta "
              + "de manzana de Bellmont. Mientras tanto, la rocola es gratis.»", new List<NpcOption>()),
    };

    private static bool IsShop(string code) => code is RamosNpc or BoticaNpc or SastreriaNpc or DinerNpc;

    public static NpcDialog DialogFor(string npcCode, EconomySnapshot s, string? prefix = null)
    {
        var npc = Find(npcCode) ?? throw new ArgumentException($"NPC desconocido: {npcCode}", nameof(npcCode));
        var (text, rawOptions) = npcCode switch
        {
            EconomyCatalog.DepositNpc => DepositDialog(s),
            EconomyCatalog.WorkshopNpc => WorkshopDialog(s),
            EconomyCatalog.ValentiOfficeNpc => ValentiDialog(s),
            EconomyCatalog.CoffeeCornerNpc => BusinessDialog(EconomyCatalog.CoffeeStand, s, "Acá se instalan los puestos de café. La licencia la da la Oficina Valenti."),
            CasinoRules.Npc => ("Bienvenido a El Trébol. Ruleta, blackjack y dados: de $1 a $50; la mesa VIP, hasta $200, es para cuadrillas de prestigio. "
                                + "La casa siempre tiene su parte, como en todos lados.", [new NpcOption(CasinoOption, "Entrar a jugar")]),
            EconomyCatalog.JazzClubNpc => BusinessDialog(EconomyCatalog.JazzClub, s,
                "Un local vacío con escenario. Con licencia de la Oficina Valenti puede ser un club de jazz; las bebidas las vende el Almacén Moretti."),
            EconomyCatalog.MorettiBossNpc or EconomyCatalog.ValentiBossNpc or EconomyCatalog.CarusoBossNpc => BossDialog(npcCode, s),
            _ when FamilyPeople.Find(npcCode) is { } contact => ContactDialog(contact, s),
            MarketInfoRules.KioskNpc => KioskDialog(s),
            _ when IsShop(npcCode) => ShopDialog(npcCode),
            MarketInfoRules.InformantNpc => InformantDialog(s),
            EncargoRules.DocksContact => ("Un marinero viejo remienda una red. «Gaetano, para servirle. Acá llegan cosas que no figuran en ningún papel. "
                + "Si alguien de las familias le dijo que pase, tengo algo para usted. Si no, yo no vi nada.»", new List<NpcOption>()),
            _ => ("…", new List<NpcOption>()),
        };
        var options = rawOptions;
        EncargoOptions(npc.Code, s, ref text, options);
        // Venta a NPC: cada local compra lo que vende (60 %, tope por hora); se ofrece lo que llevás encima.
        if (NpcSaleRules.Buyers.TryGetValue(npc.Code, out var buys))
            foreach (var item in s.Inventory.Where(i => buys.Contains(i.ItemCode) && i.Quantity > 0))
            {
                options.Add(new NpcOption($"sell1:{item.ItemCode}", $"Vender 1 × {item.Name} ({NpcSaleRules.PricePercent} % del precio)"));
                if (item.Quantity > 1)
                    options.Add(new NpcOption($"sellall:{item.ItemCode}",
                        $"Vender {Math.Min(item.Quantity, NpcSaleRules.UnitsPerHour)} × {item.Name} (tope {NpcSaleRules.UnitsPerHour} por hora)"));
            }
        // Libro de cuentas: el dueño lo ve en su local.
        if (EconomyCatalog.BusinessTypes.Values.FirstOrDefault(t => t.SiteNpc == npc.Code) is { } site && s.BusinessOf(site.Code) is not null)
            options.Add(new NpcOption(LedgerOption, "Mi negocio: fama, mejoras, precio y cuentas"));
        if (SupplierRules.Catalog.ContainsKey(npc.Code))
            options.Add(new NpcOption(SupplierOrdersOption,
                $"Pedidos por mayor (desde {SupplierRules.MinUnits}; {SupplierRules.VolumeDiscountPercent} % menos desde {SupplierRules.VolumeDiscountFrom})"));
        if (npc.Code == EconomyCatalog.WorkshopNpc)
            options.Add(new NpcOption(VehicleShopOption,
                $"Vehículos ({string.Join(", ", Vehicles.All.Select(v => $"{v.Name.Split(' ')[0].ToLowerInvariant()} {EconomyCatalog.FormatMoney(v.PriceCents)}"))})"));
        LevelContractsDialog(npc.Code, s, ref text, options);
        if (TerritoryRules.ZoneOfNpc(npc.Code) is { } zoneCode && s.Territory?.Zone(zoneCode) is { } zone)
            text += zone.ControllerCrew is { } owner
                ? $"\n\nZona {zone.Name}: la controla «{owner}» ({zone.ControllerPoints} de influencia)."
                : $"\n\nZona {zone.Name}: nadie la controla todavía (hacen falta {TerritoryRules.ControlThreshold} de influencia).";
        if (TerritoryRules.ZoneOfNpc(npc.Code) is { } eventZone && s.Events?.FirstOrDefault(e => e.Zone == eventZone) is { } ev)
            text += ev.IsActive(DateTimeOffset.UtcNow)
                ? $"\n\n¡{ev.Name}! Hasta las {ev.EndsAt.ToOffset(TerritoryRules.ArgentinaOffset):HH:mm}: cada contrato entregado acá da +{ev.PointsPerContract} de influencia."
                : $"\n\nPróximo evento acá: {ev.Name}, a las {ev.StartsAt.ToOffset(TerritoryRules.ArgentinaOffset):HH:mm}.";
        if (FamilyRules.FamilyOfNpc(npc.Code) is { } pactFamily && s.Pacts is not null && FamilyPeople.Find(npc.Code) is null
            && (!Bosses.Contains(npc) || FamilyPeople.CanMeetBoss(s.ReputationWith(pactFamily))))
        {
            if (s.Pacts.FirstOrDefault(p => p.FamilyCode == pactFamily) is { } pact)
                text += pact.Mine
                    ? $"\n\nTenemos un acuerdo con tu gente esta temporada (+{PactRules.ContractBonusPercent} % en nuestros contratos)."
                    : $"\n\nEsta temporada tenemos acuerdo con «{pact.HolderCrew}».";
            else
                options.Add(new NpcOption($"pact:{pactFamily}",
                    $"Firmar acuerdo de temporada ({EconomyCatalog.FormatMoney(PactRules.CostCents)} de la caja; pide {PactRules.RequiredReputation} de reputación sumada de la cuadrilla)"));
        }
        if (FamilyRules.FamilyOfNpc(npc.Code) is { } family && s.Families is not null)
            text += $"\n\nÁnimo de la familia: {FamilyRules.Display(s.MoodOf(family))}.";
        return new NpcDialog(npc.Code, npc.Name, prefix is null ? text : $"{prefix}\n\n{text}", options);
    }

    /// <summary>
    /// Saludo del jefe de familia: cada uno con su carácter y su desprecio por los otros dos (las familias son rivales).
    /// </summary>
    private static (string, List<NpcOption>) BossDialog(string npcCode, EconomySnapshot s)
    {
        var family = FamilyRules.FamilyOfNpc(npcCode)!;
        var rep = s.ReputationWith(family);
        if (!FamilyPeople.CanMeetBoss(rep))
        {
            // El capo no recibe a cualquiera: el guardaespaldas manda a hablar con su gente.
            var people = string.Join(" o ", FamilyPeople.Contacts.Where(c => c.Family == family).Select(c => c.Name));
            return ($"Un guardaespaldas te frena en la escalera. «El jefe no recibe a cualquiera. Si querés que te conozca, "
                + $"ganate el respeto de la familia: hablá con {people}.»\n\n"
                + $"Reputación con la familia: {rep} (el jefe recibe desde {FamilyPeople.BossReputation}).", new List<NpcOption>());
        }
        // La historia de la familia: cómo va (los capítulos se ofrecen abajo, como cualquier encargo).
        var tale = FamilyStories.Of(family);
        var chapter = s.Encargos?.FirstOrDefault(e => e.Story && e.Family == family);
        var storyLine = s.StoryDone(family)
            ? $"\n\n«{tale.Name}» — terminada. Tu premio: {tale.Perk}."
            : chapter is { IsActive: true }
                ? $"\n\n«{tale.Name}»: {chapter.Title}. {chapter.StepText(DisplayName)}"
                : chapter is { IsOffer: true } && chapter.Title == tale.Steps[0].Title
                    ? $"\n\n{tale.Intro}"
                    : "";
        var standing = rep switch
        {
            >= 100 => "Sos de los nuestros. Acá se te respeta.",
            >= 30 => "Te estás ganando un lugar. Seguí así.",
            > -20 => "Todavía no sé si puedo confiar en vos.",
            _ => "Tenés coraje para venir acá después de lo que hiciste. No te des vuelta.",
        };
        var greeting = family switch
        {
            EconomyCatalog.Moretti => "Bienvenido a Villa Moretti. Acá la palabra vale más que cualquier papel. "
                + "Los Valenti creen que todo se compra y los Caruso que todo se arregla a martillazos. Nosotros no olvidamos quién nos ayuda.",
            EconomyCatalog.Valenti => "Pase, siéntese. En el Palazzo los negocios se hacen con calma y con números. "
                + "Los Moretti viven del pasado y los Caruso no saben leer un balance. El futuro de Bellmont es nuestro.",
            _ => "Esto es Talleres Caruso: acá se trabaja con las manos y se cobra lo que se trabaja. "
                + "Los Moretti te sonríen mientras te venden caro y los Valenti te sacan la plata con una lapicera.",
        };
        return ($"{greeting}\n\n{standing} (Reputación con la familia: {rep}.){storyLine}", new List<NpcOption>());
    }

    /// <summary>
    /// Encargos en el diálogo: quien los da los ofrece (con su texto) y en cada lugar aparece el paso que toca ahí
    /// (entregar, cobrar la deuda, buscar el paquete, dejar el sobre, contar lo que viste).
    /// </summary>
    private static void EncargoOptions(string npcCode, EconomySnapshot s, ref string text, List<NpcOption> options)
    {
        foreach (var e in s.ActiveEncargos.Where(e => e.NextNpc == npcCode))
        {
            if (EncargoKinds.IsEvent(e.Kind) && e.Stage == 0)
                continue; // se cumple con lo que pasa en el mundo
            var label = (e.Kind, e.Stage) switch
            {
                (EncargoKinds.Cobro, 0) => $"Cobrar la deuda de la familia {EncargoGenerator.FamilyName(e.Family)}",
                (EncargoKinds.Paquete, 0) => "Buscar el paquete",
                (EncargoKinds.Recado, 0) => "Entregar el sobre sellado",
                (EncargoKinds.Entrega or EncargoKinds.Contrarreloj, 0) => $"Entregar {e.Quantity} × {EncargoRules.ItemName(e.ItemCode)}",
                (EncargoKinds.Reparto, 0) => $"Descargar {e.Quantity} × {EncargoRules.ItemName(e.ItemCode)} del vehículo",
                (EncargoKinds.Cobro, _) => "Dar el sobre de la cobranza",
                (EncargoKinds.Paquete, _) => "Dar el paquete",
                (EncargoKinds.Vigilar, _) => "Contar lo que viste",
                _ => "Avisar que está hecho",
            };
            options.Insert(0, new NpcOption($"encargo:paso:{e.Id}", $"★ {label} — «{e.Title}»"));
        }
        var offers = s.OfferedEncargos.Where(e => e.GiverNpc == npcCode).ToList();
        if (offers.Count == 0)
            return;
        var full = s.ActiveEncargos.Count(e => !e.Story) >= EncargoRules.MaxActive;
        text += "\n\nTrabajo:";
        foreach (var e in offers)
        {
            text += $"\n• {e.Title}: {e.Detail}";
            if (!full || e.Story) // los capítulos de las historias no cuentan para el límite
                options.Add(new NpcOption($"encargo:aceptar:{e.Id}",
                    $"Tomar «{e.Title}» — {EconomyCatalog.FormatMoney(e.RewardCents)}, +{e.Xp} XP, +{e.Reputation} de reputación"));
        }
        if (full)
            text += $"\n(Ya tenés {EncargoRules.MaxActive} encargos en curso: terminá o abandoná uno en la libreta, tecla L.)";
    }

    /// <summary>La gente de la familia: su saludo y cuánto falta para que el capo te reciba.</summary>
    private static (string, List<NpcOption>) ContactDialog(FamilyContact contact, EconomySnapshot s)
    {
        var rep = s.ReputationWith(contact.Family);
        var boss = Find(FamilyPeople.BossOf(contact.Family))!.Name;
        var road = FamilyPeople.CanMeetBoss(rep)
            ? $"{boss} ya sabe quién sos. Te espera en el cuartel."
            : $"Para que {boss} te reciba hace falta {FamilyPeople.BossReputation} de reputación con la familia; tenés {rep}.";
        return ($"{contact.Greeting}\n\n{road}", new List<NpcOption>());
    }

    /// <summary>Traduce la opción elegida a una acción, solo si está disponible ahora. Si no, null.</summary>
    public static NpcAction? Resolve(string npcCode, string optionId, EconomySnapshot s)
    {
        if (Find(npcCode) is null || DialogFor(npcCode, s).Options.All(o => o.Id != optionId))
            return null;

        var parts = optionId.Split(':', 2);
        return parts[0] switch
        {
            "accept" => new NpcAction(NpcActionKind.AcceptContract, parts[1]),
            "buy" => new NpcAction(NpcActionKind.Buy, parts[1]),
            "license" => new NpcAction(NpcActionKind.BuyLicense, parts[1]),
            "restock" => new NpcAction(NpcActionKind.Restock, parts[1]),
            "withdraw" => new NpcAction(NpcActionKind.Withdraw, parts[1]),
            "hire" => new NpcAction(NpcActionKind.HireHelper, parts[1]),
            "fire" => new NpcAction(NpcActionKind.FireHelper, parts[1]),
            "pact" => new NpcAction(NpcActionKind.SignPact, parts[1]),
            "sell1" => new NpcAction(NpcActionKind.Sell, parts[1], Quantity: 1),
            "market" => new NpcAction(NpcActionKind.MarketBuy, parts[1]),
            "sellall" => new NpcAction(NpcActionKind.Sell, parts[1], Quantity: Math.Min(s.QuantityOf(parts[1]), NpcSaleRules.UnitsPerHour)),
            "encargo" when parts[1].Split(':') is [var step, var raw] && Guid.TryParse(raw, out var encargo) =>
                new NpcAction(NpcActionKind.Encargo, step, encargo),
            "deliver" when Guid.TryParse(parts[1], out var id) =>
                new NpcAction(NpcActionKind.Deliver, s.ActiveContracts.First(c => c.Id == id).TemplateCode, id),
            _ => null,
        };
    }

    /// <summary>
    /// Contratos por nivel: en el NPC de origen se ofrecen (o se dice qué falta); en el de destino se entregan.
    /// </summary>
    private static void LevelContractsDialog(string npcCode, EconomySnapshot s, ref string text, List<NpcOption> options)
    {
        var m = EconomyCatalog.FormatMoney;
        foreach (var t in EconomyCatalog.LevelContracts)
        {
            var active = s.ActiveContract(t.Code);
            if (t.SourceNpc == npcCode && active is null)
            {
                var missing = EconomyCatalog.MissingRequirements(t, s);
                var crew = t.MatesNeeded > 0 ? $", con {t.MatesNeeded} de tu cuadrilla" : "";
                var time = t.HasTimeLimit ? $", {t.TimeLimitSeconds / 60} minutos" : "";
                if (missing.Count == 0)
                    options.Add(new NpcOption($"accept:{t.Code}", $"Aceptar: {t.Title} (paga {m(Pay(s, t))}{time})"));
                else
                    text += $"\n\nMás adelante: {t.Title} (pide {string.Join(", ", missing)}{crew}).";
            }
            if (t.DestinationNpc == npcCode && active is not null)
            {
                if (!t.HasGoods || s.QuantityOf(t.ItemCode) >= t.Quantity)
                {
                    var what = t.HasGoods ? $"Entregar {t.Quantity} × {ItemsLabel(t.ItemCode)}" : "Hacer el trabajo";
                    var crew = t.MatesNeeded > 0 ? $", con {t.MatesNeeded} de tu cuadrilla cerca" : "";
                    options.Add(new NpcOption($"deliver:{active.Id}", $"{what}: {t.Title} (cobrar {m(Pay(s, t))}{crew})"));
                }
                else
                {
                    text += $"\n\nPara «{t.Title}» te faltan {t.Quantity - s.QuantityOf(t.ItemCode)} × {ItemsLabel(t.ItemCode)}.";
                }
            }
            if (t.SourceNpc == npcCode && active is not null && t.DestinationNpc != npcCode)
                text += $"\n\nTenés en curso: {t.Title}.";
        }
    }

    private static string ItemsLabel(string item) => item switch
    {
        EconomyCatalog.DrinksCrateItem => "cajones de bebidas",
        EconomyCatalog.CoffeeBagItem => "bolsas de café",
        _ => "cajas de repuestos",
    };

    /// <summary>Precio que cobra la familia del NPC, según su ánimo.</summary>
    private static long Price(EconomySnapshot s, string npcCode, long cents) =>
        FamilyRules.FamilyOfNpc(npcCode) is { } family ? FamilyRules.SellPrice(cents, s.MoodOf(family)) : cents;

    /// <summary>Pago de un contrato según el ánimo de quien paga.</summary>
    private static long Pay(EconomySnapshot s, ContractTemplate t) =>
        FamilyRules.FamilyOfNpc(t.Payer) is { } family ? FamilyRules.ContractPay(t.RewardCents, s.MoodOf(family)) : t.RewardCents;

    private static (string, List<NpcOption>) KioskDialog(EconomySnapshot s)
    {
        var options = new List<NpcOption>();
        string text;
        if (s.NewspaperToday)
        {
            text = "El canillita dobla los diarios. «Ya tiene el de hoy, señor. Mañana a primera hora sale el nuevo.»";
            options.Add(new NpcOption(NewspaperOption, $"Leer {MarketInfoRules.PaperName}"));
        }
        else
        {
            text = $"Un canillita grita los titulares. «¡{MarketInfoRules.PaperName}, diez centavos! Precios del Mercado, policiales y avisos. "
                   + "¡Todo lo que pasó en Bellmont!»\n\nEl titular de hoy está en la pizarra del kiosco.";
            options.Add(new NpcOption($"market:{MarketInfoRules.Newspaper}",
                $"Comprar {MarketInfoRules.PaperName} ({EconomyCatalog.FormatMoney(MarketInfoRules.NewspaperPriceCents)})"));
        }
        return (text, options);
    }

    private static (string, List<NpcOption>) InformantDialog(EconomySnapshot s)
    {
        var options = new List<NpcOption>();
        var now = DateTimeOffset.UtcNow;
        string text;
        if (s.HasInformant(now))
        {
            text = "Nino mira para los dos lados antes de hablar. «Lo que sé, lo sabés. Está todo en tu libreta (P).»";
            options.Add(new NpcOption(InformantOption,
                $"Ver lo que sabe Nino (hasta las {s.InformantUntil!.Value.ToOffset(TerritoryRules.ArgentinaOffset):HH:mm})"));
        }
        else
        {
            text = "Un tipo flaco, de sombrero, fuma apoyado en la pared. «Me dicen Nino. Sé quién tiene mercadería, quién no, "
                   + "qué vende el Taller por la puerta de atrás y cuándo sale cada flete. Cinco dólares la hora, y yo no te vi.»";
            options.Add(new NpcOption($"market:{MarketInfoRules.Informant}",
                $"Pagarle a Nino ({EconomyCatalog.FormatMoney(MarketInfoRules.InformantPriceCents)} por una hora de información)"));
        }
        return (text, options);
    }

    private static NpcOption CoffeeBagOption(EconomySnapshot s) =>
        new($"buy:{EconomyCatalog.CoffeeBagItem}", $"Comprar una bolsa de café ({EconomyCatalog.FormatMoney(Price(s, EconomyCatalog.DepositNpc, EconomyCatalog.CoffeeBagPriceCents))})");

    private static (string, List<NpcOption>) DepositDialog(EconomySnapshot s)
    {
        var (text, options) = DepositContractDialog(s);
        var urgent = EconomyCatalog.UrgentCoffeeOrder;
        if (s.ActiveContract(urgent.Code) is null)
        {
            options.Add(new NpcOption($"accept:{urgent.Code}",
                $"Aceptar: {urgent.Title} (paga {EconomyCatalog.FormatMoney(Pay(s, urgent))}, {urgent.TimeLimitSeconds / 60} minutos)"));
            text += $"\n\nTambién tengo un pedido urgente: {urgent.Quantity} bolsas de café para la Oficina Valenti, " +
                    $"en menos de {urgent.TimeLimitSeconds / 60} minutos. Si llegás tarde, quedo mal y me lo voy a acordar.";
        }
        else
        {
            text += $"\n\n¡Apurate con el pedido! Llevá {urgent.Quantity} bolsas de café a la Oficina Valenti.";
        }
        options.Add(CoffeeBagOption(s));
        options.Add(new($"buy:{EconomyCatalog.DrinksCrateItem}",
            $"Comprar un cajón de bebidas ({EconomyCatalog.FormatMoney(Price(s, EconomyCatalog.DepositNpc, EconomyCatalog.JazzClub.ItemPriceCents))})"));
        return (text, options);
    }

    private static (string, List<NpcOption>) ValentiDialog(EconomySnapshot s)
    {
        var options = new List<NpcOption>();
        var offers = new List<string>();
        foreach (var type in EconomyCatalog.BusinessTypes.Values)
        {
            if (s.BusinessOf(type.Code) is not null)
                continue;
            var license = EconomyCatalog.FormatMoney(Price(s, EconomyCatalog.ValentiOfficeNpc, type.LicenseCents));
            offers.Add($"{type.Name.ToLowerInvariant()} sale {license} y {EconomyCatalog.FormatMoney(type.RentCents)} de alquiler por ciclo con ventas");
            options.Add(new($"license:{type.Code}", $"Comprar licencia de {type.Name.ToLowerInvariant()} ({license})"));
        }
        var text = offers.Count > 0
            ? $"Los Valenti otorgamos las licencias de comercio en Bellmont. Un {string.Join("; un ", offers)}."
            : "Tus licencias están en regla. Que los negocios prosperen… y el alquiler llegue a tiempo.";

        // Pedido urgente de los Moretti: se entrega acá.
        var urgent = EconomyCatalog.UrgentCoffeeOrder;
        if (s.ActiveContract(urgent.Code) is { } order)
        {
            if (s.QuantityOf(urgent.ItemCode) >= urgent.Quantity)
            {
                options.Add(new NpcOption($"deliver:{order.Id}",
                    $"Entregar {urgent.Quantity} bolsas de café de los Moretti (cobrar {EconomyCatalog.FormatMoney(Pay(s, urgent))})"));
                text += "\n\n¿El café de los Moretti? Justo a tiempo.";
            }
            else
            {
                text += $"\n\nEstoy esperando {urgent.Quantity} bolsas de café de los Moretti. ¿Las traés?";
            }
        }

        // Cobranza: trabajo de cuadrilla, solo para quien ya es conocido en el Taller Caruso.
        var collection = EconomyCatalog.CarusoCollection;
        var required = collection.RequiredReputation!;
        if (s.ActiveContract(collection.Code) is not null)
        {
            text += "\n\nAndá con tu cuadrilla al Taller Caruso y cobrá lo que nos deben.";
        }
        else if (s.ReputationWith(required.FamilyCode) >= required.Delta)
        {
            options.Add(new NpcOption($"accept:{collection.Code}",
                $"Aceptar: {collection.Title} (paga {EconomyCatalog.FormatMoney(Pay(s, collection))} a repartir)"));
            text += "\n\nLos Caruso nos deben dinero y a vos te conocen. Andá con alguien de tu cuadrilla y cobrales.";
        }
        else
        {
            text += $"\n\nTengo trabajos para gente conocida en el Taller Caruso (hace falta {required.Delta} de reputación con los Caruso).";
        }
        return (text, options);
    }

    private static (string, List<NpcOption>) BusinessDialog(BusinessType type, EconomySnapshot s, string noBusinessText)
    {
        if (s.BusinessOf(type.Code) is not { } b)
            return (noBusinessText, new List<NpcOption>());

        var m = EconomyCatalog.FormatMoney;
        var text = $"{b.Name} — {b.OperatingState}\n" +
                   $"Stock: {b.Stock} × {type.ItemName} · precio {m(b.SalePriceCents)} · fama {b.Fame:0} · último ciclo: {b.LastCustomers} clientes\n" +
                   $"Caja: {m(b.CashCents)} · Patrimonio: {m(b.EquityCents)}\n" +
                   $"Vendidos: {b.UnitsSold} · Facturación: {m(b.RevenueCents)} · Alquiler: {m(b.RentCents)} · Sueldos: {m(b.WagesCents)} · Ganancia: {m(b.ProfitCents)}\n" +
                   $"{char.ToUpperInvariant(type.EmployeeName[0])}{type.EmployeeName[1..]}: {(b.HasHelper ? "contratado" : "no")}\n\n" +
                   "Fama, mejoras del local y precio: «Mi negocio» (tecla P).";
        var options = new List<NpcOption>();
        var carrying = s.QuantityOf(type.ItemCode);
        if (carrying > 0)
            options.Add(new NpcOption($"restock:{type.Code}", $"Reponer ({carrying} × {type.ItemName} que llevás)"));
        if (b.CashCents > 0)
            options.Add(new NpcOption($"withdraw:{type.Code}", $"Retirar ganancias ({m(b.CashCents)})"));
        options.Add(b.HasHelper
            ? new NpcOption($"fire:{type.Code}", $"Despedir al {type.EmployeeName}")
            : new NpcOption($"hire:{type.Code}",
                $"Contratar {type.EmployeeName} ({m(type.EmployeeWageCents)} por ciclo abierto, atiende a uno más por ciclo)"));
        if (b.Stock == 0 && carrying == 0)
            text += $"\n\nSin mercadería no hay ventas: comprá {type.ItemName}s en el Almacén Moretti.";
        return (text, options);
    }

    private static (string, List<NpcOption>) DepositContractDialog(EconomySnapshot s)
    {
        var template = EconomyCatalog.SparePartsDelivery;
        var active = s.ActiveContract(template.Code);
        var carrying = s.QuantityOf(template.ItemCode);
        var price = EconomyCatalog.FormatMoney(Price(s, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsPriceCents));
        var reward = EconomyCatalog.FormatMoney(Pay(s, template));
        var options = new List<NpcOption>();

        if (active is null)
        {
            options.Add(new NpcOption($"accept:{template.Code}", $"Aceptar: {template.Title} (paga {reward})"));
            if (carrying == 0)
                options.Add(new NpcOption($"buy:{template.ItemCode}", $"Comprar una caja de repuestos ({price})"));
            return ($"El Taller Caruso necesita repuestos. Si se los llevás, te pagan {reward}. La caja te la vendo a {price}.", options);
        }

        if (carrying < template.Quantity)
        {
            options.Add(new NpcOption($"buy:{template.ItemCode}", $"Comprar una caja de repuestos ({price})"));
            return ("Para cumplir el contrato vas a necesitar una caja. ¿Te la llevás?", options);
        }

        return ("Ya tenés la caja. Llevásela al Taller Caruso, del otro lado de la calle.", options);
    }

    private static (string, List<NpcOption>) WorkshopDialog(EconomySnapshot s)
    {
        var template = EconomyCatalog.SparePartsDelivery;
        var active = s.ActiveContract(template.Code);
        var options = new List<NpcOption>();
        string text;

        if (active is null)
            text = "Estamos cortos de repuestos. Hablá con el Almacén Moretti si querés hacer una changa.";
        else if (s.QuantityOf(template.ItemCode) < template.Quantity)
            text = "¿Y los repuestos? Conseguilos en el Almacén Moretti y volvé.";
        else
        {
            options.Add(new NpcOption($"deliver:{active.Id}", $"Entregar la caja de repuestos (cobrar {EconomyCatalog.FormatMoney(Pay(s, template))})"));
            text = "¡Justo lo que necesitaba! ¿Me la dejás?";
        }

        // Armería del Taller Caruso.
        var level = s.ProgressOrEmpty.Level;
        var locked = new List<string>();
        foreach (var w in Weapons.All)
        {
            if (level >= w.MinLevel)
                options.Add(new NpcOption($"buy:{w.Code}", $"Comprar {w.Name.ToLowerInvariant()} ({EconomyCatalog.FormatMoney(Price(s, EconomyCatalog.WorkshopNpc, w.PriceCents))})"));
            else
                locked.Add($"{w.Name.ToLowerInvariant()} (nivel {w.MinLevel})");
        }
        foreach (var pack in Weapons.Packs)
            options.Add(new NpcOption($"buy:{pack.Code}", $"Comprar {pack.Name.ToLowerInvariant()} ({EconomyCatalog.FormatMoney(Price(s, EconomyCatalog.WorkshopNpc, pack.PriceCents))})"));
        if (locked.Count > 0)
            text += $"\n\nPor la puerta de atrás también vendemos fierros. Todavía no te vendo: {string.Join(", ", locked)}.";

        var collection = EconomyCatalog.CarusoCollection;
        if (s.ActiveContract(collection.Code) is { } debt)
        {
            options.Add(new NpcOption($"deliver:{debt.Id}",
                $"Cobrar la deuda de los Valenti ({EconomyCatalog.FormatMoney(Pay(s, collection))}, con tu cuadrilla cerca)"));
            text += "\n\n¿Vienen de parte de los Valenti? …Está bien, está bien. Pero no vengas solo.";
        }
        return (text, options);
    }
}
