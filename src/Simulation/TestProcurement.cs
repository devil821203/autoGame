using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class TestProcurement
{
    internal static void Advance(WorldState w)
    {
        if (w.TestProcurement is not { } r) return;
        foreach (string item in new[] { r.PrimaryResourceId, "beast-hide" })
        {
            var quote = w.ProcurementQuotes.SingleOrDefault(q => q.ResourceId == item) ??
                new(item, item == r.PrimaryResourceId ? r.PrimaryInitialPrice ?? r.PillInitialPrice : r.HideInitialPrice);
            w.ProcurementQuotes.RemoveAll(q => q.ResourceId == item);
            long reserve = w.SectFacilities is { } facility && facility.OrganizationId == r.SellerId
                ? item == "herb" ? facility.ReservedHerbs : item == "pill" ? facility.ReservedPills : 0 : 0;
            long amount = Math.Min(Math.Max(0, Inventory.Balance(w, r.SellerId, item) - reserve), (long.MaxValue - Inventory.Balance(w, r.SellerId, "spirit-stone")) / quote.UnitPrice);
            amount = Math.Min(amount, long.MaxValue - Inventory.Balance(w, r.BuyerId, item));
            amount = Math.Min(amount, long.MaxValue - quote.ReceivedThisPeriod);
            long payment = amount * quote.UnitPrice;
            if (amount > 0 && InventoryCapacity.CanReceive(w, r.BuyerId, "spirit-stone", payment))
            {
                string id = $"test-procurement:{item}:{w.Clock.Tick}";
                var intent = new ActionIntent(id, r.SellerId, r.BuyerId, item, amount, 0, DecisionSource.Npc);
                ActionExecutor.Transfer(w, intent with { ResourceId = "spirit-stone", Quantity = payment }, "$test-procurement", r.BuyerId, "TestProcurementFunding");
                ActionExecutor.Transfer(w, intent, r.SellerId, r.BuyerId, "TestProcurementGoods");
                ActionExecutor.Transfer(w, intent with { ResourceId = "spirit-stone", Quantity = payment }, r.BuyerId, r.SellerId, "TestProcurementPayment");
                w.ProcurementReceipts.Add(new(id, w.Clock.Tick, item, amount, quote.UnitPrice, payment));
                quote = quote with { ReceivedThisPeriod = quote.ReceivedThisPeriod + amount };
                ActionExecutor.Emit(w, id, "TestProcurementBought", $"{item}:{amount}@{quote.UnitPrice}");
            }
            if (w.Clock.Tick % r.IntervalTicks == 0)
            {
                long change = Math.Max(1, quote.UnitPrice * r.PriceChangePercent / 100);
                long price = quote.ReceivedThisPeriod < r.DemandPerItem ? Math.Min(r.MaximumPrice, quote.UnitPrice + change) :
                    quote.ReceivedThisPeriod > r.DemandPerItem ? Math.Max(r.MinimumPrice, quote.UnitPrice - change) : quote.UnitPrice;
                ActionExecutor.Emit(w, "procurement-price:" + w.Clock.Tick, "ProcurementPriceReviewed", $"{item}:received={quote.ReceivedThisPeriod}/{r.DemandPerItem},price={quote.UnitPrice}->{price}");
                quote = quote with { UnitPrice = price, ReceivedThisPeriod = 0 };
            }
            w.ProcurementQuotes.Add(quote);
        }
    }
}
