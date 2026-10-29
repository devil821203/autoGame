using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class ExternalMarketActions
{
    internal const string Source="$external-market";
    private sealed record Options(string SettlementId,decimal PurchaseMultiplier,decimal SaleMultiplier,int ActionPoints,bool OrganizationAutoTrade);
    internal static void EnableFresh(WorldState w,GameConfiguration config)
    {
        var o=config.Read<Options>("economy/external-market.json");
        var prices=new Dictionary<string,long>(w.PaidServiceRules!.MaterialPrices,StringComparer.Ordinal);
        foreach(var price in w.SettlementRules!.Prices)prices.Add(price.Item,price.Buy);
        foreach(var recipe in w.LifeSystem!.Recipes)
            if(!prices.ContainsKey(recipe.Output))prices.Add(recipe.Output,checked(recipe.Inputs.Sum(p=>checked(p.Value*prices[p.Key]))+w.PaidServiceRules.LaborFees[recipe.Id]));
        w.ExternalMarket=new(){Rules=new(o.SettlementId,o.PurchaseMultiplier,o.SaleMultiplier,prices,o.ActionPoints,o.OrganizationAutoTrade)};
    }
    public static bool Handles(ActionKind k)=>k is ActionKind.ExternalMarketBuy or ActionKind.ExternalMarketSell;
    public static string Owner(ActionIntent i)=>i.OrganizationId??i.ActorId;
    public static string? Validate(WorldState w,ActionIntent i)
    {
        if(w.ExternalMarket is not { } market||i.TargetId!=market.Rules.SettlementId||i.Quantity<=0||i.DurationTicks!=0||i.MissionId is not null)return "External market unavailable or invalid order";
        var actor=w.Characters.Single(c=>c.Id==i.ActorId);
        var settlement=w.Settlements.Single(s=>s.Id==market.Rules.SettlementId);
        var anchor=w.HexMap!.Sites.Single(s=>s.Id==settlement.SiteId).Anchor;
        if(actor.RegionId!="valley"||HexNavigation.Position(w.HexMap,actor)!=anchor)return "Must arrive at external market settlement";
        if(i.OrganizationId is not null&&(!w.Organizations.Any(o=>o.Id==i.OrganizationId&&o.LeaderId==i.ActorId)||i.OrganizationId=="consumed"))return "Organization trading permission required";
        string definition=ExternalMarketPricing.Definition(w,i.ResourceId);
        if(!market.Rules.BasePrices.ContainsKey(definition))return "Unsupported external market item";
        bool buy=i.Kind==ActionKind.ExternalMarketBuy;
        var recipe=w.LifeSystem?.Recipes.SingleOrDefault(r=>r.Output==definition&&r.Slot is not null);
        if(buy&&(i.ResourceId!=definition||recipe is not null&&i.Quantity!=1))return "Equipment purchases require one catalog item";
        if(!buy&&w.Characters.Any(c=>c.CraftedWeaponId==i.ResourceId||c.EquippedArmorId==i.ResourceId||c.EquippedWeaponId==i.ResourceId))return "Unequip item before sale";
        long total;
        try{total=ExternalMarketPricing.Total(w,i.ResourceId,i.Quantity,buy);}catch(OverflowException){return "External market price overflow";}
        if(total<1)return "Batch price below one money";
        if(i.LimitPrice is { } limit&&(buy?total>limit:total<limit))return "External market total beyond limit";
        string owner=Owner(i);
        if(Inventory.Balance(w,owner,buy?"spirit-stone":i.ResourceId)<(buy?total:i.Quantity))return "Insufficient trader funds or stock";
        if(!InventoryCapacity.CanReceive(w,owner,buy?i.ResourceId:"spirit-stone",buy?i.Quantity:total)||
            !InventoryCapacity.CanReceive(w,"consumed",buy?"spirit-stone":i.ResourceId,buy?total:i.Quantity))return "External market inventory overflow";
        return null;
    }
    internal static void Execute(WorldState w,ActionIntent i)
    {
        bool buy=i.Kind==ActionKind.ExternalMarketBuy;string owner=Owner(i),delivered=i.ResourceId;
        long total=ExternalMarketPricing.Total(w,i.ResourceId,i.Quantity,buy);
        var recipe=w.LifeSystem?.Recipes.SingleOrDefault(r=>r.Output==i.ResourceId&&r.Slot is not null);
        if(buy&&recipe is not null)
        {
            delivered="gear:"+i.Id;
            w.LifeSystem!.Equipment.Add(new(delivered,recipe.Id,EquipmentQuality.Ordinary,recipe.Slot!,recipe.Attack,recipe.Defense,i.Id));
            w.Items.Add(new(delivered,"普通"+w.Items.Single(t=>t.Id==recipe.Output).Name,recipe.Slot=="weapon"?"武器":"防具"));
        }
        w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,owner,"consumed",buy?"spirit-stone":i.ResourceId,buy?total:i.Quantity,"ExternalMarketSpent"));
        w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,Source,owner,buy?delivered:"spirit-stone",buy?i.Quantity:total,"ExternalMarketSupplied"));
        w.ExternalMarket!.Receipts.Add(new(i.Id,w.Clock.Tick,i.ActorId,owner,i.Kind,i.ResourceId,delivered,i.Quantity,total));
        ActionExecutor.Emit(w,i.Id,"ExternalMarketTrade",$"{owner}:{i.Kind}:{i.ResourceId}:{i.Quantity}:total={total}");
        w.Memories.Add(new(i.ActorId,w.Clock.Tick,i.Kind,true,i.ResourceId,buy?-total:total,buy?i.Quantity:-i.Quantity,"external-market:"+i.TargetId));
    }
    internal static void ValidateWorld(WorldState w)
    {
        if(w.ExternalMarket is not { } m)return;
        var r=m.Rules;
        if(r is null||w.SettlementOrganizationDemo is null||w.HexMap is null||!w.Settlements.Any(s=>s.Id==r.SettlementId)||
            r.PurchaseMultiplier<=0||r.PurchaseMultiplier>=1||r.SaleMultiplier!=2||r.ActionPoints is <1 or >30||r.BasePrices is null||r.BasePrices.Count==0||
            r.BasePrices.Any(p=>p.Value<=0||p.Value>long.MaxValue/2||p.Key is "spirit-stone" or "contribution" or "pill"||!w.Items.Any(t=>t.Id==p.Key)))
            throw new InvalidDataException("config/economy/external-market.json: invalid settlement, price multipliers, BasePrices or ActionPoints");
        if(m.Receipts.Select(t=>t.Id).Distinct().Count()!=m.Receipts.Count)throw new InvalidDataException("Duplicate external market receipt");
        var entries=w.Ledger.ToLookup(e=>e.ActionId);
        foreach(var t in m.Receipts)
        {
            bool buy=t.Kind==ActionKind.ExternalMarketBuy;
            if(!Handles(t.Kind)||t.Tick<=0||t.Tick>w.Clock.Tick||t.Quantity<=0||t.TotalPrice<1||!w.ProcessedIntentIds.Contains(t.Id)||
                !w.Characters.Any(c=>c.Id==t.ActorId)||!(w.Characters.Any(c=>c.Id==t.OwnerId)||w.Organizations.Any(o=>o.Id==t.OwnerId))||
                !r.BasePrices.ContainsKey(ExternalMarketPricing.Definition(w,t.ItemId))||t.TotalPrice!=ExternalMarketPricing.Total(w,t.ItemId,t.Quantity,buy)||
                t.DeliveredItemId!=(buy&&w.LifeSystem?.Recipes.Any(p=>p.Output==t.ItemId&&p.Slot is not null)==true?"gear:"+t.Id:t.ItemId)||
                entries[t.Id].Count()!=2||entries[t.Id].Count(e=>e.Tick==t.Tick&&e.From==t.OwnerId&&e.To=="consumed"&&e.ResourceId==(buy?"spirit-stone":t.ItemId)&&e.Quantity==(buy?t.TotalPrice:t.Quantity)&&e.Reason=="ExternalMarketSpent")!=1||
                entries[t.Id].Count(e=>e.Tick==t.Tick&&e.From==Source&&e.To==t.OwnerId&&e.ResourceId==(buy?t.DeliveredItemId:"spirit-stone")&&e.Quantity==(buy?t.Quantity:t.TotalPrice)&&e.Reason=="ExternalMarketSupplied")!=1)
                throw new InvalidDataException("Invalid external market receipt or issuance");
        }
        if(w.Ledger.Any(e=>e.From==Source&&!m.Receipts.Any(t=>t.Id==e.ActionId)))throw new InvalidDataException("Unbacked external market issuance");
    }
}
