using Cultivation.Domain;

namespace Cultivation.Simulation;

// Public market observations only. No private character inventory enters this interface.
public static class ActorKnowledge
{
    public static KnownMarketFact? ReadMarket(WorldState w,string actorId,string item)
    {
        var mode=w.Time?.EventDecisions??throw new InvalidOperationException("R3 not enabled");
        var actor=w.Characters.Single(a=>a.Id==actorId);long now=w.Time!.DiscreteEvents!.WorldTime;
        string marketId=w.HexMap?.Regional?.Regions.SingleOrDefault(r=>r.Id==actor.RegionId)?.MarketOwner??"market";
        var market=w.Organizations.SingleOrDefault(o=>o.Id==marketId);
        if(market is not null&&market.RegionId==actor.RegionId&&actor.DemoLocationId=="town"&&w.Items.Any(i=>i.Id==item))
        {
            MarketQuote quote;
            if(w.Time?.MarketOrders is {} orders)
            {
                var venue=orders.Settings.Rules.Venues.FirstOrDefault(v=>v.RegionId==actor.RegionId&&v.LocationId==actor.DemoLocationId);
                var book=venue is null?null:MarketOrders.Observe(w,actorId,venue.Id,item);
                if(book is null)return null;
                quote=new(venue!.Id,item,book.Asks.FirstOrDefault()?.Price??checked((long)decimal.Ceiling(book.ReferencePrice)),book.Bids.FirstOrDefault()?.Price??checked((long)decimal.Floor(book.ReferencePrice)),book.Asks.Sum(q=>q.Quantity),w.Clock.Tick,"orderbook:"+venue.Id);
            }
            else quote=MarketPricing.Quote(w,item);
            var fact=new KnownMarketFact(actorId,actor.RegionId,actor.DemoLocationId,quote,now,
                checked(now+checked(w.Rules.MemoryTicks*DiscreteTime.SecondsPerStep(w))),1,"observed:market:"+actor.RegionId);
            if(!mode.MarketKnowledge.TryGetValue(actorId,out var facts))mode.MarketKnowledge.Add(actorId,facts=new(StringComparer.Ordinal));
            facts[item]=fact;return fact;
        }
        if(mode.MarketKnowledge.TryGetValue(actorId,out var known)&&known.TryGetValue(item,out var cached)&&cached.RegionId==actor.RegionId&&cached.ExpiresAt>now)
            return cached;
        return null;
    }
}
