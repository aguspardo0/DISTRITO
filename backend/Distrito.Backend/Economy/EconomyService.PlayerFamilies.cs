using System.Text.Json;
using Distrito.Backend.Crews;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Familias de jugadores (Etapa E). Máximo 5 en el servidor y, por ahora, la fundación cerrada (decisión del equipo,
/// 2026-11-01). Se habilita con config/familias.json («fundacionHabilitada») o Families:FoundingEnabled en el servidor.
/// </summary>
public sealed partial class EconomyService
{
    /// <summary>Configuración: si se pueden fundar y cuántas como máximo (nunca más de 5).</summary>
    public (bool FoundingEnabled, int Max) FamilySettings()
    {
        var enabled = false;
        var max = PlayerFamilyRules.HardMax;
        var file = config.GetValue<string>("Families:File") ?? Path.Combine(AppContext.BaseDirectory, "familias.json");
        if (File.Exists(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("fundacionHabilitada", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    enabled = e.GetBoolean();
                if (doc.RootElement.TryGetProperty("maximo", out var m) && m.TryGetInt32(out var n))
                    max = n;
            }
            catch (JsonException)
            {
                // archivo mal escrito: queda cerrado
            }
        }
        if (config.GetValue<bool?>("Families:FoundingEnabled") is { } overridden)
            enabled = overridden;
        return (enabled, Math.Clamp(max, 0, PlayerFamilyRules.HardMax));
    }

    private async Task<PlayerFamiliesView> PlayerFamiliesAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var (enabled, max) = FamilySettings();
        var families = new List<PlayerFamilyView>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT f.id, f.name, ch.name, f.created_at,
                   COALESCE((SELECT array_agg(cr.name ORDER BY cr.name) FROM distrito.crews cr WHERE cr.family_id = f.id), '{}')
              FROM distrito.player_families f JOIN distrito.characters ch ON ch.id = f.don_character_id ORDER BY f.created_at
            """, c, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                families.Add(new PlayerFamilyView(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetFieldValue<string[]>(4).ToList(), r.GetFieldValue<DateTimeOffset>(3)));

        var seat = await CrewSeatAsync(c, tx, characterId, ct);
        string? mine = null;
        var rank = 1;
        long box = 0;
        if (seat is not null)
        {
            await using (var cmd = new NpgsqlCommand("SELECT f.name FROM distrito.crews cr JOIN distrito.player_families f ON f.id = cr.family_id WHERE cr.id = @c", c, tx))
            {
                cmd.Parameters.AddWithValue("c", seat.CrewId);
                mine = await cmd.ExecuteScalarAsync(ct) as string;
            }
            rank = (await PrestigeAsync(c, tx, seat.CrewId, ct)).Rank;
            box = await BalanceAsync(c, tx, await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct), ct);
        }
        var level = (await LoadProgressAsync(c, tx, characterId, ct)).Level;
        var missing = PlayerFamilyRules.Missing(seat is not null, seat?.Role == "leader", rank, level, box);
        return new PlayerFamiliesView(enabled, max, families, mine, missing);
    }

    /// <summary>Fundar una familia de jugadores con la cuadrilla propia (si está habilitado y queda lugar).</summary>
    public Task<EconomyActionResult> FoundFamilyAsync(FoundFamilyRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var (enabled, max) = FamilySettings();
            if (!enabled)
                throw new EconomyRejected("not_enabled", "Todavía no se pueden fundar familias de jugadores. Van a ser pocas: como mucho cinco.");
            if (PlayerFamilyRules.NameProblem(r.Name) is { } problem)
                throw new EconomyRejected("invalid_name", problem);
            // Una a la vez: el tope de cinco no se puede pasar con dos pedidos simultáneos.
            await using (var lockCmd = new NpgsqlCommand("LOCK TABLE distrito.player_families IN EXCLUSIVE MODE", c, tx))
                await lockCmd.ExecuteNonQueryAsync(ct);
            var view = await PlayerFamiliesAsync(c, tx, r.CharacterId, ct);
            if (view.Families.Count >= max)
                throw new EconomyRejected("full", $"Ya hay {max} familias en Bellmont. No entra ninguna más.");
            if (view.MyFamily is not null)
                throw new EconomyRejected("already", $"Tu cuadrilla ya es parte de la familia «{view.MyFamily}».");
            if (view.Missing.Count > 0)
                throw new EconomyRejected("requirements", $"Para fundar una familia falta: {string.Join(", ", view.Missing)}.");
            var seat = (await CrewSeatAsync(c, tx, r.CharacterId, ct))!;
            var box = await CrewService.EnsureCrewAccountAsync(c, tx, seat.CrewId, ct);
            var city = await AccountIdByCodeAsync(c, tx, EconomyCatalog.ValentiOfficeNpc, ct); // los papeles los firma la Oficina Valenti
            await LockAccountsAsync(c, tx, ct, box, city);
            var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "player_family", $"Fundación de la familia {r.Name.Trim()}", ct);
            await TransferMoneyAsync(c, tx, txId, from: box, to: city, PlayerFamilyRules.CostCents, ct);
            var id = Guid.CreateVersion7();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO distrito.player_families (id, name, founder_crew_id, don_character_id) VALUES (@id, @name, @crew, @don);
                UPDATE distrito.crews SET family_id = @id WHERE id = @crew;
                """, c, tx))
            {
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("name", r.Name.Trim());
                insert.Parameters.AddWithValue("crew", seat.CrewId);
                insert.Parameters.AddWithValue("don", r.CharacterId);
                await insert.ExecuteNonQueryAsync(ct);
            }
            return $"Nace la familia «{r.Name.Trim()}». Sos el Don; «{seat.CrewName}» es su primera subfacción.";
        });
}
