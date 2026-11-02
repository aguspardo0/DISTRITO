namespace Distrito.Shared.Market;

/// <summary>Un local que vende al público (por ejemplo, un puesto de café).</summary>
public sealed class SimShop
{
    public string Name { get; init; } = "";
    public string Zone { get; init; } = "";
    public double X { get; init; }
    public double Z { get; init; }
    public string Product { get; init; } = "";
    public long PriceCents { get; set; }
    public int Stock { get; set; }
    /// <summary>Clientes que atiende por ciclo (techo de ventas; hoy: 2 + empleado + noche + Administración).</summary>
    public int Capacity { get; set; }
    public double Quality { get; init; } = 1;
    public long Cash { get; set; }
    public long RentPerCycle { get; init; }
    public long WagePerCycle { get; init; }
    public int RestockBelow { get; init; } = 8;
    public int RestockTo { get; init; } = 24;
    internal double Pending;
    // Del día (se reinician cada día).
    public int SoldToday { get; internal set; }
    public int OfferedToday { get; internal set; }
    public long CostToday { get; internal set; }
    public long RevenueToday { get; internal set; }
}

/// <summary>Proveedor (el Almacén): compra a la fábrica y vende a los comercios.</summary>
public sealed class SimSupplier
{
    public int Stock { get; set; }
    public long SellPriceCents { get; init; }
    public long Cash { get; set; }
}

/// <summary>Fábrica (el tostadero): con insumos y obreros pagos produce; compra los insumos en el puerto.</summary>
public sealed class SimFactory
{
    public string Zone { get; init; } = "";
    public double MaxPerCycle { get; init; }
    public int Workers { get; init; }
    public long WagePerWorker { get; init; }
    public double Productivity { get; init; }
    public double Waste { get; init; }
    public double Recipe { get; init; } = 1;
    public int Inputs { get; set; }
    public int Output { get; set; }
    public long Cash { get; set; }
    public long SellPriceCents { get; init; }
    public long InputPriceCents { get; init; }
    public int ReorderBelow { get; init; } = 50;
    public int ReorderAmount { get; init; } = 200;
    /// <summary>Produce mientras lo hecho más lo que tiene el proveedor no pase de esto (produce a pedido, no para acumular).</summary>
    public int ChainTarget { get; init; } = 400;
    internal double Pending;
    public int ProducedToday { get; internal set; }
}

/// <summary>Transportista (el camión): lleva lo de la fábrica al proveedor; tarda y cobra por viaje.</summary>
public sealed class SimTruck
{
    public int Capacity { get; init; }
    public int TripCycles { get; init; }
    public long FeePerTrip { get; init; }
    public int MinLoad { get; init; }
    /// <summary>Sale solo si el proveedor tiene menos que esto (el proveedor pide cuando le hace falta).</summary>
    public int ShipWhenSupplierBelow { get; init; } = 300;
    public long Cash { get; set; }
    public int Cargo { get; internal set; }
    public int Remaining { get; internal set; }
    public int LostToday { get; internal set; }
}

/// <summary>Lo que pasó en un día, por zona y por local (para el informe y las pruebas).</summary>
/// <summary>
/// Un día de una zona. Demand = lo que la gente quiso comprar; Wanted = la parte que fue a los locales (el resto eligió no
/// comprar: lejos o caro, «oportunidad»); Shortage = lo que fue a un local y no se pudo vender (sin stock o sin quién atienda);
/// Idle = capacidad con mercadería que nadie usó. El precio de referencia se mueve con Shortage − Idle.
/// </summary>
public sealed record SimZoneDay(string Zone, double Demand, double Wanted, int Served, double Shortage, double Idle, double ReferencePrice, long BudgetCents)
{
    public double Opportunity => Math.Max(0, Demand - Wanted);
}
public sealed record SimShopDay(string Shop, int Sold, int Offered, int StockEnd, long Revenue, long Cost, long ProfitCents);
public sealed record SimDay(int Day, List<SimZoneDay> Zones, List<SimShopDay> Shops, int SupplierStock, int FactoryOutput, int Produced,
    int LostInTransit, long TotalMoneyCents, long ExteriorCents);

