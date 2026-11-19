namespace Cultivation.Domain;

public sealed record MarketListing(string ResourceId, long BasePrice, long TargetStock,
    long MinimumPrice, long MaximumPrice, long StockPriceStep = 1, long Spread = 2,
    string MarketId = "market", string RegionId = "valley");
public sealed record MarketQuote(string MarketId, string ResourceId, long BuyPrice, long SellPrice, long Stock, long Tick, string Source);
public sealed record TradeReceipt(string ActionId, long Tick, string ActorId, string MarketId,
    ActionKind Kind, string ResourceId, long Quantity, long UnitPrice);
public enum MissionStatus { Active, Completed, Cancelled, Expired }
public sealed record MissionOffer(string Id, long Quantity, long UnitPrice, long DeadlineTick, long MaximumBudget);
public sealed record MissionState(string Id, string OrganizationId, string RegionId, string ResourceId,
    long Required, long UnitPrice, long DeadlineTick, long ReservedBudget, long Delivered,
    MissionStatus Status, List<string> Participants, bool MembersOnly = false);
public sealed record DeliveryReceipt(string ActionId, string MissionId, long Tick, string ActorId, long Quantity, long Payment);

public static class MarketPricing
{
    public static MarketQuote Quote(WorldState world, string item, string market = "market")
    {
        long stock = Inventory.Balance(world, market, item);
        var listing = world.MarketListings.SingleOrDefault(l => l.MarketId == market && l.ResourceId == item);
        long fallback = item == "herb" ? world.Rules.HerbPrice : world.Rules.PillPrice;
        if (listing is null) return new(market, item, fallback, fallback, stock, world.Clock.Tick, $"{market}:valley");
        decimal difference = (decimal)listing.TargetStock - stock;
        decimal raw = listing.BasePrice;
        if (listing.StockPriceStep > 0)
        {
            if (difference >= 0)
                raw = difference >= ((decimal)listing.MaximumPrice - listing.BasePrice) / listing.StockPriceStep ? listing.MaximumPrice : listing.BasePrice + difference * listing.StockPriceStep;
            else
                raw = -difference >= ((decimal)listing.BasePrice - listing.MinimumPrice) / listing.StockPriceStep ? listing.MinimumPrice : listing.BasePrice + difference * listing.StockPriceStep;
        }
        long buy = (long)Math.Clamp(raw, listing.MinimumPrice, listing.MaximumPrice);
        long sell = Math.Max(listing.MinimumPrice, buy - listing.Spread);
        return new(market, item, buy, sell, stock, world.Clock.Tick, $"{market}:{listing.RegionId}");
    }
}
