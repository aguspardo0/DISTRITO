using Distrito.Shared.Economy;
using Distrito.Shared.Progression;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>Árbol de habilidades: aprender rangos con los puntos que da el nivel. Los efectos se aplican donde corresponde.</summary>
public sealed partial class EconomyService
{
    private static async Task<Dictionary<string, int>> SkillRanksAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var ranks = new Dictionary<string, int>();
        await using var cmd = new NpgsqlCommand("SELECT node_code, rank FROM distrito.character_skills WHERE character_id = @c", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) ranks[r.GetString(0)] = r.GetInt32(1);
        return ranks;
    }

    private static async Task<double> SkillAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, string code, CancellationToken ct) =>
        SkillTree.Effect(await SkillRanksAsync(c, tx, characterId, ct), code);

    public Task<EconomyActionResult> LearnSkillAsync(LearnSkillRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            // Se bloquea la fila del personaje para que dos clics simultáneos no gasten el mismo punto.
            await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM distrito.characters WHERE id = @c FOR UPDATE", c, tx))
            {
                lockRow.Parameters.AddWithValue("c", r.CharacterId);
                await lockRow.ExecuteNonQueryAsync(ct);
            }
            var level = (await LoadProgressAsync(c, tx, r.CharacterId, ct)).Level;
            var ranks = await SkillRanksAsync(c, tx, r.CharacterId, ct);
            if (SkillTree.WhyNot(r.Node, level, ranks) is { } reason)
                throw new EconomyRejected("cannot_learn", reason);
            await using (var learn = new NpgsqlCommand("""
                INSERT INTO distrito.character_skills (character_id, node_code, rank) VALUES (@c, @n, 1)
                ON CONFLICT (character_id, node_code) DO UPDATE SET rank = distrito.character_skills.rank + 1
                """, c, tx))
            {
                learn.Parameters.AddWithValue("c", r.CharacterId);
                learn.Parameters.AddWithValue("n", r.Node);
                await learn.ExecuteNonQueryAsync(ct);
            }
            await NewLedgerTransactionAsync(c, tx, r.RequestId, "skill_learned", $"Habilidad: {r.Node}", ct);
            var node = SkillTree.Find(r.Node)!;
            var rank = ranks.GetValueOrDefault(r.Node) + 1;
            return $"Aprendiste {node.Name}{(node.MaxRank > 1 ? $" (rango {rank}/{node.MaxRank})" : "")}: {node.Description}";
        });
}