/// <summary>
/// Simulador del mercado de prueba (etapa 1): una fábrica, un proveedor, comercios, cinco zonas de habitantes y un
/// transportista, todo en memoria y por ciclos agregados. La plata es entera (centavos) y se conserva: lo único que entra
/// es el ingreso de afuera (el puerto) y lo único que sale son los insumos que se compran en el puerto.
/// </summary>
public sealed class MarketSimulation
{
    private readonly MarketConfig _cfg;
    private readonly Dictionary<string, Zone> _zones;
    private int _cycle;

    public List<SimShop> Shops { get; } = new();
    public SimSupplier Supplier { get; }
    public SimFactory Factory { get; }
    public SimTruck Truck { get; }
    /// <summary>Lo que cobran las familias de alquiler (queda adentro del sistema).</summary>
    public long FamiliesCash { get; private set; }
    /// <summary>Saldo del exterior: baja con el ingreso que entra y sube con los insumos que se compran afuera.</summary>
    public long ExteriorCents { get; private set; }
    public List<SimDay> Days { get; } = new();

    /// <summary>Intervenciones del escenario: se llaman al empezar cada ciclo (día, ciclo del día).</summary>
    public Action<MarketSimulation, int, int>? BeforeCycle { get; set; }
    /// <summary>Si da verdadero, un viaje que llega se pierde (el camión fue asaltado).</summary>
    public Func<int, bool> TruckRobbed { get; set; } = _ => false;
    /// <summary>Si da falso, la fábrica no puede comprar insumos ese día (puerto parado).</summary>
    public Func<int, bool> PortOpen { get; set; } = _ => true;

    private sealed class Zone
    {
        public required MarketZoneConfig Config;
        public long Budget;
        public Dictionary<string, double> Reference = new();
        public double DemandToday;
        public double WantedToday;
        public double ShortageToday;
        public double IdleToday;
        public int ServedToday;
        public long PaidToday;
    }

    public MarketSimulation(MarketConfig cfg, SimSupplier supplier, SimFactory factory, SimTruck truck, long startingBudgetPerHabitant = 500)
    {
        _cfg = cfg;
        Supplier = supplier;
        Factory = factory;
        Truck = truck;
        _zones = cfg.Zones.ToDictionary(z => z.Code, z => new Zone
        {
            Config = z,
            Budget = z.Habitants * startingBudgetPerHabitant,
            Reference = cfg.Products.ToDictionary(p => p.Code, p => (double)p.BasePriceCents),
        });
    }

    public int Day => _cycle / _cfg.CyclesPerDay;

    public long ZoneBudget(string zone) => _zones[zone].Budget;
    public double ReferencePrice(string zone, string product) => _zones[zone].Reference[product];

    /// <summary>Toda la plata adentro de Bellmont (zonas, comercios, proveedor, fábrica, camión y familias).</summary>
    public long TotalMoney() =>
        _zones.Values.Sum(z => z.Budget) + Shops.Sum(s => s.Cash) + Supplier.Cash + Factory.Cash + Truck.Cash + FamiliesCash;

    /// <summary>Lo que no puede cambiar nunca: la plata de adentro más el saldo del exterior.</summary>
    public long ConservedTotal() => TotalMoney() + ExteriorCents;

    public void RunDays(int days)
    {
        for (var i = 0; i < days * _cfg.CyclesPerDay; i++)
            Step();
    }

