using Distrito.Backend.Economy;
using Distrito.Shared.Crews;
using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Crews;

/// <summary>Votaciones de la cuadrilla: cambiar de jefe o dar plata de la caja a un integrante.</summary>
public sealed partial class CrewService
{
    public Task<CrewActionResult> OpenVoteAsync(CrewVoteOpenRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            if (r.Kind is not (CrewVotes.Leader or CrewVotes.Payout))
                throw new CrewRejected("invalid_vote", "Tipo de votación inválido.");
            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);
            await CloseExpiredVotesAsync(c, tx, crew.Id, ct);
            var target = await CrewOfAsync(c, tx, r.TargetCharacterId, ct);
            if (target?.Id != crew.Id)
                throw new CrewRejected("invalid_target", "Esa persona no es de tu cuadrilla.");
            if (r.Kind == CrewVotes.Leader && target.Role == CrewRoles.Leader)
                throw new CrewRejected("invalid_target", "Esa persona ya es el jefe.");
            if (r.Kind == CrewVotes.Payout)
            {
                var box = await EnsureCrewAccountAsync(c, tx, crew.Id, ct);
                if (r.AmountCents <= 0 || r.AmountCents > await EconomyService.BalanceAsync(c, tx, box, ct))
                    throw new CrewRejected("invalid_amount", "La caja no tiene esa plata.");
            }

