using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class MarketOrders
{
    internal static string Account(string id)=>"$order:"+id;
    internal static string CommandKey(string id)=>"$market-command:"+id;
    internal static string ExpiryKey(string id)=>"$market-expiry:"+id;
    internal static long Now(WorldState w)=>w.Time!.DiscreteEvents!.WorldTime;
    public static bool Handles(ActionKind k)=>k is ActionKind.PlaceMarketBuy or ActionKind.PlaceMarketSell or ActionKind.CancelMarketOrder;
    public static void EnableFresh(WorldState w,GameConfiguration? configuration=null)
    {
        if(w.Time?.Content is null||w.Time.MarketOrders is not null||Now(w)!=0||w.Time.Charges.Count!=0||w.Pending.Count!=0)throw new InvalidOperationException("R5 requires a fresh R4 world; no old save conversion");
        var config=configuration??GameConfiguration.Find();
        w.Time.MarketOrders=new(){Settings=new(config.Read<OrderBookRules>("economy/order-book.json"),ExternalMarketActions.BasePrices(w).Where(p=>w.Items.Any(i=>i.Id==p.Key)).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal))};
        MarketOrderIntegrity.Validate(w);
    }
    internal static long Duration(WorldState w)=>checked(w.Time!.MarketOrders!.Settings.Rules.OperationMinutes*60L);
    internal static string? Eligibility(WorldState w,ActionIntent i,MarketVenue venue)
    {
        var actor=w.Characters.Single(a=>a.Id==i.ActorId);
        if(actor.Health<=0||actor.RegionId!=venue.RegionId||actor.DemoLocationId!=venue.LocationId||BoardBattleActions.Participant(w,actor.Id)||CombatActions.Active(w,actor.Id) is not null)return "Order requires a living actor in the same-region town outside combat";
        if(i.OrganizationId is {} org&&(!w.Organizations.Any(o=>o.Id==org&&o.LeaderId==actor.Id)||actor.OrganizationId!=org))return "Organization order requires its own leader";
        return null;
    }
    internal static string? Validate(WorldState w,ActionIntent i)
    {
        if(w.Time?.MarketOrders is not {} market)return "R5 order book not enabled";
        if(i.DurationTicks!=0||i.MissionId is not null||i.Offer is not null||i.Policy is not null||i.StockReservation is not null||i.SocialOfferId is not null)return "Unexpected order payload";
        if(market.Commands.Any(c=>c.Intent.ActorId==i.ActorId))return "Actor busy submitting market command";
        if(i.Kind==ActionKind.CancelMarketOrder)
        {
            if(!market.Orders.TryGetValue(i.TargetId,out var order)||order.Phase!=MarketOrderPhase.Open||order.OwnerId!=(i.OrganizationId??i.ActorId)||i.ResourceId!=order.ResourceId||i.Quantity!=1||i.LimitPrice is not null)return "Order cancellation not owned, open, or correctly formed";
            return Eligibility(w,i,market.Settings.Rules.Venues.Single(v=>v.Id==order.VenueId));
        }
        var venue=market.Settings.Rules.Venues.SingleOrDefault(v=>v.Id==i.TargetId);
        if(venue is null||!market.Settings.BasePrices.ContainsKey(i.ResourceId)||i.ResourceId is "spirit-stone" or "contribution" or "pill"||i.Quantity<=0||i.LimitPrice is not >0)return "Invalid market, item, quantity or limit price";
        var error=Eligibility(w,i,venue);if(error is not null)return error;
        try{
            long amount=i.Kind==ActionKind.PlaceMarketBuy?checked(i.Quantity*i.LimitPrice.Value):i.Quantity;
            if(Inventory.Balance(w,i.OrganizationId??i.ActorId,i.Kind==ActionKind.PlaceMarketBuy?"spirit-stone":i.ResourceId)<amount)return "Insufficient unreserved order assets";
            _=checked(Now(w)+Duration(w)+checked(market.Settings.Rules.DefaultExpiryDays*w.Time.Rules.TicksPerDay*DiscreteTime.SecondsPerStep(w)));
        }catch(OverflowException){return "Order value or expiry overflow";}
        return null;
    }
    internal static void Start(WorldState w,ActionIntent i)
    {
        var market=w.Time!.MarketOrders!;long now=Now(w),due=checked(now+Duration(w));
        if(i.Kind!=ActionKind.CancelMarketOrder)
        {
            var side=i.Kind==ActionKind.PlaceMarketBuy?MarketOrderSide.Buy:MarketOrderSide.Sell;
            long reserved=side==MarketOrderSide.Buy?checked(i.Quantity*i.LimitPrice!.Value):i.Quantity;
            var order=new MarketOrder{Id=i.Id,ActorId=i.ActorId,OwnerId=i.OrganizationId??i.ActorId,VenueId=i.TargetId,ResourceId=i.ResourceId,Side=side,
                LimitPrice=i.LimitPrice!.Value,Quantity=i.Quantity,Remaining=i.Quantity,Sequence=market.NextSequence++,CreatedAt=now,ActiveAt=due,
                ExpiresAt=checked(due+checked(market.Settings.Rules.DefaultExpiryDays*w.Time.Rules.TicksPerDay*DiscreteTime.SecondsPerStep(w))),Reserved=reserved};
            market.Orders.Add(order.Id,order);
            Move(w,order.Id,order.OwnerId,Account(order.Id),side==MarketOrderSide.Buy?"spirit-stone":i.ResourceId,reserved,"OrderReserved");
        }
        market.Commands.Add(new(i,due));ActionExecutor.Emit(w,i.Id,"MarketCommandStarted",i.Kind+":"+due);
    }
    internal static void Complete(WorldState w,string id)
    {
        var market=w.Time!.MarketOrders!;var command=market.Commands.SingleOrDefault(c=>c.Intent.Id==id);if(command is null)return;
        ActionLifecycle.RequireRunning(w,id);var i=command.Intent;
        var order=market.Orders[i.Kind==ActionKind.CancelMarketOrder?i.TargetId:i.Id];
        var venue=market.Settings.Rules.Venues.Single(v=>v.Id==order.VenueId);
        bool valid=Eligibility(w,i,venue) is null;
        market.Commands.Remove(command);
        if(i.Kind==ActionKind.CancelMarketOrder)
        {
            if(valid&&order.Phase==MarketOrderPhase.Open){Close(w,order,MarketOrderPhase.Cancelled);ActionLifecycle.Checkpoint(w,id);ActionLifecycle.Finish(w,id,ActionPhase.Completed,"Unfilled order remainder cancelled");}
            else ActionLifecycle.Finish(w,id,ActionPhase.Failed,"Cancellation no longer available");
        }
        else if(!valid){Close(w,order,MarketOrderPhase.Failed);ActionLifecycle.Finish(w,id,ActionPhase.Failed,"Posting eligibility changed; order escrow refunded");}
        else
        {
            order.Phase=MarketOrderPhase.Open;Match(w,order);ActionLifecycle.Checkpoint(w,id);ActionLifecycle.Finish(w,id,ActionPhase.Completed,"Posting complete; remaining assets stay in order escrow");Notify(w,venue);
        }
    }
    private static void Match(WorldState w,MarketOrder incoming)
    {
        var market=w.Time!.MarketOrders!;
        var eligible=market.Orders.Values.Where(o=>o.Id!=incoming.Id&&o.Phase==MarketOrderPhase.Open&&o.VenueId==incoming.VenueId&&o.ResourceId==incoming.ResourceId&&o.Side!=incoming.Side&&o.OwnerId!=incoming.OwnerId&&o.ExpiresAt>Now(w)&&
            (incoming.Side==MarketOrderSide.Buy?o.LimitPrice<=incoming.LimitPrice:o.LimitPrice>=incoming.LimitPrice));
        var sorted=incoming.Side==MarketOrderSide.Buy?eligible.OrderBy(o=>o.LimitPrice).ThenBy(o=>o.Sequence):eligible.OrderByDescending(o=>o.LimitPrice).ThenBy(o=>o.Sequence);
        foreach(var resting in sorted.ToArray())
        {
            if(incoming.Remaining==0)break;
            var buy=incoming.Side==MarketOrderSide.Buy?incoming:resting;var sell=incoming.Side==MarketOrderSide.Sell?incoming:resting;
            long quantity=Math.Min(buy.Remaining,sell.Remaining),price=resting.LimitPrice,total=checked(quantity*price),difference=checked(quantity*(buy.LimitPrice-price));
            string fillId="$market-fill:"+buy.Id+":"+sell.Id; // One activation can match a pair only once.
            Move(w,fillId,Account(buy.Id),sell.OwnerId,"spirit-stone",total,"OrderPayment");
            Move(w,fillId,Account(sell.Id),buy.OwnerId,sell.ResourceId,quantity,"OrderGoods");
            if(difference>0)Move(w,fillId,Account(buy.Id),buy.OwnerId,"spirit-stone",difference,"OrderPriceImprovement");
            buy.Consumed=checked(buy.Consumed+total);buy.Refunded=checked(buy.Refunded+difference);sell.Consumed=checked(sell.Consumed+quantity);
            buy.Remaining-=quantity;sell.Remaining-=quantity;
            market.Fills.Add(new(fillId,buy.Id,sell.Id,incoming.VenueId,incoming.ResourceId,quantity,price,Now(w)));
            if(buy.Remaining==0)Close(w,buy,MarketOrderPhase.Filled);if(sell.Remaining==0)Close(w,sell,MarketOrderPhase.Filled);
            ActionExecutor.Emit(w,fillId,"MarketFilled",$"{incoming.ResourceId}:{quantity}@{price}");
            EventDecisions.Request(w,buy.ActorId,DecisionTrigger.StateChanged);EventDecisions.Request(w,sell.ActorId,DecisionTrigger.StateChanged);
        }
    }
    internal static void Expire(WorldState w,string id)
    {
        if(w.Time!.MarketOrders!.Orders.TryGetValue(id,out var order)&&order.Phase==MarketOrderPhase.Open&&order.ExpiresAt<=Now(w))Close(w,order,MarketOrderPhase.Expired);
    }
    private static void Close(WorldState w,MarketOrder order,MarketOrderPhase phase)
    {
        long refund=order.EscrowRemaining;
        if(refund>0){Move(w,order.Id,Account(order.Id),order.OwnerId,order.Side==MarketOrderSide.Buy?"spirit-stone":order.ResourceId,refund,"OrderRemainderRefund");order.Refunded=checked(order.Refunded+refund);}
        order.Phase=phase;new EventScheduler(w.Time!.DiscreteEvents!).Cancel(ExpiryKey(order.Id));
        ActionExecutor.Emit(w,order.Id,"MarketOrderClosed",phase.ToString());EventDecisions.Request(w,order.ActorId,DecisionTrigger.StateChanged);
        Notify(w,w.Time.MarketOrders!.Settings.Rules.Venues.Single(v=>v.Id==order.VenueId));
    }
    internal static void Move(WorldState w,string id,string from,string to,string resource,long amount,string reason)=>ActionExecutor.Transfer(w,new(id,from,to,resource,amount,0,DecisionSource.Npc),from,to,reason);
    private static void Notify(WorldState w,MarketVenue venue)
    {
        foreach(var actor in w.Characters.Where(a=>a.RegionId==venue.RegionId&&a.DemoLocationId==venue.LocationId&&a.MonsterKind==MonsterKind.None))EventDecisions.Request(w,actor.Id,DecisionTrigger.KnownNotification);
    }
    public static decimal Reference(WorldState w,string venue,string resource)
    {
        var m=w.Time!.MarketOrders!;var fills=m.Fills.Where(f=>f.VenueId==venue&&f.ResourceId==resource).TakeLast(m.Settings.Rules.ReferenceTradeWindow).ToArray();
        return fills.Length==0?m.Settings.BasePrices[resource]:fills.Sum(f=>(decimal)f.Quantity*f.UnitPrice)/fills.Sum(f=>(decimal)f.Quantity);
    }
    public static KnownOrderBook? Observe(WorldState w,string actorId,string venueId,string resource)
    {
        var m=w.Time?.MarketOrders??throw new InvalidOperationException("R5 not enabled");var actor=w.Characters.Single(a=>a.Id==actorId);var venue=m.Settings.Rules.Venues.SingleOrDefault(v=>v.Id==venueId);
        if(venue is null||!m.Settings.BasePrices.ContainsKey(resource))return null;
        string key=venueId+":"+resource;
        if(actor.RegionId==venue.RegionId&&actor.DemoLocationId==venue.LocationId)
        {
            var orders=m.Orders.Values.Where(o=>o.VenueId==venueId&&o.ResourceId==resource&&o.Phase==MarketOrderPhase.Open&&o.ExpiresAt>Now(w)).ToArray();
            PublicOrderQuote[] Quotes(MarketOrderSide side)=>orders.Where(o=>o.Side==side).GroupBy(o=>o.LimitPrice).OrderBy(g=>side==MarketOrderSide.Buy?-g.Key:g.Key).Select(g=>new PublicOrderQuote(g.Key,g.Sum(o=>o.Remaining))).ToArray();
            var book=new KnownOrderBook(actorId,venueId,venue.RegionId,resource,Now(w),checked(Now(w)+w.Rules.MemoryTicks*DiscreteTime.SecondsPerStep(w)),"observed:orderbook:"+venueId,Reference(w,venueId,resource),Quotes(MarketOrderSide.Buy),Quotes(MarketOrderSide.Sell));
            if(!m.Knowledge.TryGetValue(actorId,out var known))m.Knowledge.Add(actorId,known=new(StringComparer.Ordinal));known[key]=book;return book;
        }
        return m.Knowledge.TryGetValue(actorId,out var facts)&&facts.TryGetValue(key,out var old)&&old.RegionId==actor.RegionId&&old.ExpiresAt>Now(w)?old:null;
    }
}
