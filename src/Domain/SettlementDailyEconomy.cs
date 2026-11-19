namespace Cultivation.Domain;

public sealed record SettlementResidentConsumption(string ResourceId, long UnitsPerResident, long MoneyPerUnit)
{
    public long? MaximumUnitsPerResident { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public long MaximumUnits => MaximumUnitsPerResident ?? UnitsPerResident;
}
public sealed record SettlementPopulationGrowthRules(long InitialValue, long MinimumValue, long Threshold,
    long PointsPerGroup, int PopulationPerThreshold);
public sealed record SettlementDailyEconomyRules(List<SettlementResidentConsumption> Consumption)
{
    public SettlementPopulationGrowthRules? PopulationGrowth { get; init; }
}
public sealed record SettlementConsumptionReceipt(string Id, string SettlementId, long Tick, int Population,
    int SettlementLevel, string ResourceId, long StockBefore, long ReservedQuantity, long Quantity, long Income);
public sealed record SettlementPopulationReceipt(string Id, string SettlementId, long Tick, int PopulationBefore,
    int Capacity, long ProgressBefore, long ConsumedGroups, long MinimumGroups, long Delta,
    int AddedPopulation, int PopulationAfter, long ProgressAfter);