            var id = Guid.CreateVersion7();
            var now = clock.GetUtcNow();
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO distrito.crew_votes (id, crew_id, kind, target_character_id, amount_cents, opened_by, opened_at, closes_at)
                VALUES (@id, @crew, @kind, @target, @amount, @by, @now, @closes)
                ON CONFLICT DO NOTHING
                """, c, tx))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("crew", crew.Id);
                cmd.Parameters.AddWithValue("kind", r.Kind);
                cmd.Parameters.AddWithValue("target", r.TargetCharacterId);
                cmd.Parameters.AddWithValue("amount", r.Kind == CrewVotes.Payout ? r.AmountCents : 0);
                cmd.Parameters.AddWithValue("by", r.CharacterId);
                cmd.Parameters.AddWithValue("now", now);
                cmd.Parameters.AddWithValue("closes", now + CrewVotes.Duration);
                if (await cmd.ExecuteNonQueryAsync(ct) == 0)
                    throw new CrewRejected("vote_open", "Ya hay una votación abierta.");
            }
            await LogAsync(c, tx, crew.Id, "vote_opened", r.CharacterId, await DescribeVoteAsync(c, tx, id, ct), ct);
            await CastAsync(c, tx, crew.Id, id, r.CharacterId, yes: true, ct); // quien la abre vota que sí
            return "Abriste la votación. Votaste que sí.";
        });

    public Task<CrewActionResult> CastVoteAsync(CrewVoteCastRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, ct, async (c, tx) =>
        {
            var crew = await CrewOfAsync(c, tx, r.CharacterId, ct)
                ?? throw new CrewRejected("not_in_crew", "No estás en una cuadrilla.");
            await LockCrewAsync(c, tx, crew.Id, ct);
            await CloseExpiredVotesAsync(c, tx, crew.Id, ct);
            await using (var check = new NpgsqlCommand(
                "SELECT 1 FROM distrito.crew_votes WHERE id = @id AND crew_id = @crew AND status = 'open'", c, tx))
            {
                check.Parameters.AddWithValue("id", r.VoteId);
                check.Parameters.AddWithValue("crew", crew.Id);
                if (await check.ExecuteScalarAsync(ct) is null)
                    throw new CrewRejected("vote_closed", "Esa votación ya cerró.");
            }
            var result = await CastAsync(c, tx, crew.Id, r.VoteId, r.CharacterId, r.Yes, ct);
            return result switch
            {
                "passed" => "Tu voto definió la votación: se aprobó.",
                "failed" => "Tu voto definió la votación: no se aprobó.",
                _ => r.Yes ? "Votaste que sí." : "Votaste que no.",
            };
        });

    /// <summary>Registra el voto y, si ya hay resultado, cierra y ejecuta. Devuelve open, passed o failed.</summary>
    private async Task<string> CastAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid voteId, Guid voter, bool yes, CancellationToken ct)
    {
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO distrito.crew_vote_ballots (vote_id, character_id, yes) VALUES (@v, @c, @y)
            ON CONFLICT (vote_id, character_id) DO UPDATE SET yes = EXCLUDED.yes
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("v", voteId);
            cmd.Parameters.AddWithValue("c", voter);
            cmd.Parameters.AddWithValue("y", yes);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        int members, yesCount, noCount;
        await using (var count = new NpgsqlCommand("""
            SELECT (SELECT count(*)::int FROM distrito.crew_members WHERE crew_id = @crew),
                   (SELECT count(*)::int FROM distrito.crew_vote_ballots b JOIN distrito.crew_members m ON m.character_id = b.character_id AND m.crew_id = @crew WHERE b.vote_id = @v AND b.yes),
                   (SELECT count(*)::int FROM distrito.crew_vote_ballots b JOIN distrito.crew_members m ON m.character_id = b.character_id AND m.crew_id = @crew WHERE b.vote_id = @v AND NOT b.yes)
            """, c, tx))
        {
            count.Parameters.AddWithValue("crew", crewId);
            count.Parameters.AddWithValue("v", voteId);
            await using var reader = await count.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            (members, yesCount, noCount) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }

        if (CrewVotes.Passed(yesCount, members))
        {
            await ExecuteVoteAsync(c, tx, crewId, voteId, ct);
            await CloseVoteAsync(c, tx, crewId, voteId, "passed", ct);
            return "passed";
        }
        if (CrewVotes.CannotPass(noCount, members))
        {
            await CloseVoteAsync(c, tx, crewId, voteId, "failed", ct);
            return "failed";
        }
        return "open";
    }

    private async Task ExecuteVoteAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid voteId, CancellationToken ct)
    {
        string kind;
        Guid target;
        long amount;
        await using (var cmd = new NpgsqlCommand("SELECT kind, target_character_id, amount_cents FROM distrito.crew_votes WHERE id = @v", c, tx))
        {
            cmd.Parameters.AddWithValue("v", voteId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            (kind, target, amount) = (reader.GetString(0), reader.GetGuid(1), reader.GetInt64(2));
        }

        if (kind == CrewVotes.Leader)
        {
            await using var change = new NpgsqlCommand("""
                UPDATE distrito.crew_members SET role = 'member' WHERE crew_id = @crew AND role = 'leader';
                UPDATE distrito.crew_members SET role = 'leader' WHERE crew_id = @crew AND character_id = @t;
                UPDATE distrito.crews SET leader_character_id = @t WHERE id = @crew;
                """, c, tx);
            change.Parameters.AddWithValue("crew", crewId);
            change.Parameters.AddWithValue("t", target);
            await change.ExecuteNonQueryAsync(ct);
            return;
        }

        // Pago de la caja: si ya no alcanza (alguien sacó plata), se paga lo que haya.
        var box = await EnsureCrewAccountAsync(c, tx, crewId, ct);
        var account = await EconomyService.EnsureCharacterAccountAsync(c, tx, target, ct);
        await EconomyService.LockAccountsAsync(c, tx, ct, box, account);
        var pay = Math.Min(amount, await EconomyService.BalanceAsync(c, tx, box, ct));
        if (pay <= 0)
            return;
        var txId = await EconomyService.NewLedgerTransactionAsync(c, tx, $"vote:{voteId}", "crew_vote_payout", "Pago de la caja aprobado por votación", ct);
        await EconomyService.TransferMoneyAsync(c, tx, txId, from: box, to: account, pay, ct);
    }

    private async Task CloseVoteAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, Guid voteId, string status, CancellationToken ct)
    {
        await using (var cmd = new NpgsqlCommand(
            "UPDATE distrito.crew_votes SET status = @s, closed_at = @now WHERE id = @v AND status = 'open'", c, tx))
        {
            cmd.Parameters.AddWithValue("s", status);
            cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
            cmd.Parameters.AddWithValue("v", voteId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await LogAsync(c, tx, crewId, status == "passed" ? "vote_passed" : "vote_failed", null, await DescribeVoteAsync(c, tx, voteId, ct), ct);
    }

    private async Task CloseExpiredVotesAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid crewId, CancellationToken ct)
    {
        Guid? expired = null;
        await using (var cmd = new NpgsqlCommand(
            "SELECT id FROM distrito.crew_votes WHERE crew_id = @crew AND status = 'open' AND closes_at <= @now", c, tx))
        {
            cmd.Parameters.AddWithValue("crew", crewId);
            cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
            expired = await cmd.ExecuteScalarAsync(ct) as Guid?;
        }
        if (expired is { } id)
            await CloseVoteAsync(c, tx, crewId, id, "failed", ct);
    }

    private static async Task<string> DescribeVoteAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid voteId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT v.kind, ch.name, v.amount_cents FROM distrito.crew_votes v JOIN distrito.characters ch ON ch.id = v.target_character_id WHERE v.id = @v
            """, c, tx);
        cmd.Parameters.AddWithValue("v", voteId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return reader.GetString(0) == CrewVotes.Leader
            ? $"que {reader.GetString(1)} sea el nuevo jefe"
            : $"darle {EconomyCatalog.FormatMoney(reader.GetInt64(2))} de la caja a {reader.GetString(1)}";
    }

    /// <summary>La votación abierta, o la última cerrada en las últimas 24 h (para ver el resultado).</summary>
    private async Task<CrewVoteView?> LoadVoteAsync(NpgsqlConnection c, NpgsqlTransaction? tx, Guid crewId, Guid viewer, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT v.id, v.kind, ch.name, v.amount_cents, v.closes_at, v.status,
                   (SELECT count(*)::int FROM distrito.crew_vote_ballots b WHERE b.vote_id = v.id AND b.yes),
                   (SELECT count(*)::int FROM distrito.crew_vote_ballots b WHERE b.vote_id = v.id AND NOT b.yes),
                   (SELECT count(*)::int FROM distrito.crew_members m WHERE m.crew_id = v.crew_id),
                   (SELECT b.yes FROM distrito.crew_vote_ballots b WHERE b.vote_id = v.id AND b.character_id = @viewer)
              FROM distrito.crew_votes v JOIN distrito.characters ch ON ch.id = v.target_character_id
             WHERE v.crew_id = @crew AND (v.status = 'open' OR v.closed_at > @since)
             ORDER BY (v.status = 'open') DESC, v.opened_at DESC LIMIT 1
            """, c, tx);
        cmd.Parameters.AddWithValue("crew", crewId);
        cmd.Parameters.AddWithValue("viewer", viewer);
        cmd.Parameters.AddWithValue("since", clock.GetUtcNow() - CrewVotes.Duration);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new CrewVoteView(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
            reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetFieldValue<DateTimeOffset>(4), reader.GetString(5),
            reader.IsDBNull(9) ? null : reader.GetBoolean(9));
    }
}
