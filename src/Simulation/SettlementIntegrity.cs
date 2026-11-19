using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SettlementIntegrity
{
    public static List<SettlementBuildingRule> ReadRules(GameConfiguration config)
    {
        var rules = config.Read<List<SettlementBuildingRule>>("settlements/buildings.json");
        ValidateRules(rules);
        return rules;
    }

    public static void ValidateRules(List<SettlementBuildingRule> rules)
    {
        if (rules.Count is not (8 or 10) || !Enum.GetValues<SettlementBuildingKind>().Take(8).All(k=>rules.Any(r=>r.Kind==k)) ||
            rules.Select(r => r.Kind).Distinct().Count() != rules.Count ||
            rules.Any(r => !Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Terrain)))
            throw new InvalidDataException("config/settlements/buildings.json: Kind/Terrain must define each building exactly once");
    }

    public static void Validate(WorldState w)
    {
        if (w.Settlements.Count == 0 && w.SettlementBuildingRules is null) return;
        if (w.SettlementBuildingRules is null || w.HexMap is null)
            throw new InvalidDataException("Settlements require a hex map and a building rules snapshot");
        ValidateRules(w.SettlementBuildingRules);
        if (w.Settlements.Select(s => s.Id).Distinct().Count() != w.Settlements.Count ||
            w.Settlements.Select(s => s.OrganizationId).Distinct().Count() != w.Settlements.Count ||
            w.Settlements.Select(s => s.SiteId).Distinct().Count() != w.Settlements.Count)
            throw new InvalidDataException("Duplicate settlement identity, account or scene");
        foreach (var s in w.Settlements)
        {
            var site = w.HexMap.Sites.SingleOrDefault(site => site.Id == s.SiteId);
            if (string.IsNullOrWhiteSpace(s.Id) || !w.Organizations.Any(o => o.Id == s.OrganizationId) ||
                site is null || site.Kind is not (SiteKind.Village or SiteKind.Town or SiteKind.City) ||
                s.Level <= 0 || s.Population < 0 || s.PopulationCapacity < s.Population ||
                s.WorkingPopulation < 0 || s.WorkingPopulation > s.Population ||
                s.Territory.Count == 0 || s.Territory.Distinct().Count() != s.Territory.Count ||
                !s.Territory.Contains(site.Anchor) || s.Territory.Any(c => !w.HexMap.Tiles.Any(t => t.Coord == c)))
                throw new InvalidDataException($"Invalid settlement population, scene, territory or account: {s.Id}");
            if (s.Buildings.Select(b => b.Id).Distinct().Count() != s.Buildings.Count ||
                s.Buildings.Sum(b => (long)b.EmployeeCount) > s.WorkingPopulation)
                throw new InvalidDataException($"Duplicate building or overallocated settlement workforce: {s.Id}");
            foreach (var b in s.Buildings)
            {
                var rule = w.SettlementBuildingRules.SingleOrDefault(r => r.Kind == b.Kind);
                var tile = w.HexMap.Tiles.SingleOrDefault(t => t.Coord == b.Position);
                if (string.IsNullOrWhiteSpace(b.Id) || rule is null || tile is null ||
                    !s.Territory.Contains(b.Position) || !SettlementPlacement.Allows(rule, tile) ||
                    b.EmployeeCount < 0 || b.EmployeeLimit < 0 || b.EmployeeCount > b.EmployeeLimit ||
                    (!rule.RequiresEmployees && (b.EmployeeCount != 0 || b.EmployeeLimit != 0)))
                    throw new InvalidDataException($"Invalid settlement building terrain or employees: {s.Id}/{b.Id}");
            }
        }
    }
}
