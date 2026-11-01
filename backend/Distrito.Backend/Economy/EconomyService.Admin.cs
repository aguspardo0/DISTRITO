using System.Text.Json;
using Distrito.Shared.Economy;
using Distrito.Shared.Progression;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Panel de administración para probar: solo cuentas listadas en config/admins.json (o Admin:Accounts en el servidor).
/// El backend verifica la cuenta en cada pedido; todo queda en el libro como emisión de admin.
/// </summary>
public sealed partial class EconomyService
{
    public HashSet<string> AdminAccounts()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in (config.GetValue<string>("Admin:Accounts") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            names.Add(n);
        var file = config.GetValue<string>("Admin:File") ?? Path.Combine(AppContext.BaseDirectory, "admins.json");
        if (File.Exists(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("cuentas", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var e in list.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } n)
                            names.Add(n.Trim());
            }
            catch (JsonException)
            {
                // archivo mal escrito: sin admins
            }
        }
        return names;
    }

    private async Task<bool> IsAdminAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var admins = AdminAccounts();
        if (admins.Count == 0)
            return false;
        await using var cmd = new NpgsqlCommand("SELECT a.username FROM distrito.characters ch JOIN distrito.accounts a ON a.id = ch.account_id WHERE ch.id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        return await cmd.ExecuteScalarAsync(ct) is string username && admins.Contains(username);
    }

    public async Task<AdminView> AdminAsync(Guid characterId, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (!await IsAdminAsync(c, tx, characterId, ct))
        {
            await tx.CommitAsync(ct);
            return new AdminView(false, []);
        }
        var items = new List<InventoryItem>();
        await using (var cmd = new NpgsqlCommand("SELECT code, name FROM distrito.item_definitions ORDER BY kind, name", c, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) items.Add(new InventoryItem(r.GetString(0), r.GetString(1), 0));
        await tx.CommitAsync(ct);
        return new AdminView(true, items);
    }

    public Task<EconomyActionResult> AdminGrantAsync(AdminGrantRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (!await IsAdminAsync(c, tx, r.CharacterId, ct))
                throw new EconomyRejected("not_admin", "Esa cuenta no es de administración.");
            if (r.Cents is < 0 or > 100_000_000 || r.Quantity is < 0 or > 10_000 || r.Level is < 1 or > ProgressionRules.MaxLevel)
                throw new EconomyRejected("invalid_request", "Valores fuera de rango.");
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var emission = await AccountIdByCodeAsync(c, tx, "system:emision", ct);
            await LockAccountsAsync(c, tx, ct, me, emission);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "admin_grant", "Panel de administración (pruebas)", ct);
            var done = new List<string>();
            if (r.Cents > 0)
            {
                await TransferMoneyAsync(c, tx, txId, from: emission, to: me, r.Cents, ct);
                done.Add(EconomyCatalog.FormatMoney(r.Cents));
            }
            if (r.ItemCode is { Length: > 0 } code && r.Quantity > 0)
            {
                var (name, _) = await ItemInfoAsync(c, tx, code, ct);
                await using var give = new NpgsqlCommand("""
                    INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity) VALUES (@id, @a, @i, @l, @q)
                    ON CONFLICT (owner_account_id, item_code, location) DO UPDATE SET quantity = distrito.item_stacks.quantity + EXCLUDED.quantity;
                    INSERT INTO distrito.item_movements (transaction_id, item_code, from_account_id, from_location, to_account_id, to_location, quantity, actor_character_id)
                    VALUES (@tx, @i, @e, 'admin', @a, @l, @q, @c);
                    """, c, tx);
                give.Parameters.AddWithValue("id", Guid.CreateVersion7());
                give.Parameters.AddWithValue("a", me);
                give.Parameters.AddWithValue("i", code);
                give.Parameters.AddWithValue("l", EconomyCatalog.CarriedLocation);
                give.Parameters.AddWithValue("q", r.Quantity);
                give.Parameters.AddWithValue("tx", txId);
                give.Parameters.AddWithValue("e", emission);
                give.Parameters.AddWithValue("c", r.CharacterId);
                await give.ExecuteNonQueryAsync(ct);
                done.Add($"{r.Quantity} × {name}");
            }
            if (r.Level is { } level)
            {
                await using var set = new NpgsqlCommand("""
                    INSERT INTO distrito.character_progress (character_id, xp) VALUES (@c, @xp)
                    ON CONFLICT (character_id) DO UPDATE SET xp = @xp, updated_at = now()
                    """, c, tx);
                set.Parameters.AddWithValue("c", r.CharacterId);
                set.Parameters.AddWithValue("xp", ProgressionRules.XpForLevel(level));
                await set.ExecuteNonQueryAsync(ct);
                done.Add($"nivel {level}");
            }
            return done.Count == 0 ? "Nada que dar." : $"Admin: {string.Join(", ", done)}.";
        });
}
