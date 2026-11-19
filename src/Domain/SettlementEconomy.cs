namespace Cultivation.Domain;

public sealed record SettlementLevelRule(int Level, int RequiredPopulation, long Stones, long Wood);
public sealed record SettlementShopPrice(string Item, long Buy, long Sell);
public sealed record SettlementRuntimeRules(int InitialLevel, int InitialPopulation, decimal InitialHappiness,
    decimal MinimumHappiness, decimal MaximumHappiness, long InitialStones, int HallCapacity,
    int HousingCapacity, int HallCapacityPerLevel, int EmployeeLimit, long DailyWage,
    long DailyOutputPerEmployee, long MaterialInfluence, long BuildingInfluence,
    Dictionary<SettlementBuildingKind, string> Outputs, List<SettlementLevelRule> Levels,
    List<SettlementShopPrice> Prices)
{
    public Dictionary<SettlementBuildingKind,long>? DailyOutputOverrides { get; init; }
    public long DailyOutput(SettlementBuildingKind kind) => DailyOutputOverrides?.GetValueOrDefault(kind,DailyOutputPerEmployee) ?? DailyOutputPerEmployee;
}
public sealed record SettlementDemoLayout(string SiteId, HexCoord Forest, HexCoord Mountain);
public sealed record SettlementDayReceipt(string Id, string SettlementId, string BuildingId,
    long Tick, int Employees, long Wages, string? Output, long Quantity)
{
    public int Laborers { get; init; }
    public string? OwnerId { get; init; }
    public bool Automatic { get; init; }
    public long GrossQuantity { get; init; }
    public long TaxQuantity { get; init; }
    public decimal TaxRemainder { get; init; }
}
public sealed record SettlementInfluenceReceipt(string Id, string SettlementId, string OrganizationId,
    string ResourceId, long Quantity, long Points);
public sealed record SettlementConstructionRule(SettlementBuildingKind Kind, long Stones, long Wood);
public sealed record SettlementConstructionReceipt(string Id, string SettlementId, string BuildingId,
    string SponsorId, long Tick, SettlementBuildingKind Kind, HexCoord Position, long Stones, long Wood, long Points);
