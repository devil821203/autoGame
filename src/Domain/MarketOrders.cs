namespace Cultivation.Domain;

public enum MarketOrderSide { Buy, Sell }
public enum MarketOrderPhase { Submitting, Open, Filled, Cancelled, Expired, Failed }
public sealed record MarketVenue(string Id,string RegionId,string LocationId);
public sealed record OrderBookRules(string Priority,string ExecutionPrice,bool AllowSelfTrade,int ReferenceTradeWindow,
    decimal TransactionFeeRate,int OperationMinutes,int OperationActionPoints,int DefaultExpiryDays,MarketVenue[] Venues);
public sealed record OrderBookSettings(OrderBookRules Rules,Dictionary<string,long> BasePrices);
public sealed record MarketCommand(ActionIntent Intent,long DueAt);
public sealed class MarketOrder
{
    public string Id { get; set; }="";
    public string ActorId { get; set; }="";
    public string OwnerId { get; set; }="";
    public string VenueId { get; set; }="";
    public string ResourceId { get; set; }="";
    public MarketOrderSide Side { get; set; }
    public MarketOrderPhase Phase { get; set; }
    public long LimitPrice { get; set; }
    public long Quantity { get; set; }
    public long Remaining { get; set; }
    public long Sequence { get; set; }
    public long CreatedAt { get; set; }
    public long ActiveAt { get; set; }
    public long ExpiresAt { get; set; }
    public long Reserved { get; set; }
    public long Consumed { get; set; }
    public long Refunded { get; set; }
    public long EscrowRemaining=>Reserved-Consumed-Refunded;
}
public sealed record MarketFill(string Id,string BuyOrderId,string SellOrderId,string VenueId,string ResourceId,long Quantity,long UnitPrice,long AtSeconds);
public sealed record PublicOrderQuote(long Price,long Quantity);
public sealed record KnownOrderBook(string ActorId,string VenueId,string RegionId,string ResourceId,long ObservedAt,long ExpiresAt,
    string Source,decimal ReferencePrice,PublicOrderQuote[] Bids,PublicOrderQuote[] Asks);
public sealed class MarketOrderState
{
    public int FormatVersion { get; set; }=1;
    public OrderBookSettings Settings { get; set; }=null!;
    public long NextSequence { get; set; }=1;
    public Dictionary<string,MarketOrder> Orders { get; set; }=new(StringComparer.Ordinal);
    public List<MarketCommand> Commands { get; set; }=[];
    public List<MarketFill> Fills { get; set; }=[];
    public Dictionary<string,Dictionary<string,KnownOrderBook>> Knowledge { get; set; }=new(StringComparer.Ordinal);
}