    public void Step()
    {
        var day = Day;
        var inDay = _cycle % _cfg.CyclesPerDay;
        BeforeCycle?.Invoke(this, day, inDay);

        // Ingreso de afuera: el total del día repartido exacto en los ciclos (sin perder centavos).
        foreach (var z in _zones.Values)
        {
            var dayTotal = (long)z.Config.Habitants * z.Config.DailyIncomeCents;
            var amount = dayTotal * (inDay + 1) / _cfg.CyclesPerDay - dayTotal * inDay / _cfg.CyclesPerDay;
            z.Budget += amount;
            ExteriorCents -= amount;
        }

        ProduceAndShip(day);
        Restock();
        Sell();
        PayCosts();
        SpendSurplus();

        _cycle++;
        if (_cycle % _cfg.CyclesPerDay == 0)
            CloseDay(day);
    }

    private void ProduceAndShip(int day)
    {
        var f = Factory;
        // Insumos del puerto (la plata sale del sistema).
        if (f.Inputs < f.ReorderBelow && PortOpen(day))
        {
            // Primero se guarda un día de sueldos: sin obreros pagos, los insumos no sirven.
            var forInputs = Math.Max(0, f.Cash - (long)f.Workers * f.WagePerWorker * _cfg.ReserveCycles);
            var buy = (int)Math.Min(f.ReorderAmount, forInputs / Math.Max(1, f.InputPriceCents));
            f.Cash -= buy * f.InputPriceCents;
            ExteriorCents += buy * f.InputPriceCents;
            f.Inputs += buy;
        }
        // Obreros: trabajan (y cobran) solo si la cadena necesita producción; el sueldo va a la gente de su zona.
        var needed = f.Output + Supplier.Stock + Truck.Cargo < f.ChainTarget && f.Inputs > 0;
        var paid = needed ? (int)Math.Min(f.Workers, f.Cash / Math.Max(1, f.WagePerWorker)) : 0;
        f.Cash -= paid * f.WagePerWorker;
        _zones[f.Zone].Budget += paid * f.WagePerWorker;
        f.Pending += MarketMath.Production(f.MaxPerCycle, f.Inputs, f.Recipe, paid, f.Productivity, f.Waste);
        var made = Math.Min((int)f.Pending, (int)(f.Inputs / Math.Max(f.Recipe, 0.0001)));
        f.Pending -= made;
        f.Inputs -= (int)Math.Ceiling(made * f.Recipe);
        f.Output += made;
        f.ProducedToday += made;

        // Camión: sale cuando el proveedor necesita y hay carga. El proveedor paga la carga y el flete al cargar (desde ahí la
        // mercadería es suya): si asaltan el camión, pierde el proveedor, no la fábrica.
        var t = Truck;
        if (t.Remaining > 0)
        {
            t.Remaining--;
            if (t.Remaining == 0)
            {
                if (TruckRobbed(day))
                    t.LostToday += t.Cargo;
                else
                    Supplier.Stock += t.Cargo;
                t.Cargo = 0;
            }
        }
        else if (f.Output >= t.MinLoad && Supplier.Stock < t.ShipWhenSupplierBelow)
        {
            var affordable = (int)((Supplier.Cash - t.FeePerTrip) / Math.Max(1, f.SellPriceCents));
            var load = Math.Min(Math.Min(t.Capacity, f.Output), affordable);
            if (load >= t.MinLoad)
            {
                Supplier.Cash -= load * f.SellPriceCents + t.FeePerTrip;
                f.Cash += load * f.SellPriceCents;
                t.Cash += t.FeePerTrip;
                t.Cargo = load;
                f.Output -= load;
                t.Remaining = Math.Max(1, t.TripCycles);
            }
        }
    }

    private void Restock()
    {
        foreach (var s in Shops.Where(s => s.Stock < s.RestockBelow))
        {
            var qty = (int)Math.Min(Math.Min(s.RestockTo - s.Stock, Supplier.Stock), s.Cash / Math.Max(1, Supplier.SellPriceCents));
            if (qty <= 0) continue;
            s.Cash -= qty * Supplier.SellPriceCents;
            s.CostToday += qty * Supplier.SellPriceCents;
            Supplier.Cash += qty * Supplier.SellPriceCents;
            Supplier.Stock -= qty;
            s.Stock += qty;
        }
    }

