namespace Cultivation.Domain;

public sealed record ProcurementRules(string BuyerId = "test-buyer", string SellerId = "sect", long IntervalTicks = 20,
    long DemandPerItem = 10, int PriceChangePercent = 10, long PillInitialPrice = 150, long HideInitialPrice = 30,
    long MinimumPrice = 1, long MaximumPrice = 1000000, string PrimaryResourceId = "pill", long? PrimaryInitialPrice = null);
public sealed record ProcurementQuote(string ResourceId, long UnitPrice, long ReceivedThisPeriod = 0);
public sealed record ProcurementReceipt(string Id, long Tick, string ResourceId, long Quantity, long UnitPrice, long Payment);
