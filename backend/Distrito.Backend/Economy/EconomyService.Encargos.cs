using Distrito.Shared.Economy;
using Npgsql;

namespace Distrito.Backend.Economy;

/// <summary>
/// Encargos personales (propuesta aprobada por el equipo, 2026-11-01). Las ofertas se calculan (EncargoGenerator) y se
/// guardan recién al tomarlas. La paga sale de la cuenta real de la familia que encarga (hasta donde le alcance), igual
/// que los contratos; la mercadería entregada va al stock de la familia. Los sobres y paquetes los crea el encargo.
/// </summary>
public sealed partial class EconomyService
{
    private const string EncargoStock = "encargos";

    /// <summary>Cuenta que paga por cada familia (la misma que en las misiones de cuadrilla y los acuerdos).</summary>
    private static string PayerNpcOf(string family) => family switch
    {
        EconomyCatalog.Moretti => EconomyCatalog.DepositNpc,
        EconomyCatalog.Caruso => EconomyCatalog.WorkshopNpc,
        _ => EconomyCatalog.ValentiOfficeNpc,
    };

    private sealed record EncargoRow(Guid Id, string Kind, string Giver, string Family, string Title, string? Item, int Quantity,
        string? Target, int Progress, int Goal, int Stage, long Reward, int Xp, int Reputation, bool Daily,
        DateTimeOffset AcceptedAt, DateTimeOffset ExpiresAt, DateTimeOffset? FastUntil);

