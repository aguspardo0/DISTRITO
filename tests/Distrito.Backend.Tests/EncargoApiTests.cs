using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Encargos personales: ofertas, tomar con quien los da, pasos en el lugar, paga, abandonar y el sobre perdido.</summary>
[Trait("Category", "Database")]
public class EncargoApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static string Req() => Guid.NewGuid().ToString();

    private static async Task<Guid> Player(DistritoApiClient api, string user, long cents = 20_000, int level = 3)
    {
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        Assert.True((await api.AdminGrantAsync(Key, new AdminGrantRequest(id, cents, null, 0, level, Req()))).Value!.Ok);
        return id;
    }

    /// <summary>Cambia un encargo tomado para probar un tipo concreto sin depender del azar de las ofertas.</summary>
    private static async Task Force(Guid encargo, string kind, string? item, int quantity, string? target, int goal = 1)
    {
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("""
            UPDATE distrito.character_encargos SET kind = @k, item_code = @i, quantity = @q, target_npc = @t, target = @g, stage = 0, progress = 0
             WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("k", kind);
        cmd.Parameters.AddWithValue("i", (object?)item ?? DBNull.Value);
        cmd.Parameters.AddWithValue("q", quantity);
        cmd.Parameters.AddWithValue("t", (object?)target ?? DBNull.Value);
        cmd.Parameters.AddWithValue("g", goal);
        cmd.Parameters.AddWithValue("id", encargo);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task<ApiResult<EconomyActionResult>> Act(DistritoApiClient api, Guid who, Guid encargo, string action, string npc) =>
        api.EncargoActionAsync(Key, new EncargoActionRequest(who, encargo, action, npc, Req()));

    [Fact]
    public async Task Ofertas_tomar_entregar_y_cobrar()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await Player(api, TestBackend.UniqueUser("en"));

        var snapshot = (await api.GetEconomyAsync(Key, me)).Value!;
        var offers = snapshot.OfferedEncargos.ToList();
        Assert.Equal(EncargoRules.Offers + 1, offers.Count); // los del turno y el del día
        Assert.Single(offers, o => o.Daily);
        Assert.All(offers, o => Assert.True(o.RewardCents <= (o.Daily ? 11_300 : EncargoRules.MaxRewardCents)));

        // Se toma solo con quien lo da.
        var offer = offers.First(o => !o.Daily);
        var wrong = (await Act(api, me, offer.Id, "aceptar", "npc:esquina-cafe")).Value!;
        Assert.Equal("wrong_npc", wrong.ErrorCode);
        var taken = (await Act(api, me, offer.Id, "aceptar", offer.GiverNpc)).Value!;
        Assert.True(taken.Ok, taken.Message);
        Assert.Contains(taken.Snapshot.ActiveEncargos, e => e.Id == offer.Id);
        Assert.DoesNotContain(taken.Snapshot.OfferedEncargos, e => e.Id == offer.Id);
        Assert.Equal("taken", (await Act(api, me, offer.Id, "aceptar", offer.GiverNpc)).Value!.ErrorCode);

        // Entrega: sin la mercadería no se puede; con ella, paga, experiencia y reputación con la familia.
        await Force(offer.Id, EncargoKinds.Entrega, EconomyCatalog.CoffeeBagItem, 2, null, 2);
        Assert.Equal("not_enough_items", (await Act(api, me, offer.Id, "paso", offer.GiverNpc)).Value!.ErrorCode);
        await api.AdminGrantAsync(Key, new AdminGrantRequest(me, 0, EconomyCatalog.CoffeeBagItem, 2, null, Req()));
        var before = (await api.GetEconomyAsync(Key, me)).Value!;
        var done = (await Act(api, me, offer.Id, "paso", offer.GiverNpc)).Value!;
        Assert.True(done.Ok, done.Message);
        Assert.Contains("¡Encargo cumplido!", done.Message);
        Assert.Equal(before.BalanceCents + offer.RewardCents, done.Snapshot.BalanceCents);
        Assert.Equal(before.ProgressOrEmpty.Xp + offer.Xp, done.Snapshot.ProgressOrEmpty.Xp);
        Assert.Equal(before.ReputationWith(offer.Family) + offer.Reputation, done.Snapshot.ReputationWith(offer.Family));
        Assert.Equal(0, done.Snapshot.QuantityOf(EconomyCatalog.CoffeeBagItem));
        Assert.Empty(done.Snapshot.ActiveEncargos);
    }

    [Fact]
    public async Task Cobro_al_rival_sobre_y_sobre_perdido_al_abandonar()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await Player(api, TestBackend.UniqueUser("co"));
        var offers = (await api.GetEconomyAsync(Key, me)).Value!.OfferedEncargos.Where(o => !o.Daily).Take(2).ToList();
        foreach (var o in offers)
            Assert.True((await Act(api, me, o.Id, "aceptar", o.GiverNpc)).Value!.Ok);
        // No se puede tener más de dos en curso.
        var third = (await api.GetEconomyAsync(Key, me)).Value!.OfferedEncargos.First();
        Assert.Equal("too_many", (await Act(api, me, third.Id, "aceptar", third.GiverNpc)).Value!.ErrorCode);

        // Cobro para los Moretti en el Taller Caruso: el sobre aparece encima y los Caruso te quieren un poco menos.
        var cobro = offers[0];
        await Force(cobro.Id, EncargoKinds.Cobro, null, 0, EconomyCatalog.WorkshopNpc);
        await using (var db = NpgsqlDataSource.Create(TestBackend.ConnectionString))
        await using (var family = db.CreateCommand("UPDATE distrito.character_encargos SET family = 'moretti', giver_npc = 'npc:moretti-tano' WHERE id = @id"))
        {
            family.Parameters.AddWithValue("id", cobro.Id);
            await family.ExecuteNonQueryAsync();
        }
        await using (var db = NpgsqlDataSource.Create(TestBackend.ConnectionString))
        await using (var rep = db.CreateCommand("""
            INSERT INTO distrito.character_reputation (character_id, family_code, value) VALUES (@c, 'caruso', 10)
            ON CONFLICT (character_id, family_code) DO UPDATE SET value = 10
            """))
        {
            rep.Parameters.AddWithValue("c", me);
            await rep.ExecuteNonQueryAsync();
        }
        var carusoBefore = (await api.GetEconomyAsync(Key, me)).Value!.ReputationWith(EconomyCatalog.Caruso);
        Assert.Equal("wrong_npc", (await Act(api, me, cobro.Id, "paso", "npc:moretti-tano")).Value!.ErrorCode);
        var collected = (await Act(api, me, cobro.Id, "paso", EconomyCatalog.WorkshopNpc)).Value!;
        Assert.True(collected.Ok, collected.Message);
        Assert.Equal(1, collected.Snapshot.QuantityOf(EncargoRules.CollectionEnvelope));
        Assert.Equal(Math.Max(0, carusoBefore - EncargoRules.RivalPenalty), collected.Snapshot.ReputationWith(EconomyCatalog.Caruso));
        Assert.Equal(1, collected.Snapshot.ActiveEncargos.Single(e => e.Id == cobro.Id).Stage);

        // Si el sobre se pierde y se abandona el encargo, se paga lo que valía.
        await using (var db = NpgsqlDataSource.Create(TestBackend.ConnectionString))
        await using (var lose = db.CreateCommand("""
            DELETE FROM distrito.item_stacks WHERE item_code = 'sobre-cobro'
               AND owner_account_id = (SELECT id FROM distrito.economic_accounts WHERE character_id = @c)
            """))
        {
            lose.Parameters.AddWithValue("c", me);
            await lose.ExecuteNonQueryAsync();
        }
        var balance = collected.Snapshot.BalanceCents;
        var left = (await api.AbandonEncargoAsync(Key, new EncargoAbandonRequest(me, cobro.Id, Req()))).Value!;
        Assert.True(left.Ok, left.Message);
        Assert.Contains("Perdiste el sobre", left.Message);
        Assert.Equal(balance - cobro.RewardCents, left.Snapshot.BalanceCents);

        // Asalto: cuenta lo que pasa en el mundo y se cobra volviendo con quien lo dio.
        var asalto = offers[1];
        await Force(asalto.Id, EncargoKinds.Asalto, null, 0, EncargoRules.Truck);
        var progress = (await api.EncargoProgressAsync(Key, new EncargoProgressRequest(me, EncargoKinds.Asalto, 1, Req(), EncargoRules.Truck))).Value!;
        Assert.True(progress.Ok, progress.Message);
        Assert.Contains("Volvé con", progress.Message);
        var paid = (await Act(api, me, asalto.Id, "paso", asalto.GiverNpc)).Value!;
        Assert.True(paid.Ok, paid.Message);
        Assert.Contains("¡Encargo cumplido!", paid.Message);
    }

    [Fact]
    public async Task Recado_con_sobre_sellado_y_vencimiento()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var me = await Player(api, TestBackend.UniqueUser("re"), level: 1);
        var offer = (await api.GetEconomyAsync(Key, me)).Value!.OfferedEncargos.First(o => !o.Daily);
        Assert.True((await Act(api, me, offer.Id, "aceptar", offer.GiverNpc)).Value!.Ok);
        await Force(offer.Id, EncargoKinds.Recado, null, 0, EconomyCatalog.JazzClubNpc);
        // El sobre lo da la familia al tomar el recado; acá se fuerza el tipo, así que se lo damos a mano.
        await using (var db = NpgsqlDataSource.Create(TestBackend.ConnectionString))
        {
            await using var give = db.CreateCommand("""
                INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity)
                SELECT gen_random_uuid(), id, 'sobre-sellado', 'encima', 1 FROM distrito.economic_accounts WHERE character_id = @c
                """);
            give.Parameters.AddWithValue("c", me);
            await give.ExecuteNonQueryAsync();
        }
        var delivered = (await Act(api, me, offer.Id, "paso", EconomyCatalog.JazzClubNpc)).Value!;
        Assert.True(delivered.Ok, delivered.Message);
        Assert.Equal(0, delivered.Snapshot.QuantityOf(EncargoRules.SealedEnvelope));

        // Vencimiento: el que pasó de hora queda vencido al mirar la economía.
        var another = delivered.Snapshot.OfferedEncargos.First(o => !o.Daily);
        Assert.True((await Act(api, me, another.Id, "aceptar", another.GiverNpc)).Value!.Ok);
        await using (var db = NpgsqlDataSource.Create(TestBackend.ConnectionString))
        await using (var old = db.CreateCommand("UPDATE distrito.character_encargos SET expires_at = now() - interval '1 minute' WHERE id = @id"))
        {
            old.Parameters.AddWithValue("id", another.Id);
            await old.ExecuteNonQueryAsync();
        }
        Assert.Empty((await api.GetEconomyAsync(Key, me)).Value!.ActiveEncargos);
    }
}