    private void Sell()
    {
        foreach (var product in _cfg.Products)
        {
            var shops = Shops.Where(s => s.Product == product.Code).ToList();
            var wanted = new double[shops.Count, _zones.Count];
            var zones = _zones.Values.ToList();
            for (var zi = 0; zi < zones.Count; zi++)
            {
                var z = zones[zi];
                var reference = z.Reference[product.Code];
                var needPerCycle = MarketMath.Need(z.Config.Habitants, product.DailyConsumption, z.Config.PreferenceFor(product.Code)) / _cfg.CyclesPerDay;
                // La gente va a su local de siempre aunque esté vacío: si no hay, eso es escasez (no cambia de local en el acto).
                var attractions = shops.Select(s => MarketMath.Attraction(reference, s.PriceCents, s.Quality, Distance(z.Config, s), _cfg)).ToList();
                var shares = MarketMath.Shares(attractions, _cfg.NoPurchaseWeight);
                var shareSum = shares.Sum();
                var averagePrice = shareSum > 0 ? shops.Select((s, i) => shares[i] * s.PriceCents).Sum() / shareSum : reference;
                var demand = MarketMath.Demand(needPerCycle, reference, averagePrice, z.Budget, _cfg.ReserveCycles, _cfg.Elasticity, _cfg.MaxPriceBoost);
                // Sin ningún local con mercadería, la necesidad igual cuenta (es una oportunidad), al precio de referencia.
                z.DemandToday += shareSum > 0 ? demand : MarketMath.Demand(needPerCycle, reference, reference, z.Budget, _cfg.ReserveCycles, _cfg.Elasticity, _cfg.MaxPriceBoost);
                for (var si = 0; si < shops.Count; si++)
                {
                    wanted[si, zi] = shares[si] * demand;
                    z.WantedToday += wanted[si, zi];
                }
            }
            for (var si = 0; si < shops.Count; si++)
            {
                var s = shops[si];
                var offered = Math.Min(s.Stock, s.Capacity);
                s.OfferedToday += offered;
                var total = 0.0;
                for (var zi = 0; zi < zones.Count; zi++) total += wanted[si, zi];
                if (total <= 0) continue;
                if (offered == 0)
                {
                    // Fueron a comprar y no había: escasez.
                    for (var zi = 0; zi < zones.Count; zi++) zones[zi].ShortageToday += wanted[si, zi];
                    continue;
                }
                s.Pending += Math.Min(total, offered);
                var units = Math.Min((int)s.Pending, offered);
                s.Pending -= units;
                // Reparto entre zonas por resto mayor; cada zona paga lo que se lleva si le alcanza.
                var row = si;
                var exact = Enumerable.Range(0, zones.Count).Select(k => units * wanted[row, k] / total).ToArray();
                var given = exact.Select(e => (int)Math.Floor(e)).ToArray();
                foreach (var k in Enumerable.Range(0, zones.Count).OrderByDescending(j => exact[j] - given[j]).Take(units - given.Sum()).ToList())
                    given[k]++;
                // Lo que se quiso y no se pudo atender cuenta como escasez; la capacidad con mercadería que sobró, como ociosa.
                var idle = Math.Max(0, offered - total);
                for (var zi = 0; zi < zones.Count; zi++)
                {
                    zones[zi].ShortageToday += Math.Max(0, wanted[si, zi] - units * wanted[si, zi] / total);
                    zones[zi].IdleToday += idle * wanted[si, zi] / total;
                }
                for (var zi = 0; zi < zones.Count; zi++)
                {
                    var z = zones[zi];
                    var take = (int)Math.Min(given[zi], z.Budget / Math.Max(1, s.PriceCents));
                    if (take <= 0) continue;
                    var pay = take * s.PriceCents;
                    z.Budget -= pay;
                    z.ServedToday += take;
                    z.PaidToday += pay;
                    s.Cash += pay;
                    s.RevenueToday += pay;
                    s.Stock -= take;
                    s.SoldToday += take;
                }
            }
        }
    }

