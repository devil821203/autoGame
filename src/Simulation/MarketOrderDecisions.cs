using Cultivation.AI;
using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class MarketOrderDecisions
{
    internal static ScoredAction? Map(WorldState w,CharacterState actor,ScoredAction candidate)
    {
        var i=candidate.Intent;
        if(w.Time?.MarketOrders is not {} m||i.Kind is not (ActionKind.Buy or ActionKind.Sell))return candidate;
        var venue=m.Settings.Rules.Venues.FirstOrDefault(v=>v.RegionId==actor.RegionId&&v.LocationId==actor.DemoLocationId);
        if(venue is null||!m.Settings.BasePrices.ContainsKey(i.ResourceId))return null;
        if(m.Orders.Values.Any(o=>o.OwnerId==actor.Id&&o.VenueId==venue.Id&&o.ResourceId==i.ResourceId&&o.Side==(i.Kind==ActionKind.Buy?MarketOrderSide.Buy:MarketOrderSide.Sell)&&o.Phase is MarketOrderPhase.Open or MarketOrderPhase.Submitting))
            return null;
        var book=MarketOrders.Observe(w,actor.Id,venue.Id,i.ResourceId)!;
        long price=i.Kind==ActionKind.Buy?book.Asks.FirstOrDefault()?.Price??checked((long)decimal.Ceiling(book.ReferencePrice)):book.Bids.FirstOrDefault()?.Price??checked((long)decimal.Floor(book.ReferencePrice));
        return candidate with {Intent=i with {TargetId=venue.Id,Kind=i.Kind==ActionKind.Buy?ActionKind.PlaceMarketBuy:ActionKind.PlaceMarketSell,LimitPrice=price,DurationTicks=0,MissionId=null,Plan=null},Reason=candidate.Reason+"；依本地公開訂單簿掛限價單"};
    }
}
