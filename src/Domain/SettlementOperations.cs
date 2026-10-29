namespace Cultivation.Domain;

public sealed record SettlementOperationRules(int InitialBuildingLevel,int EmployeesPerLevel,long UpgradeStones,long UpgradeWood)
{
    // Absent in historical snapshots: retain their elapsed-period settlement rules.
    public bool FixedDailySettlement { get; init; }
}
public sealed record SettlementTaxRules(decimal ProductionRate);
public sealed record SettlementBuildingUpgradeReceipt(string Id,string SettlementId,string BuildingId,string OwnerId,
    long Tick,int ResultLevel,long Stones,long Wood);
