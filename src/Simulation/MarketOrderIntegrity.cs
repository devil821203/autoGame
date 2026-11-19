using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class MarketOrderIntegrity
{
    public static void Validate(WorldState w)
    {
        if(w.Time?.MarketOrders is not {} m)
        {
            if(w.Time?.DiscreteEvents?.Pending.Any(e=>e.Kind is SimulationEventKind.MarketCommand or SimulationEventKind.MarketOrderExpiry)==true)throw new InvalidDataException("Market event without order snapshot");return;
        }
        try{ValidateCore(w,m);}catch(OverflowException e){throw new InvalidDataException("R5 order arithmetic overflow",e);}
    }
    private static void ValidateCore(WorldState w,MarketOrderState m)
    {
        var r=m.Settings?.Rules;var events=w.Time!.DiscreteEvents;
        if(w.Time.Content is null||events is null||m.FormatVersion!=1||r is null||r.Priority!="PriceTime"||r.ExecutionPrice!="RestingOrder"||r.AllowSelfTrade||r.TransactionFeeRate!=0||
            r.ReferenceTradeWindow<=0||r.DefaultExpiryDays<=0||r.OperationMinutes<=0||r.OperationActionPoints<=0||r.OperationActionPoints>w.Time.Rules.DailyActionPoints||
            r.OperationMinutes%w.Time.ActionDriven!.MinutesPerActionPoint!=0||r.OperationActionPoints!=r.OperationMinutes/w.Time.ActionDriven.MinutesPerActionPoint||
            r.Venues is not {Length:>0}||r.Venues.Any(v=>v is null||string.IsNullOrWhiteSpace(v.Id)||string.IsNullOrWhiteSpace(v.RegionId)||string.IsNullOrWhiteSpace(v.LocationId)||!w.Organizations.Any(o=>o.Id==v.Id&&o.RegionId==v.RegionId))||r.Venues.Select(v=>v.Id).Distinct().Count()!=r.Venues.Length||
            m.Settings!.BasePrices is not {Count:>0}||m.Settings.BasePrices.Any(p=>p.Value<=0||p.Key is "spirit-stone" or "contribution" or "pill"||!w.Items.Any(i=>i.Id==p.Key))||m.Orders is null||m.Commands is null||m.Fills is null||m.Knowledge is null||m.NextSequence<1)
            throw new InvalidDataException("config/economy/order-book.json: invalid R5 rules or state");
        long now=events.WorldTime;var actors=w.Characters.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);var owners=actors.Concat(w.Organizations.Select(o=>o.Id)).ToHashSet(StringComparer.Ordinal);
        if(m.Orders.Values.Any(o=>o is null)||m.Commands.Any(c=>c is null)||m.Fills.Any(f=>f is null)||m.Orders.Values.Select(o=>o.Sequence).Distinct().Count()!=m.Orders.Count||m.Fills.Select(f=>f.Id).Distinct().Count()!=m.Fills.Count||m.Commands.Select(c=>c.Intent.ActorId).Distinct().Count()!=m.Commands.Count)throw new InvalidDataException("Duplicate/null R5 records");
        foreach(var (id,o) in m.Orders)
        {
            bool live=o.Phase is MarketOrderPhase.Open or MarketOrderPhase.Submitting;
            var fills=m.Fills.Where(f=>f.BuyOrderId==id||f.SellOrderId==id).ToArray();
            if(id!=o.Id||!actors.Contains(o.ActorId)||!owners.Contains(o.OwnerId)||!r.Venues.Any(v=>v.Id==o.VenueId)||!m.Settings.BasePrices.ContainsKey(o.ResourceId)||!Enum.IsDefined(o.Side)||!Enum.IsDefined(o.Phase)||
                o.Sequence<1||o.Sequence>=m.NextSequence||o.LimitPrice<=0||o.Quantity<=0||o.Remaining<0||o.Remaining>o.Quantity||o.CreatedAt<0||o.CreatedAt>now||o.ActiveAt!=checked(o.CreatedAt+MarketOrders.Duration(w))||o.ExpiresAt<=o.ActiveAt||o.Phase==MarketOrderPhase.Open&&o.ExpiresAt<now||
                o.Reserved!=(o.Side==MarketOrderSide.Buy?checked(o.Quantity*o.LimitPrice):o.Quantity)||o.Consumed<0||o.Refunded<0||o.EscrowRemaining<0||
                fills.Sum(f=>f.Quantity)!=o.Quantity-o.Remaining||o.Phase==MarketOrderPhase.Filled&&o.Remaining!=0||o.EscrowRemaining!=(live?(o.Side==MarketOrderSide.Buy?checked(o.Remaining*o.LimitPrice):o.Remaining):0))
                throw new InvalidDataException("Invalid order/escrow accounting: "+id);
            string account=MarketOrders.Account(id),resource=o.Side==MarketOrderSide.Buy?"spirit-stone":o.ResourceId;
            var entries=w.Ledger.Where(e=>e.From==account||e.To==account).ToArray();
            if(entries.Any(e=>e.ResourceId!=resource)||entries.Count(e=>e.To==account&&e.From==o.OwnerId&&e.Quantity==o.Reserved&&e.ActionId==id&&e.Reason=="OrderReserved")!=1||
                entries.Where(e=>e.To==account).Sum(e=>e.Quantity)!=o.Reserved||Inventory.Balance(w,account,resource)!=o.EscrowRemaining||
                entries.Where(e=>e.From==account&&e.Reason is "OrderPriceImprovement" or "OrderRemainderRefund").Any(e=>e.To!=o.OwnerId)||
                entries.Where(e=>e.From==account&&e.Reason is "OrderPriceImprovement" or "OrderRemainderRefund").Sum(e=>e.Quantity)!=o.Refunded||
                entries.Where(e=>e.From==account&&e.Reason is "OrderGoods" or "OrderPayment").Sum(e=>e.Quantity)!=o.Consumed||
                entries.Any(e=>e.Reason is not ("OrderReserved" or "OrderGoods" or "OrderPayment" or "OrderPriceImprovement" or "OrderRemainderRefund")))throw new InvalidDataException("Order escrow disagrees with authoritative ledger: "+id);
            if(!w.Time.ActionRuntime!.Actions.TryGetValue(id,out var action)||action.ActorId!=o.ActorId||action.ResourceId!=o.ResourceId||action.Kind!=(o.Side==MarketOrderSide.Buy?ActionKind.PlaceMarketBuy:ActionKind.PlaceMarketSell)||
                (o.Phase==MarketOrderPhase.Submitting)!=(action.Phase==ActionPhase.Running)||o.Phase==MarketOrderPhase.Failed&&action.Phase!=ActionPhase.Failed)throw new InvalidDataException("Order missing posting action");
        }
        foreach(var f in m.Fills)
        {
            if(!m.Orders.TryGetValue(f.BuyOrderId,out var buy)||!m.Orders.TryGetValue(f.SellOrderId,out var sell)||buy.Side!=MarketOrderSide.Buy||sell.Side!=MarketOrderSide.Sell||buy.OwnerId==sell.OwnerId||buy.VenueId!=sell.VenueId||f.VenueId!=buy.VenueId||buy.ResourceId!=sell.ResourceId||f.ResourceId!=buy.ResourceId||f.Quantity<=0||f.UnitPrice<=0||f.UnitPrice>buy.LimitPrice||f.UnitPrice<sell.LimitPrice||f.UnitPrice!=(buy.Sequence<sell.Sequence?buy.LimitPrice:sell.LimitPrice)||
                f.AtSeconds<buy.ActiveAt||f.AtSeconds<sell.ActiveAt||f.AtSeconds>=buy.ExpiresAt||f.AtSeconds>=sell.ExpiresAt||f.AtSeconds>now)throw new InvalidDataException("Invalid R5 fill");
            var entries=w.Ledger.Where(e=>e.ActionId==f.Id).ToArray();long improvement=checked(f.Quantity*(buy.LimitPrice-f.UnitPrice));
            bool Entry(string from,string to,string item,long amount,string reason)=>entries.Count(e=>e.From==from&&e.To==to&&e.ResourceId==item&&e.Quantity==amount&&e.Reason==reason)==1;
            if(entries.Length!=(improvement>0?3:2)||!Entry(MarketOrders.Account(buy.Id),sell.OwnerId,"spirit-stone",checked(f.Quantity*f.UnitPrice),"OrderPayment")||!Entry(MarketOrders.Account(sell.Id),buy.OwnerId,f.ResourceId,f.Quantity,"OrderGoods")||improvement>0&&!Entry(MarketOrders.Account(buy.Id),buy.OwnerId,"spirit-stone",improvement,"OrderPriceImprovement"))throw new InvalidDataException("Fill ledger legs missing or duplicated");
        }
        foreach(var c in m.Commands)
            if(!MarketOrders.Handles(c.Intent.Kind)||!actors.Contains(c.Intent.ActorId)||c.DueAt<now||!w.Time.ActionRuntime!.Actions.TryGetValue(c.Intent.Id,out var action)||action.Phase!=ActionPhase.Running||action.DueAt!=c.DueAt||
                !m.Orders.ContainsKey(c.Intent.Kind==ActionKind.CancelMarketOrder?c.Intent.TargetId:c.Intent.Id)||events.Pending.Count(e=>e.Kind==SimulationEventKind.MarketCommand&&e.Detail==c.Intent.Id&&e.CorrelationId==MarketOrders.CommandKey(c.Intent.Id)&&e.DueAt==c.DueAt&&e.Version==events.Versions[e.CorrelationId])!=1)
                throw new InvalidDataException("Market command/event/action mismatch");
        foreach(var order in m.Orders.Values.Where(o=>o.Phase==MarketOrderPhase.Open))
            if(events.Pending.Count(e=>e.Kind==SimulationEventKind.MarketOrderExpiry&&e.Detail==order.Id&&e.CorrelationId==MarketOrders.ExpiryKey(order.Id)&&e.DueAt==order.ExpiresAt&&e.Version==events.Versions[e.CorrelationId])!=1)throw new InvalidDataException("Order expiry event missing/duplicated");
        foreach(var e in events.Pending.Where(e=>e.Version==events.Versions[e.CorrelationId]))
            if(e.Kind==SimulationEventKind.MarketCommand&&!m.Commands.Any(c=>e.Detail==c.Intent.Id&&e.ActorId==c.Intent.ActorId&&e.CorrelationId==MarketOrders.CommandKey(c.Intent.Id))||
                e.Kind==SimulationEventKind.MarketOrderExpiry&&!m.Orders.Values.Any(o=>o.Id==e.Detail&&o.ActorId==e.ActorId&&o.Phase==MarketOrderPhase.Open&&e.CorrelationId==MarketOrders.ExpiryKey(o.Id)))throw new InvalidDataException("Orphan market event");
        foreach(var (actor,facts) in m.Knowledge)
        {
            if(!actors.Contains(actor)||facts is null)throw new InvalidDataException("Invalid book observer");
            foreach(var (key,f) in facts)
                if(f is null||key!=f.VenueId+":"+f.ResourceId||f.ActorId!=actor||!r.Venues.Any(v=>v.Id==f.VenueId&&v.RegionId==f.RegionId)||!m.Settings.BasePrices.ContainsKey(f.ResourceId)||f.Source!="observed:orderbook:"+f.VenueId||f.ObservedAt<0||f.ObservedAt>now||f.ExpiresAt<=f.ObservedAt||f.ReferencePrice<=0||f.Bids is null||f.Asks is null||f.Bids.Concat(f.Asks).Any(q=>q is null||q.Price<=0||q.Quantity<=0))throw new InvalidDataException("Invalid book knowledge provenance");
        }
    }
}
