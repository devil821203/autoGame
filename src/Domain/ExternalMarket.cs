namespace Cultivation.Domain;

public sealed record ExternalMarketRules(string SettlementId, decimal PurchaseMultiplier,
    decimal SaleMultiplier, Dictionary<string,long> BasePrices, int ActionPoints, bool OrganizationAutoTrade);
public sealed record ExternalMarketReceipt(string Id,long Tick,string ActorId,string OwnerId,ActionKind Kind,
    string ItemId,string DeliveredItemId,long Quantity,long TotalPrice);
public sealed class ExternalMarketState
{
    public ExternalMarketRules Rules { get; set; } = null!;
    public List<ExternalMarketReceipt> Receipts { get; set; } = [];
}

public static class ExternalMarketPricing
{
    public static string Definition(WorldState w,string item)=>w.LifeSystem?.Equipment.SingleOrDefault(g=>g.Id==item)?.DefinitionId??w.EquipmentDurability?.LegacyWeapons.SingleOrDefault(g=>g.Id==item)?.DefinitionId??item;
    public static long Total(WorldState w,string item,long quantity,bool buying)
    {
        var r=w.ExternalMarket!.Rules;
        decimal price=checked(r.BasePrices[Definition(w,item)]*(decimal)quantity*(buying?r.SaleMultiplier:r.PurchaseMultiplier));
        return checked((long)decimal.Floor(price));
    }
}