    /// <summary>Encargos para la foto económica: los tomados, los ofrecidos este turno y el del día (si no se tomaron).</summary>
    private async Task<List<EncargoView>> LoadEncargosAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, int level, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var list = new List<EncargoView>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT id, kind, giver_npc, family, title, detail, item_code, quantity, target_npc, progress, target, stage,
                   reward_cents, xp, reputation, status, daily, expires_at, fast_until
              FROM distrito.character_encargos WHERE character_id = @c AND status = 'activo' ORDER BY accepted_at
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var expires = r.GetFieldValue<DateTimeOffset>(17);
                var kind = r.GetString(1);
                list.Add(new EncargoView(r.GetGuid(0), kind, r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetString(6), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetInt32(9),
                    r.GetInt32(10), r.GetInt32(11), r.GetInt64(12), r.GetInt32(13), r.GetInt32(14), r.GetString(15), r.GetBoolean(16),
                    expires, kind == EncargoKinds.Contrarreloj ? expires : null, r.IsDBNull(18) ? null : r.GetFieldValue<DateTimeOffset>(18)));
            }
        }
        var slot = EncargoRules.SlotOf(now);
        var drafts = EncargoGenerator.Generate(characterId, slot, level);
        var daily = EncargoGenerator.Daily(characterId, EncargoRules.DayOf(now), level);
        var taken = new HashSet<Guid>();
        await using (var cmd = new NpgsqlCommand("SELECT id FROM distrito.character_encargos WHERE id = ANY(@ids)", c, tx))
        {
            cmd.Parameters.AddWithValue("ids", drafts.Select(d => d.Id).Append(daily.Id).ToArray());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) taken.Add(r.GetGuid(0));
        }
        list.AddRange(drafts.Where(d => !taken.Contains(d.Id)).Select(d => EncargoGenerator.ToOffer(d, EncargoRules.SlotEnd(slot))));
        if (!taken.Contains(daily.Id))
            list.Add(EncargoGenerator.ToOffer(daily, EncargoRules.DayEnd(now)));
        return list;
    }

    private async Task<EncargoRow?> ActiveEncargoAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, Guid id, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, kind, giver_npc, family, title, item_code, quantity, target_npc, progress, target, stage, reward_cents, xp,
                   reputation, daily, accepted_at, expires_at, fast_until
              FROM distrito.character_encargos WHERE id = @id AND character_id = @c AND status = 'activo' FOR UPDATE
            """, c, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("c", characterId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return new EncargoRow(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
            r.GetInt32(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), r.GetInt64(11), r.GetInt32(12),
            r.GetInt32(13), r.GetBoolean(14), r.GetFieldValue<DateTimeOffset>(15), r.GetFieldValue<DateTimeOffset>(16),
            r.IsDBNull(17) ? null : r.GetFieldValue<DateTimeOffset>(17));
    }

    /// <summary>
    /// Vencimientos: lo que pasó de hora queda vencido. Los objetos del encargo que tengas encima se devuelven; si un sobre de
    /// cobranza ya cobrado no está, se cobra lo que valía (aprobado: «si lo perdés, lo pagás vos»).
    /// </summary>
    private async Task ExpireEncargosAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, CancellationToken ct)
    {
        var expired = new List<Guid>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT id FROM distrito.character_encargos WHERE character_id = @c AND status = 'activo' AND expires_at <= @now
            """, c, tx))
        {
            cmd.Parameters.AddWithValue("c", characterId);
            cmd.Parameters.AddWithValue("now", clock.GetUtcNow());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) expired.Add(r.GetGuid(0));
        }
        foreach (var id in expired)
            if (await ActiveEncargoAsync(c, tx, characterId, id, ct) is { } row)
                await CloseEncargoAsync(c, tx, characterId, row, EncargoStatus.Expired, $"encargo-vence:{id}", ct);
    }

    /// <summary>Cierra un encargo sin cumplirlo (vencido o abandonado) y limpia sus objetos.</summary>
    private async Task<string> CloseEncargoAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, EncargoRow row, string status,
        string idempotency, CancellationToken ct)
    {
        var txId = await NewLedgerTransactionAsync(c, tx, idempotency, "encargo", $"Encargo {status}: {row.Title}", ct);
        var me = await EnsureCharacterAccountAsync(c, tx, characterId, ct);
        var payer = await AccountIdByCodeAsync(c, tx, PayerNpcOf(row.Family), ct);
        var note = "";
        foreach (var item in MissionItemsOf(row))
        {
            var have = await CarriedQuantityAsync(c, tx, me, item, ct);
            if (have > 0)
                await MoveItemsAsync(c, tx, txId, item, Math.Min(have, 1), from: (me, EconomyCatalog.CarriedLocation), to: (payer, EncargoStock), "", ct, actor: characterId);
            else if (item == EncargoRules.CollectionEnvelope)
            {
                // El sobre cobrado se perdió: se paga lo que valía.
                await LockAccountsAsync(c, tx, ct, me, payer);
                var charge = Math.Min(row.Reward, await BalanceAsync(c, tx, me, ct));
                if (charge > 0)
                    await TransferMoneyAsync(c, tx, txId, from: me, to: payer, charge, ct);
                note = $" Perdiste el sobre de la cobranza: se te cobraron {EconomyCatalog.FormatMoney(charge)}.";
            }
        }
        await using var update = new NpgsqlCommand("UPDATE distrito.character_encargos SET status = @s, finished_at = @now WHERE id = @id", c, tx);
        update.Parameters.AddWithValue("s", status);
        update.Parameters.AddWithValue("now", clock.GetUtcNow());
        update.Parameters.AddWithValue("id", row.Id);
        await update.ExecuteNonQueryAsync(ct);
        return note;
    }

    /// <summary>Objetos que el encargo puso en tus manos en el paso actual (y que se van con él).</summary>
    private static IEnumerable<string> MissionItemsOf(EncargoRow row) => row.Kind switch
    {
        EncargoKinds.Recado when row.Stage == 0 => [EncargoRules.SealedEnvelope],
        EncargoKinds.Cobro when row.Stage == 1 => [EncargoRules.CollectionEnvelope],
        EncargoKinds.Paquete when row.Stage == 1 => [EncargoRules.SealedPackage],
        _ => [],
    };

    private static async Task<int> CarriedQuantityAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid account, string item, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT COALESCE(sum(quantity), 0)::int FROM distrito.item_stacks WHERE owner_account_id = @a AND item_code = @i AND location = @l
            """, c, tx);
        cmd.Parameters.AddWithValue("a", account);
        cmd.Parameters.AddWithValue("i", item);
        cmd.Parameters.AddWithValue("l", EconomyCatalog.CarriedLocation);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    /// <summary>Crea un objeto de encargo en la cuenta de la familia y te lo pasa (queda en el libro como un movimiento).</summary>
    private static async Task GiveMissionItemAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid txId, Guid from, Guid to, string item, Guid actor, CancellationToken ct)
    {
        await using (var mint = new NpgsqlCommand("""
            INSERT INTO distrito.item_stacks (id, owner_account_id, item_code, location, quantity) VALUES (@id, @a, @i, @l, 1)
            ON CONFLICT (owner_account_id, item_code, location) DO UPDATE SET quantity = distrito.item_stacks.quantity + 1
            """, c, tx))
        {
            mint.Parameters.AddWithValue("id", Guid.CreateVersion7());
            mint.Parameters.AddWithValue("a", from);
            mint.Parameters.AddWithValue("i", item);
            mint.Parameters.AddWithValue("l", EncargoStock);
            await mint.ExecuteNonQueryAsync(ct);
        }
        await MoveItemsAsync(c, tx, txId, item, 1, from: (from, EncargoStock), to: (to, EconomyCatalog.CarriedLocation), "", ct, actor: actor);
    }

    private static string NameOfNpc(string code) => code == EncargoRules.DocksContact ? "Gaetano, el marinero"
        : Distrito.Shared.Economy.FamilyPeople.Find(code)?.Name ?? code switch
        {
            EconomyCatalog.DepositNpc => "el Almacén Moretti",
            EconomyCatalog.WorkshopNpc => "el Taller Caruso",
            EconomyCatalog.ValentiOfficeNpc => "la Oficina Valenti",
            EconomyCatalog.CoffeeCornerNpc => "la Esquina del café",
            EconomyCatalog.JazzClubNpc => "el Club de jazz",
            CasinoRules.Npc => "el portero de El Trébol",
            EconomyCatalog.MorettiBossNpc => "Don Moretti",
            EconomyCatalog.ValentiBossNpc => "Don Valenti",
            EconomyCatalog.CarusoBossNpc => "Don Caruso",
            _ => code,
        };

    /// <summary>Aceptar un encargo con quien lo da, o hacer el paso que toca en el lugar (el servidor de juego validó la presencia).</summary>
    public Task<EconomyActionResult> EncargoActionAsync(EncargoActionRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            await ExpireEncargosAsync(c, tx, r.CharacterId, ct);
            return r.Action switch
            {
                "aceptar" => await AcceptEncargoAsync(c, tx, r, ct),
                "paso" => await EncargoStepAsync(c, tx, r, ct),
                _ => throw new EconomyRejected("invalid_action", "Acción de encargo desconocida."),
            };
        });

    private async Task<string> AcceptEncargoAsync(NpgsqlConnection c, NpgsqlTransaction tx, EncargoActionRequest r, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var level = (await LoadProgressAsync(c, tx, r.CharacterId, ct)).Level;
        var drafts = EncargoGenerator.Generate(r.CharacterId, EncargoRules.SlotOf(now), level).Append(EncargoGenerator.Daily(r.CharacterId, EncargoRules.DayOf(now), level));
        var d = drafts.FirstOrDefault(x => x.Id == r.EncargoId) ?? throw new EconomyRejected("not_offered", "Ese encargo ya no se ofrece (se renuevan cada 2 horas).");
        if (d.GiverNpc != r.NpcCode)
            throw new EconomyRejected("wrong_npc", $"Ese encargo lo da {NameOfNpc(d.GiverNpc)}.");
        if (await ExistsAsync(c, tx, "SELECT 1 FROM distrito.character_encargos WHERE id = @k::uuid", d.Id.ToString(), ct))
            throw new EconomyRejected("taken", "Ese encargo ya lo tomaste.");
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM distrito.character_encargos WHERE character_id = @c AND status = 'activo'", c, tx))
        {
            count.Parameters.AddWithValue("c", r.CharacterId);
            if ((long)(await count.ExecuteScalarAsync(ct) ?? 0L) >= EncargoRules.MaxActive)
                throw new EconomyRejected("too_many", $"Ya tenés {EncargoRules.MaxActive} encargos en curso. Terminá o abandoná uno (libreta, L).");
        }
        var expires = d.Daily ? EncargoRules.DayEnd(now) : now + EncargoRules.ActiveFor;
        DateTimeOffset? fast = null;
        if (d.TimeLimitSeconds > 0)
        {
            expires = now.AddSeconds(d.TimeLimitSeconds);
            fast = now.AddSeconds(d.TimeLimitSeconds / 2.0);
        }
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO distrito.character_encargos (id, character_id, kind, giver_npc, family, title, detail, item_code, quantity, target_npc,
                target, reward_cents, xp, reputation, daily, status, accepted_at, expires_at, fast_until)
            VALUES (@id, @c, @kind, @giver, @family, @title, @detail, @item, @q, @target, @goal, @reward, @xp, @rep, @daily, 'activo', @now, @exp, @fast)
            """, c, tx))
        {
            insert.Parameters.AddWithValue("id", d.Id);
            insert.Parameters.AddWithValue("c", r.CharacterId);
            insert.Parameters.AddWithValue("kind", d.Kind);
            insert.Parameters.AddWithValue("giver", d.GiverNpc);
            insert.Parameters.AddWithValue("family", d.Family);
            insert.Parameters.AddWithValue("title", d.Title);
            insert.Parameters.AddWithValue("detail", d.Detail);
            insert.Parameters.AddWithValue("item", (object?)d.ItemCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("q", d.Quantity);
            insert.Parameters.AddWithValue("target", (object?)d.TargetNpc ?? DBNull.Value);
            insert.Parameters.AddWithValue("goal", d.Target);
            insert.Parameters.AddWithValue("reward", d.RewardCents);
            insert.Parameters.AddWithValue("xp", d.Xp);
            insert.Parameters.AddWithValue("rep", d.Reputation);
            insert.Parameters.AddWithValue("daily", d.Daily);
            insert.Parameters.AddWithValue("now", now);
            insert.Parameters.AddWithValue("exp", expires);
            insert.Parameters.AddWithValue("fast", (object?)fast ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(ct);
        }
        var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "encargo", $"Encargo tomado: {d.Title}", ct);
        if (d.Kind == EncargoKinds.Recado)
        {
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var payer = await AccountIdByCodeAsync(c, tx, PayerNpcOf(d.Family), ct);
            await GiveMissionItemAsync(c, tx, txId, payer, me, EncargoRules.SealedEnvelope, r.CharacterId, ct);
        }
        var view = new EncargoView(d.Id, d.Kind, d.GiverNpc, d.Family, d.Title, d.Detail, d.ItemCode, d.Quantity, d.TargetNpc, 0, d.Target, 0,
            d.RewardCents, d.Xp, d.Reputation, EncargoStatus.Active, d.Daily, expires);
        var limit = d.TimeLimitSeconds > 0 ? $" Tenés {d.TimeLimitSeconds / 60} minutos." : "";
        return $"Encargo tomado: {d.Title}.{limit}\nAhora: {view.StepText(NameOfNpc)}";
    }

    private async Task<string> EncargoStepAsync(NpgsqlConnection c, NpgsqlTransaction tx, EncargoActionRequest r, CancellationToken ct)
    {
        var row = await ActiveEncargoAsync(c, tx, r.CharacterId, r.EncargoId, ct)
            ?? throw new EconomyRejected("not_active", "Ese encargo ya no está en curso.");
        var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
        var payer = await AccountIdByCodeAsync(c, tx, PayerNpcOf(row.Family), ct);
        var txId = await NewLedgerTransactionAsync(c, tx, r.RequestId, "encargo", $"Encargo: {row.Title}", ct);
        void At(string npc)
        {
            if (r.NpcCode != npc)
                throw new EconomyRejected("wrong_npc", $"Esto se hace con {NameOfNpc(npc)}.");
        }

        if (row.Stage == 0)
        {
            switch (row.Kind)
            {
                case EncargoKinds.Entrega or EncargoKinds.Contrarreloj:
                    At(row.Giver);
                    await LockAccountsAsync(c, tx, ct, me, payer);
                    await MoveItemsAsync(c, tx, txId, row.Item!, row.Quantity, from: (me, EconomyCatalog.CarriedLocation),
                        to: (payer, StockLocation(PayerNpcOf(row.Family))),
                        notEnough: $"Te faltan: hacen falta {row.Quantity} × {EncargoRules.ItemName(row.Item)} encima.", ct, actor: r.CharacterId);
                    return await CompleteEncargoAsync(c, tx, txId, r.CharacterId, row, ct);
                case EncargoKinds.Reparto:
                {
                    At(row.Giver);
                    var crew = await CrewOfCharacterAsync(c, tx, r.CharacterId, ct);
                    (Guid Id, Guid Account)? found = null;
                    await using (var cmd = new NpgsqlCommand("""
                        SELECT v.id, v.account_id FROM distrito.vehicles v
                         WHERE v.id = ANY(@ids) AND (v.owner_character_id = @c OR (@crew::uuid IS NOT NULL AND v.owner_crew_id = @crew))
                           AND COALESCE((SELECT sum(quantity) FROM distrito.item_stacks s
                                          WHERE s.owner_account_id = v.account_id AND s.location = 'vehiculo:' || v.id::text AND s.item_code = @item), 0) >= @q
                         LIMIT 1
                        """, c, tx))
                    {
                        cmd.Parameters.AddWithValue("ids", (r.VehiclesNear ?? []).ToArray());
                        cmd.Parameters.AddWithValue("c", r.CharacterId);
                        cmd.Parameters.AddWithValue("crew", (object?)crew ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("item", row.Item!);
                        cmd.Parameters.AddWithValue("q", row.Quantity);
                        await using var reader = await cmd.ExecuteReaderAsync(ct);
                        if (await reader.ReadAsync(ct))
                            found = (reader.GetGuid(0), reader.GetGuid(1));
                    }
                    if (found is not { } v)
                        throw new EconomyRejected("no_vehicle",
                            $"Estacioná acá cerca un vehículo tuyo o de tu cuadrilla con {row.Quantity} × {EncargoRules.ItemName(row.Item)} cargados.");
                    await LockAccountsAsync(c, tx, ct, v.Account, payer);
                    await MoveItemsAsync(c, tx, txId, row.Item!, row.Quantity, from: (v.Account, Vehicles.Location(v.Id)),
                        to: (payer, StockLocation(PayerNpcOf(row.Family))), notEnough: "La carga no alcanza.", ct, actor: r.CharacterId);
                    return await CompleteEncargoAsync(c, tx, txId, r.CharacterId, row, ct);
                }
                case EncargoKinds.Cobro:
                {
                    At(row.Target!);
                    await GiveMissionItemAsync(c, tx, txId, payer, me, EncargoRules.CollectionEnvelope, r.CharacterId, ct);
                    var debtor = FamilyRules.FamilyOfNpc(row.Target!);
                    if (debtor is not null && debtor != row.Family)
                        await GrantReputationAsync(c, tx, r.CharacterId, new ReputationReward(debtor, -EncargoRules.RivalPenalty), $"cobro para los {row.Family}", txId, ct);
                    await SetStageAsync(c, tx, row.Id, 1, row.Goal, ct);
                    return $"Pagaron, de mala gana. Llevale el sobre a {NameOfNpc(row.Giver)}."
                           + (debtor is not null && debtor != row.Family ? $" (−{EncargoRules.RivalPenalty} de reputación con los {EncargoGenerator.FamilyName(debtor)})" : "");
                }
                case EncargoKinds.Paquete:
                    At(row.Target!);
                    await GiveMissionItemAsync(c, tx, txId, payer, me, EncargoRules.SealedPackage, r.CharacterId, ct);
                    await SetStageAsync(c, tx, row.Id, 1, row.Goal, ct);
                    return $"Gaetano te da un paquete envuelto en arpillera. «No lo abras y no te pares.» Llevaselo a {NameOfNpc(row.Giver)}.";
                case EncargoKinds.Recado:
                {
                    At(row.Target!);
                    var receiver = await AccountIdByCodeAsync(c, tx, PayerNpcOf(FamilyRules.FamilyOfNpc(row.Target!) ?? row.Family), ct);
                    await LockAccountsAsync(c, tx, ct, me, receiver);
                    await MoveItemsAsync(c, tx, txId, EncargoRules.SealedEnvelope, 1, from: (me, EconomyCatalog.CarriedLocation), to: (receiver, EncargoStock),
                        notEnough: "No tenés el sobre: sin el sobre no hay recado.", ct, actor: r.CharacterId);
                    return await CompleteEncargoAsync(c, tx, txId, r.CharacterId, row, ct);
                }
                default:
                    throw new EconomyRejected("not_yet", $"Todavía no: {row.Progress}/{row.Goal}.");
            }
        }

        // Paso final: volver con quien lo encargó.
        At(row.Giver);
        var proof = row.Kind switch
        {
            EncargoKinds.Cobro => EncargoRules.CollectionEnvelope,
            EncargoKinds.Paquete => EncargoRules.SealedPackage,
            _ => null,
        };
        if (proof is not null)
        {
            await LockAccountsAsync(c, tx, ct, me, payer);
            await MoveItemsAsync(c, tx, txId, proof, 1, from: (me, EconomyCatalog.CarriedLocation), to: (payer, EncargoStock),
                notEnough: $"No tenés el {EncargoRules.ItemName(proof)}. Si lo perdiste, al vencer el encargo se cobra.", ct, actor: r.CharacterId);
        }
        return await CompleteEncargoAsync(c, tx, txId, r.CharacterId, row, ct);
    }

    private static async Task SetStageAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid id, int stage, int progress, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE distrito.character_encargos SET stage = @s, progress = @p WHERE id = @id", c, tx);
        cmd.Parameters.AddWithValue("s", stage);
        cmd.Parameters.AddWithValue("p", progress);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Paga, experiencia y reputación de un encargo cumplido (en la transacción que lo cumple).</summary>
    private async Task<string> CompleteEncargoAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid txId, Guid characterId, EncargoRow row, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var fast = row.FastUntil is { } until && now <= until;
        var reward = fast ? row.Reward * (100 + EncargoRules.FastBonusPercent) / 100 : row.Reward;
        var me = await EnsureCharacterAccountAsync(c, tx, characterId, ct);
        var payer = await AccountIdByCodeAsync(c, tx, PayerNpcOf(row.Family), ct);
        await LockAccountsAsync(c, tx, ct, me, payer);
        var paid = Math.Min(reward, await BalanceAsync(c, tx, payer, ct));
        if (paid > 0)
            await TransferMoneyAsync(c, tx, txId, from: payer, to: me, paid, ct);
        var before = (await LoadProgressAsync(c, tx, characterId, ct)).Level;
        await GrantProgressAsync(c, tx, characterId, row.Xp, 0, 0, 0, ct);
        await GrantReputationAsync(c, tx, characterId, new ReputationReward(row.Family, row.Reputation), $"encargo {row.Kind}", txId, ct);
        await using (var done = new NpgsqlCommand("""
            UPDATE distrito.character_encargos SET status = 'cumplido', finished_at = @now, paid_cents = @paid, stage = 1, progress = target WHERE id = @id
            """, c, tx))
        {
            done.Parameters.AddWithValue("now", now);
            done.Parameters.AddWithValue("paid", paid);
            done.Parameters.AddWithValue("id", row.Id);
            await done.ExecuteNonQueryAsync(ct);
        }
        var after = (await LoadProgressAsync(c, tx, characterId, ct)).Level;
        var family = EncargoGenerator.FamilyName(row.Family);
        var text = $"¡Encargo cumplido! {row.Title}\nCobraste {EconomyCatalog.FormatMoney(paid)}{(fast ? $" (con +{EncargoRules.FastBonusPercent} % por llegar rápido)" : "")}, "
                   + $"+{row.Xp} de experiencia y +{row.Reputation} de reputación con los {family}.";
        if (after > before)
            text += $"\n¡Subiste a nivel {after}!";
        var rep = await ReputationOfAsync(c, tx, characterId, row.Family, ct);
        if (rep >= FamilyPeople.BossReputation && rep - row.Reputation < FamilyPeople.BossReputation)
            text += $"\n{NameOfNpc(FamilyPeople.BossOf(row.Family))} quiere conocerte. Te espera en el cuartel.";
        return text;
    }

    private static async Task<int> ReputationOfAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid characterId, string family, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT value FROM distrito.character_reputation WHERE character_id = @c AND family_code = @f", c, tx);
        cmd.Parameters.AddWithValue("c", characterId);
        cmd.Parameters.AddWithValue("f", family);
        return await cmd.ExecuteScalarAsync(ct) is int v ? v : 0;
    }

    /// <summary>Abandonar un encargo desde la libreta: sin multa (salvo el sobre de una cobranza que ya no tengas).</summary>
    public Task<EconomyActionResult> AbandonEncargoAsync(EncargoAbandonRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            var row = await ActiveEncargoAsync(c, tx, r.CharacterId, r.EncargoId, ct)
                ?? throw new EconomyRejected("not_active", "Ese encargo ya no está en curso.");
            var note = await CloseEncargoAsync(c, tx, r.CharacterId, row, EncargoStatus.Abandoned, r.RequestId, ct);
            return $"Dejaste el encargo «{row.Title}».{note}";
        });

    /// <summary>Lo que pasó en el mundo: un asalto (Target = camion, cobrador o el local) o medio minuto de vigilancia.</summary>
    public Task<EconomyActionResult> EncargoProgressAsync(EncargoProgressRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, r.RequestId, ct, async (c, tx) =>
        {
            if (!EncargoKinds.IsEvent(r.Kind) || r.Amount <= 0)
                return "";
            Guid? id = null;
            await using (var find = new NpgsqlCommand("""
                SELECT id FROM distrito.character_encargos
                 WHERE character_id = @c AND kind = @k AND status = 'activo' AND stage = 0 AND expires_at > @now
                   AND (@t::text IS NULL OR target_npc = @t) ORDER BY accepted_at LIMIT 1
                """, c, tx))
            {
                find.Parameters.AddWithValue("c", r.CharacterId);
                find.Parameters.AddWithValue("k", r.Kind);
                find.Parameters.AddWithValue("now", clock.GetUtcNow());
                find.Parameters.AddWithValue("t", (object?)r.Target ?? DBNull.Value);
                id = await find.ExecuteScalarAsync(ct) as Guid?;
            }
            if (id is null || await ActiveEncargoAsync(c, tx, r.CharacterId, id.Value, ct) is not { } row)
                return "";
            var now = Math.Min(row.Goal, row.Progress + r.Amount);
            await SetStageAsync(c, tx, row.Id, now >= row.Goal ? 1 : 0, now, ct);
            if (now < row.Goal)
                return row.Kind == EncargoKinds.Vigilar ? "" : $"Encargo «{row.Title}»: {now}/{row.Goal}.";
            return row.Kind == EncargoKinds.Vigilar
                ? $"Ya viste suficiente. Volvé con {NameOfNpc(row.Giver)} a contarle."
                : $"¡Hecho! Volvé con {NameOfNpc(row.Giver)} a cobrar el encargo.";
        });
}
