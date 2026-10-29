using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class TestEconomyIntegrity
{
    internal static void Validate(WorldState w)
    {
        if (w.SectDuty is { MissionLifetimeTicks: < 0 }) throw new InvalidDataException("Invalid mission lifetime");
        if (w.SectDuty is { } d && (d.MissionIntervalTicks <= 0 || d.ReviewIntervalTicks <= 0 || d.MissLimit is < 1 or > 100 ||
            d.PillQuantity is < 1 or > 1000000 || d.HideQuantity is < 1 or > 1000000 || d.PillReward is < 1 or > 1000000 || d.HideReward is < 1 or > 1000000 ||
            w.SocialRules is null || !w.Organizations.Any(o => o.Id == d.OrganizationId))) throw new InvalidDataException("Invalid sect duty rules");
        if (w.MemberDuties.Select(x => (x.OrganizationId, x.ActorId)).Distinct().Count() != w.MemberDuties.Count ||
            w.MemberDuties.Any(x => w.SectDuty is null || x.OrganizationId != w.SectDuty.OrganizationId || x.JoinedTick < 0 || x.LastReviewTick < x.JoinedTick || x.LastReviewTick > w.Clock.Tick ||
                x.Misses < 0 || x.Misses >= w.SectDuty.MissLimit || !w.Characters.Any(c => c.Id == x.ActorId && c.OrganizationId == x.OrganizationId))) throw new InvalidDataException("Invalid member duty state");
        var r = w.TestProcurement;
        if (r is null)
        {
            if (w.ProcurementQuotes.Count > 0 || w.ProcurementReceipts.Count > 0) throw new InvalidDataException("Procurement state without rules");
            return;
        }
        if (r.BuyerId == r.SellerId || !w.Organizations.Any(o => o.Id == r.BuyerId) || !w.Organizations.Any(o => o.Id == r.SellerId) || r.IntervalTicks <= 0 || r.DemandPerItem <= 0 ||
            r.PriceChangePercent is < 1 or > 100 || r.MinimumPrice < 1 || r.MaximumPrice < r.MinimumPrice || r.MaximumPrice > 1000000 ||
            r.PrimaryResourceId is not ("pill" or "herb") || (r.PrimaryInitialPrice ?? r.PillInitialPrice) < r.MinimumPrice || (r.PrimaryInitialPrice ?? r.PillInitialPrice) > r.MaximumPrice || r.HideInitialPrice < r.MinimumPrice || r.HideInitialPrice > r.MaximumPrice ||
            w.ProcurementQuotes.Select(q => q.ResourceId).Distinct().Count() != w.ProcurementQuotes.Count ||
            w.ProcurementQuotes.Any(q => q.ResourceId != r.PrimaryResourceId && q.ResourceId != "beast-hide" || q.UnitPrice < r.MinimumPrice || q.UnitPrice > r.MaximumPrice || q.ReceivedThisPeriod < 0)) throw new InvalidDataException("Invalid procurement rules or quotes");
        if (w.ProcurementReceipts.Select(x => x.Id).Distinct().Count() != w.ProcurementReceipts.Count) throw new InvalidDataException("Duplicate procurement receipt");
        var procurementLedger = w.Ledger.ToLookup(e => e.ActionId, StringComparer.Ordinal);
        foreach (var receipt in w.ProcurementReceipts)
        {
            if (receipt.ResourceId != r.PrimaryResourceId && receipt.ResourceId != "beast-hide" || receipt.Tick < 1 || receipt.Tick > w.Clock.Tick || receipt.Quantity <= 0 || receipt.UnitPrice < r.MinimumPrice || receipt.UnitPrice > r.MaximumPrice ||
                receipt.Quantity > long.MaxValue / receipt.UnitPrice || receipt.Payment != receipt.Quantity * receipt.UnitPrice) throw new InvalidDataException("Invalid procurement receipt");
            bool Entry(string from, string to, string resource, long quantity, string reason) => procurementLedger[receipt.Id].Count(e => e.Tick == receipt.Tick && e.From == from && e.To == to && e.ResourceId == resource && e.Quantity == quantity && e.Reason == reason) == 1;
            if (!Entry("$test-procurement", r.BuyerId, "spirit-stone", receipt.Payment, "TestProcurementFunding") ||
                !Entry(r.SellerId, r.BuyerId, receipt.ResourceId, receipt.Quantity, "TestProcurementGoods") ||
                !Entry(r.BuyerId, r.SellerId, "spirit-stone", receipt.Payment, "TestProcurementPayment")) throw new InvalidDataException("Procurement receipt does not match ledger");
        }
    }
}
