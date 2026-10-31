using Distrito.Shared.Api;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Tests;

/// <summary>Economía contra PostgreSQL real: compra, contrato, idempotencia, concurrencia y conservación del dinero.</summary>
[Trait("Category", "Database")]
public class EconomyApiTests
{
    private const string Key = TestBackend.ServerKey;
    private static readonly ContractTemplate Contract = EconomyCatalog.SparePartsDelivery;

    private static async Task<(DistritoApiClient Api, Guid CharacterId)> NewCharacter(DistritoApiClient api)
    {
        var user = TestBackend.UniqueUser();
        await api.RegisterAsync(user, "contraseña-larga-1");
        var token = (await api.LoginAsync(user, "contraseña-larga-1")).Value!.Token;
        var character = (await api.CreateCharacterAsync(token, TestBackend.UniqueCharacterName())).Value!;
        return (api, character.Id);
    }

    private static string Req() => Guid.NewGuid().ToString();

    private static BuyItemRequest Buy(Guid character, string? requestId = null) =>
        new(character, EconomyCatalog.DepositNpc, EconomyCatalog.SparePartsItem, 1, requestId ?? Req());

    [Fact]
    public async Task Personaje_nuevo_recibe_fondos_iniciales_una_sola_vez()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));

        var first = await api.GetEconomyAsync(Key, id);
        var second = await api.GetEconomyAsync(Key, id);

        Assert.Equal(EconomyCatalog.StartingFundsCents, first.Value!.BalanceCents);
        Assert.Equal(EconomyCatalog.StartingFundsCents, second.Value!.BalanceCents);
        Assert.Empty(first.Value.Inventory);
    }

    [Fact]
    public async Task Contrato_completo_compra_entrega_y_cobro()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));

        var accepted = await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Contract.Code, Req()));
        Assert.True(accepted.Value!.Ok, accepted.Value.Message);
        var contract = Assert.Single(accepted.Value.Snapshot.ActiveContracts);

        var bought = await api.BuyAsync(Key, Buy(id));
        Assert.True(bought.Value!.Ok, bought.Value.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.SparePartsPriceCents, bought.Value.Snapshot.BalanceCents);
        Assert.Equal(1, bought.Value.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));

        var delivered = await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()));
        Assert.True(delivered.Value!.Ok, delivered.Value.Message);
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.SparePartsPriceCents + Contract.RewardCents,
            delivered.Value.Snapshot.BalanceCents);
        Assert.Equal(0, delivered.Value.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));
        Assert.Empty(delivered.Value.Snapshot.ActiveContracts);
        Assert.Equal(1, delivered.Value.Snapshot.CompletedContracts);
    }

    [Fact]
    public async Task No_se_puede_entregar_sin_la_mercaderia()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Contract.Code, Req()))).Value!.Snapshot.ActiveContracts[0];

        var result = await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()));

        Assert.False(result.Value!.Ok);
        Assert.Equal("not_enough_items", result.Value.ErrorCode);
        Assert.Equal(EconomyCatalog.StartingFundsCents, result.Value.Snapshot.BalanceCents);
        Assert.Single(result.Value.Snapshot.ActiveContracts);
    }

    [Fact]
    public async Task No_se_puede_aceptar_dos_veces_el_mismo_contrato_en_curso()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));
        await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Contract.Code, Req()));

        var again = await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Contract.Code, Req()));

        Assert.False(again.Value!.Ok);
        Assert.Equal("contract_already_active", again.Value.ErrorCode);
    }

    [Fact]
    public async Task Sin_saldo_suficiente_no_se_compra_y_nada_cambia()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));
        var affordable = (int)(EconomyCatalog.StartingFundsCents / EconomyCatalog.SparePartsPriceCents);

        for (var i = 0; i < affordable; i++)
            Assert.True((await api.BuyAsync(Key, Buy(id))).Value!.Ok);
        var rejected = await api.BuyAsync(Key, Buy(id));

        Assert.False(rejected.Value!.Ok);
        Assert.Equal("insufficient_funds", rejected.Value.ErrorCode);
        Assert.Equal(affordable, rejected.Value.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));
        Assert.Equal(EconomyCatalog.StartingFundsCents - affordable * EconomyCatalog.SparePartsPriceCents, rejected.Value.Snapshot.BalanceCents);
    }

    [Fact]
    public async Task El_mismo_pedido_repetido_no_cobra_dos_veces()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));
        var request = Buy(id, requestId: Req());

        var first = await api.BuyAsync(Key, request);
        var retry = await api.BuyAsync(Key, request);

        Assert.True(first.Value!.Ok);
        Assert.True(retry.Value!.Ok);
        Assert.Equal(1, retry.Value.Snapshot.QuantityOf(EconomyCatalog.SparePartsItem));
        Assert.Equal(EconomyCatalog.StartingFundsCents - EconomyCatalog.SparePartsPriceCents, retry.Value.Snapshot.BalanceCents);
    }

    [Fact]
    public async Task Compras_simultaneas_nunca_dejan_el_saldo_negativo()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));
        var affordable = (int)(EconomyCatalog.StartingFundsCents / EconomyCatalog.SparePartsPriceCents);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.BuyAsync(Key, Buy(id))));
        var final = (await api.GetEconomyAsync(Key, id)).Value!;

        Assert.Equal(affordable, results.Count(r => r.Value!.Ok));
        Assert.Equal(affordable, final.QuantityOf(EconomyCatalog.SparePartsItem));
        Assert.True(final.BalanceCents >= 0);
        Assert.Equal(EconomyCatalog.StartingFundsCents - affordable * EconomyCatalog.SparePartsPriceCents, final.BalanceCents);
    }

    [Fact]
    public async Task El_dinero_se_conserva_y_los_saldos_coinciden_con_el_libro()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));
        var contract = (await api.AcceptContractAsync(Key, new AcceptContractRequest(id, Contract.Code, Req()))).Value!.Snapshot.ActiveContracts[0];
        await api.BuyAsync(Key, Buy(id));
        await api.DeliverAsync(Key, new DeliverContractRequest(id, contract.Id, Req()));

        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);

        // 1. Cada transacción del libro suma cero.
        await using (var unbalanced = db.CreateCommand(
            "SELECT count(*) FROM (SELECT transaction_id FROM distrito.ledger_entries GROUP BY transaction_id HAVING sum(amount_cents) <> 0) x"))
            Assert.Equal(0L, (long)(await unbalanced.ExecuteScalarAsync())!);

        // 2. La suma de todos los saldos es cero (todo lo que existe salió de la emisión).
        await using (var total = db.CreateCommand("SELECT sum(balance_cents) FROM distrito.economic_accounts"))
            Assert.Equal(0m, (decimal)(await total.ExecuteScalarAsync())!);

        // 3. El saldo guardado de cada cuenta coincide con la suma de sus asientos.
        await using (var mismatched = db.CreateCommand("""
            SELECT count(*) FROM distrito.economic_accounts a
            WHERE a.balance_cents <> COALESCE((SELECT sum(e.amount_cents) FROM distrito.ledger_entries e WHERE e.account_id = a.id), 0)
            """))
            Assert.Equal(0L, (long)(await mismatched.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task El_precio_del_catalogo_coincide_con_la_base()
    {
        await using var factory = TestBackend.Create();
        _ = factory.CreateClient(); // aplica migraciones
        await using var db = NpgsqlDataSource.Create(TestBackend.ConnectionString);
        await using var cmd = db.CreateCommand("SELECT reference_price_cents FROM distrito.item_definitions WHERE code = @c");
        cmd.Parameters.AddWithValue("c", EconomyCatalog.SparePartsItem);
        Assert.Equal(EconomyCatalog.SparePartsPriceCents, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task La_api_economica_exige_la_clave_del_servidor()
    {
        await using var factory = TestBackend.Create();
        var (api, id) = await NewCharacter(TestBackend.Api(factory));

        var result = await api.BuyAsync("clave-falsa", Buy(id));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, result.Status);
    }

    [Fact]
    public void Formato_de_dinero()
    {
        Assert.Equal("$15.00", EconomyCatalog.FormatMoney(1500));
        Assert.Equal("$0.05", EconomyCatalog.FormatMoney(5));
        Assert.Equal("-$8.00", EconomyCatalog.FormatMoney(-800));
    }
}
