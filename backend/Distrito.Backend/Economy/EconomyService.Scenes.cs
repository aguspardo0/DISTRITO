using Distrito.Shared.Economy;

namespace Distrito.Backend.Economy;

/// <summary>
/// Escenas de la ciudad con las que se puede interactuar (extorsión, tiroteo, persecución). Dan reputación y, en la
/// extorsión, un regalito del stock del local de la familia que cuida la zona. Una vez por escena y personaje.
/// </summary>
public sealed partial class EconomyService
{
    public const int SceneRewardCap = 20;

    public Task<EconomyActionResult> SceneRewardAsync(SceneRewardRequest r, CancellationToken ct) =>
        RunAsync(r.CharacterId, $"escena:{r.SceneId}:{r.CharacterId}", ct, async (c, tx) =>
        {
            if (r.Kind is not ("extorsion" or "tiroteo" or "persecucion") || r.Reason.Length is 0 or > 160 || r.Reputation.Count is 0 or > 3 || r.Reputation.Any(d => Math.Abs(d.Amount) > SceneRewardCap || !IsFamily(d.Family)))
                throw new EconomyRejected("invalid_request", "Premio fuera de rango.");
            var me = await EnsureCharacterAccountAsync(c, tx, r.CharacterId, ct);
            var txId = await NewLedgerTransactionAsync(c, tx, $"escena:{r.SceneId}:{r.CharacterId}", $"scene_{r.Kind}", r.Reason, ct);
            var parts = new List<string>();
            foreach (var d in r.Reputation.Where(d => d.Amount != 0))
            {
                await GrantReputationAsync(c, tx, r.CharacterId, new ReputationReward(d.Family, d.Amount), r.Reason, txId, ct);
                parts.Add($"{(d.Amount > 0 ? "+" : "")}{d.Amount} con los {EconomyCatalog.FamilyName(d.Family)}");
            }
            if (r.GiftFrom is { } family && IsFamily(family))
            {
                var shopCode = PayerNpcOf(family);
                var shop = await AccountIdByCodeAsync(c, tx, shopCode, ct);
                await LockAccountsAsync(c, tx, ct, me, shop);
                // Lo más barato y legal que tenga en el estante: un gesto, no un pago.
                var gift = (await EntriesAtAsync(c, tx, shop, StockLocation(shopCode), ct))
                    .Where(i => i.LegalStatus == "legal" && i.Quantity > 0)
                    .OrderBy(i => i.ReferencePriceCents).FirstOrDefault();
                if (gift is not null)
                {
                    await MoveItemsAsync(c, tx, txId, gift.ItemCode, 1, from: (shop, StockLocation(shopCode)), to: (me, EconomyCatalog.CarriedLocation),
                        notEnough: "", ct, actor: r.CharacterId);
                    parts.Add($"te regalaron 1 × {gift.Name.ToLowerInvariant()}");
                }
            }
            return parts.Count > 0 ? $"Se corrió la voz: {string.Join(", ", parts)}." : "Se corrió la voz.";
        });

    private static bool IsFamily(string code) => code is EconomyCatalog.Moretti or EconomyCatalog.Valenti or EconomyCatalog.Caruso;
}
