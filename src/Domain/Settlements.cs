namespace Cultivation.Domain;

public enum SettlementBuildingKind { TownHall, Housing, Farm, Forestry, Mine, Smithy, Pharmacy, Shop, Dormitory, SeniorDormitory }
public enum SettlementTerrainRequirement { Land, Plains, Forest, Mountain }
public sealed record SettlementBuildingRule(SettlementBuildingKind Kind,
    SettlementTerrainRequirement Terrain, bool RequiresEmployees);
public sealed record SettlementBuilding(string Id, SettlementBuildingKind Kind, HexCoord Position,
    int EmployeeCount, int EmployeeLimit)
{
    public long PaidThroughTick { get; init; }
    public string? OwnerOrganizationId { get; init; }
    public int? Level { get; init; }
    public decimal TaxRemainder { get; init; }
    public int LaborCount { get; init; }
}
// Assets belong to the existing OrganizationId ledger account, never a duplicate inventory.
public sealed class SettlementState
{
    public string Id { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string ResidentsAccountId { get; set; } = "";
    public long LastPayrollTick { get; set; }
    public Dictionary<string, long> Influence { get; set; } = [];
    public int Level { get; set; }
    public decimal Happiness { get; set; }
    public int Population { get; set; }
    public long GrowthProgress { get; set; }
    public int PopulationCapacity { get; set; }
    public int WorkingPopulation { get; set; }
    public List<HexCoord> Territory { get; set; } = [];
    public List<SettlementBuilding> Buildings { get; set; } = [];
}

public static class SettlementPlacement
{
    public static bool Allows(SettlementBuildingRule rule, HexTile tile) => rule.Terrain switch
    {
        SettlementTerrainRequirement.Land => tile.Terrain != HexTerrain.Lake && !tile.Mountain,
        SettlementTerrainRequirement.Plains => tile.Terrain == HexTerrain.Plains && !tile.Forest && !tile.Mountain,
        SettlementTerrainRequirement.Forest => tile.Forest && !tile.Mountain && tile.Terrain != HexTerrain.Lake,
        SettlementTerrainRequirement.Mountain => tile.Mountain && tile.Terrain != HexTerrain.Lake,
        _ => false
    };
}
