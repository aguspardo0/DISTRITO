using Distrito.Shared.Life;

namespace Distrito.Shared.Tests;

/// <summary>Vida de los NPC, etapa 8: la bolsa de las compras y hablar con los habitantes.</summary>
public class ResidentTalkTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Afternoon = Start.AddHours(8); // las 16 en Bellmont

    private static LifeConfig Config()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config/vida.json");
            if (File.Exists(candidate))
                return LifeConfig.Parse(File.ReadAllText(candidate));
        }
        throw new FileNotFoundException("config/vida.json");
    }

    private static Resident With(Resident r, double distrustful, double aggressive = 0.2) => r with
    {
        Traits = new Dictionary<string, double>(r.Traits) { ["desconfiado"] = distrustful, ["agresivo"] = aggressive, ["pacifico"] = 1 - aggressive },
    };

    private sealed record Town(LifeConfig Cfg, List<Resident> People, Dictionary<Guid, ResidentState> States)
    {
        public LifeWorld World => new(People.ToDictionary(p => p.Id), States);
    }

    private static Town Make()
    {
        var cfg = Config();
        var people = ResidentGenerator.Generate(cfg).Select(p => With(p, 0.2)).ToList();
        return new Town(cfg, people, people.ToDictionary(p => p.Id, p => ResidentGenerator.InitialState(p, Afternoon)));
    }

    [Fact]
    public void Al_saludar_dice_quien_es_y_que_esta_haciendo()
    {
        var t = Make();
        var vecina = t.People.First(p => p.Profession == "vecino" && p.Woman);
        var s = t.States[vecina.Id] with { Activity = LifeActivities.Shopping };
        var text = ResidentTalk.Greeting(vecina, s, t.Cfg, Afternoon);
        Assert.Contains($"Soy {vecina.FullName}, vecina ", text);
        Assert.Contains("compras", text);
        var district = t.Cfg.District(vecina.District)!.Name;
        Assert.Contains(district, text);
        Assert.DoesNotContain("del Los", text);

        var cop = t.People.First(p => p.Profession == "policia");
        Assert.Contains("agente de policía", ResidentTalk.Greeting(cop, t.States[cop.Id] with { Activity = LifeActivities.Patrolling }, t.Cfg, Afternoon));
        var fleeing = ResidentTalk.Greeting(vecina, s with { Activity = LifeActivities.Fleeing }, t.Cfg, Afternoon);
        Assert.Contains("tiros", fleeing);
    }

    [Fact]
    public void Cuenta_lo_que_vio_y_los_amigos_lo_saben_de_oidas()
    {
        var t = Make();
        var vecinos = t.People.Where(p => p.Profession == "vecino").ToList();
        Resident witness = vecinos[0], friend = vecinos[1], stranger = vecinos[2];
        t.States[friend.Id] = t.States[friend.Id] with { Ties = new() { [witness.Id] = 0.8 } };
        var rumor = new LifeRumor(IncidentKinds.Shot, 0, 14, Afternoon.AddMinutes(-10), ResidentTalk.NearestPlaceName(t.Cfg, 0, 14), new HashSet<Guid> { witness.Id });
        Assert.Equal("Diner La Estrella", rumor.PlaceName);
        var rumors = new List<LifeRumor> { rumor };

        var seen = ResidentTalk.Answer(ResidentTalk.News, witness, t.States[witness.Id], t.World, rumors, t.Cfg, Afternoon);
        Assert.Contains("Yo estaba ahí cerca", seen);
        Assert.Contains("un tiro por Diner La Estrella, hace 10 minutos", seen);

        var heard = ResidentTalk.Answer(ResidentTalk.News, friend, t.States[friend.Id], t.World, rumors, t.Cfg, Afternoon);
        Assert.Contains($"Me contó {witness.FirstName}", heard);

        var nothing = ResidentTalk.Answer(ResidentTalk.News, stranger, t.States[stranger.Id], t.World, rumors, t.Cfg, Afternoon);
        Assert.DoesNotContain("Diner", nothing);

        // Al otro día ya nadie habla de eso.
        var later = ResidentTalk.Answer(ResidentTalk.News, witness, t.States[witness.Id], t.World, rumors, t.Cfg, Afternoon.AddHours(ResidentTalk.RumorHours + 1));
        Assert.DoesNotContain("Diner", later);

        // El desconfiado no suelta nada aunque lo haya visto; la policía sabe de todos los avisos.
        var closed = With(witness, 0.9);
        Assert.DoesNotContain("Diner", ResidentTalk.Answer(ResidentTalk.News, closed, t.States[witness.Id], t.World, rumors, t.Cfg, Afternoon));
        var cop = t.People.First(p => p.Profession == "policia");
        Assert.Contains("Diner La Estrella", ResidentTalk.Answer(ResidentTalk.News, cop, t.States[cop.Id], t.World, rumors, t.Cfg, Afternoon));
    }

    [Fact]
    public void Habla_de_sus_amigos_rivales_y_de_donde_compra()
    {
        var t = Make();
        var me = t.People.First(p => p.Profession == "vecino");
        var friend = t.People.First(p => p.Profession == "obrero");
        var rival = t.People.First(p => p.Profession == "transportista");
        var merchant = t.People.First(p => p.Profession == "comerciante" && t.Cfg.ShopPlaces.Contains(p.Workplace!));
        var s = t.States[me.Id] with { Ties = new() { [friend.Id] = 0.75, [rival.Id] = -0.6, [merchant.Id] = 0.5 } };

        var people = ResidentTalk.Answer(ResidentTalk.People, me, s, t.World, [], t.Cfg, Afternoon);
        Assert.Contains($"Mi gran amigo es {friend.FullName}", people);
        Assert.Contains(rival.FullName, people);

        var shops = ResidentTalk.Answer(ResidentTalk.Shops, me, s, t.World, [], t.Cfg, Afternoon);
        Assert.Contains($"le compro a {merchant.FullName}", shops);
        Assert.Contains(t.Cfg.Place(merchant.Workplace!)!.Name, shops);

        var nobody = ResidentTalk.Answer(ResidentTalk.People, me, t.States[me.Id] with { Ties = null }, t.World, [], t.Cfg, Afternoon);
        Assert.Contains("Recién", nobody);
    }

    [Fact]
    public void Al_salir_de_comprar_lleva_la_bolsa_y_se_ve_en_la_red()
    {
        var t = Make();
        var sim = new LifeSimulation(t.Cfg);
        var r = t.People.First(p => p.Profession == "vecino");
        var shopping = t.States[r.Id] with { Activity = LifeActivities.Shopping, Place = "botica", Until = Afternoon, ArrivesAt = Afternoon.AddMinutes(-10), Since = Afternoon.AddMinutes(-12) };
        var next = sim.Advance(r, shopping, Afternoon);
        Assert.True(next.Bag);
        var view = ResidentSyncCodec.Decode(ResidentSyncCodec.Encode(0, [ResidentView.Of(r, next)], [])).Views[0];
        Assert.True(view.Bag);
        // La próxima vez que sale ya no la lleva (salvo que venga de comprar otra vez).
        var after = sim.Advance(r, next, next.Until);
        Assert.Equal(next.Activity == LifeActivities.Shopping, after.Bag);
    }
}