    private void PayCosts()
    {
        // Como en el juego: sin mercadería no hay alquiler ni empleado ese ciclo.
        foreach (var s in Shops.Where(s => s.Stock > 0))
        {
            var rent = Math.Min(s.RentPerCycle, s.Cash);
            s.Cash -= rent;
            FamiliesCash += rent;
            s.CostToday += rent;
            if (s.WagePerCycle > 0 && s.Cash >= s.WagePerCycle)
            {
                s.Cash -= s.WagePerCycle;
                s.CostToday += s.WagePerCycle;
                _zones[s.Zone].Budget += s.WagePerCycle; // el empleado vive en la zona del local
            }
        }
    }

    /// <summary>
    /// Las empresas NPC no guardan la plata para siempre: lo que les sobra por encima de una reserva vuelve a la gente de su
    /// barrio (sueldos, compras, gastos), de a poco. Sin esto la plata se estanca y la demanda se seca.
    /// </summary>
    private void SpendSurplus()
    {
        long Pay(long cash, string zone)
        {
            var surplus = cash - _cfg.BusinessReserveCents;
            if (surplus <= 0) return 0;
            var amount = Math.Max(1, surplus / _cfg.ReserveCycles);
            _zones[zone].Budget += amount;
            return amount;
        }
        Supplier.Cash -= Pay(Supplier.Cash, "el-mercado");
        Truck.Cash -= Pay(Truck.Cash, Factory.Zone);
        Factory.Cash -= Pay(Factory.Cash, Factory.Zone);
        FamiliesCash -= Pay(FamiliesCash, "barrio-valenti"); // el alquiler lo cobra la Oficina Valenti
        // Lo que ganan los dueños de los locales: en el juego vuelve cuando los jugadores gastan; acá se supone que lo
        // gastan en el barrio del local.
        foreach (var s in Shops)
            s.Cash -= Pay(s.Cash, s.Zone);
    }

    private void CloseDay(int day)
    {
        var zoneDays = new List<SimZoneDay>();
        foreach (var product in _cfg.Products)
            foreach (var z in _zones.Values)
            {
                var before = z.Reference[product.Code];
                // Sin locales a los que ir no hay señal de precio (sólo oportunidad).
                if (z.WantedToday > 0)
                    z.Reference[product.Code] = MarketMath.NextReferencePrice(before, product.BasePriceCents, z.ShortageToday, z.IdleToday, z.WantedToday, _cfg,
                        z.ServedToday > 0 ? z.PaidToday / (double)z.ServedToday : 0);
                zoneDays.Add(new SimZoneDay(z.Config.Code, z.DemandToday, z.WantedToday, z.ServedToday, z.ShortageToday, z.IdleToday, before, z.Budget));
            }
        var shopDays = Shops.Select(s => new SimShopDay(s.Name, s.SoldToday, s.OfferedToday, s.Stock, s.RevenueToday, s.CostToday, s.RevenueToday - s.CostToday)).ToList();
        Days.Add(new SimDay(day + 1, zoneDays, shopDays, Supplier.Stock, Factory.Output, Factory.ProducedToday, Truck.LostToday, TotalMoney(), ExteriorCents));
        foreach (var z in _zones.Values) { z.DemandToday = 0; z.WantedToday = 0; z.ShortageToday = 0; z.IdleToday = 0; z.ServedToday = 0; z.PaidToday = 0; }
        foreach (var s in Shops) { s.SoldToday = 0; s.OfferedToday = 0; s.CostToday = 0; s.RevenueToday = 0; }
        Factory.ProducedToday = 0;
        Truck.LostToday = 0;
    }

    private static double Distance(MarketZoneConfig z, SimShop s) => Math.Sqrt((z.X - s.X) * (z.X - s.X) + (z.Z - s.Z) * (z.Z - s.Z));
}
