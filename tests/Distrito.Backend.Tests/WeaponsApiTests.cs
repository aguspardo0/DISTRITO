using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Armas: compra con nivel, cajas de munición, munición gastada y armas que quedan tiradas al quedar herido.</summary>
[Trait("Category", "Database")]
public class WeaponsApiTests
{
    private const string Key = TestBackend.ServerKey;

    [Fact]
    public async Task Comprar_pistola_y_balas_gastar_y_perderla_al_quedar_herido()
    {
        await using var factory = TestBackend.Create();
        var api = TestBackend.Api(factory);
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var id = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!.Id;
        await api.GetEconomyAsync(Key, id);

        BuyItemRequest Buy(string code) => new(id, EconomyCatalog.WorkshopNpc, code, 1, Guid.NewGuid().ToString());
        Assert.Equal("level_too_low", (await api.BuyAsync(Key, Buy("pistola"))).Value!.ErrorCode);

        await using (var db = NpgsqlDataSource.Create(TestBackend.ConnectionString))
        {
            await using var cmd = db.CreateCommand("""
                INSERT INTO distrito.character_progress (character_id, xp) VALUES (@c, 300) ON CONFLICT (character_id) DO UPDATE SET xp = 300;
                WITH t AS (INSERT INTO distrito.ledger_transactions (id, idempotency_key, kind, description)
                           VALUES (gen_random_uuid(), 'test-grant:' || gen_random_uuid(), 'test', 'Fondos para prueba') RETURNING id),
                     mint AS (SELECT id FROM distrito.economic_accounts WHERE code = 'system:emision'),
                     acc AS (SELECT id FROM distrito.economic_accounts WHERE character_id = @c)
                INSERT INTO distrito.ledger_entries (transaction_id, account_id, amount_cents)
                SELECT t.id, mint.id, -5000 FROM t, mint UNION ALL SELECT t.id, acc.id, 5000 FROM t, acc;
                UPDATE distrito.economic_accounts SET balance_cents = balance_cents - 5000 WHERE code = 'system:emision';
                UPDATE distrito.economic_accounts SET balance_cents = balance_cents + 5000 WHERE character_id = @c;
                """);
            cmd.Parameters.AddWithValue("c", id);
            await cmd.ExecuteNonQueryAsync();
        }

        var pistol = (await api.BuyAsync(Key, Buy("pistola"))).Value!;
        Assert.True(pistol.Ok, pistol.Message);
        var ammo = (await api.BuyAsync(Key, Buy("caja-balas-pistola"))).Value!;
        Assert.True(ammo.Ok, ammo.Message);
        Assert.Equal(7, ammo.Snapshot.QuantityOf(Weapons.Pistol.AmmoItem));
        Assert.Equal(EconomyCatalog.StartingFundsCents + 5_000 - Weapons.Pistol.PriceCents - 200, ammo.Snapshot.BalanceCents);

        Assert.Equal(3, (await api.UseAmmoAsync(Key, new AmmoUseRequest(id, Weapons.Pistol.AmmoItem, 3, Guid.NewGuid().ToString()))).Value);
        Assert.Equal(4, (await api.UseAmmoAsync(Key, new AmmoUseRequest(id, Weapons.Pistol.AmmoItem, 10, Guid.NewGuid().ToString()))).Value); // solo quedaban 4
        Assert.Equal(0, (await api.GetEconomyAsync(Key, id)).Value!.QuantityOf(Weapons.Pistol.AmmoItem));

        // Herido: la pistola queda tirada (decisión del equipo: las armas se pierden).
        var drop = (await api.KnockoutAsync(Key, new KnockoutRequest(id, Guid.NewGuid(), Guid.NewGuid().ToString()))).Value!;
        Assert.Contains(drop.Items, i => i.ItemCode == "pistola");
        Assert.Equal(0, (await api.GetEconomyAsync(Key, id)).Value!.QuantityOf("pistola"));
    }
}
