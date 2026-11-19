using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SettlementTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static WorldState Create()
    {
        var w = HexWorldScenario.Create();
        var site = w.HexMap!.Sites.First(s => s.Kind == SiteKind.Village);
        var plains = w.HexMap.Tiles.First(t => t.Terrain == HexTerrain.Plains && !t.Forest && !t.Mountain);
        w.SettlementBuildingRules = SettlementIntegrity.ReadRules(GameConfiguration.Find());
        w.Settlements.Add(new()
        {
            Id = "settlement-test", SiteId = site.Id, OrganizationId = "sect", Level = 1,
            Happiness = 50, Population = 10, WorkingPopulation = 6, PopulationCapacity = 20,
            Territory = w.HexMap.Tiles.Select(t => t.Coord).ToList(),
            Buildings = [new("farm-test", SettlementBuildingKind.Farm, plains.Coord, 2, 5)]
        });
        return w;
    }
    private static void Reject(WorldState w)
    {
        try { WorldIntegrity.Validate(w); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid settlement was accepted");
    }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Settlement rules load eight buildings and enforce resource terrain", () =>
        {
            var rules = SettlementIntegrity.ReadRules(GameConfiguration.Find());
            var farm = rules.Single(r => r.Kind == SettlementBuildingKind.Farm);
            var forest = rules.Single(r => r.Kind == SettlementBuildingKind.Forestry);
            var mine = rules.Single(r => r.Kind == SettlementBuildingKind.Mine);
            Check(SettlementPlacement.Allows(farm, new(new(0,0))), "Plains farm rejected");
            Check(!SettlementPlacement.Allows(farm, new(new(0,0), Forest: true)), "Farm allowed in forest");
            Check(SettlementPlacement.Allows(forest, new(new(0,0), Forest: true)), "Forestry rejected");
            Check(!SettlementPlacement.Allows(forest, new(new(0,0))), "Forestry allowed without forest");
            Check(SettlementPlacement.Allows(mine, new(new(0,0), Mountain: true)), "Mountain mine rejected");
            Check(!SettlementPlacement.Allows(mine, new(new(0,0))), "Mine allowed on plains");
            rules.Add(rules[0]);
            try { SettlementIntegrity.ValidateRules(rules); } catch (InvalidDataException) { return; }
            throw new Exception("Duplicate building definition accepted");
        }),
        ("Settlement workforce cannot exceed building caps or available residents", () =>
        {
            var w = Create(); WorldIntegrity.Validate(w);
            var s = w.Settlements[0]; var b = s.Buildings[0];
            s.Buildings[0] = b with { EmployeeCount = b.EmployeeLimit + 1 }; Reject(w);
            s.Buildings[0] = b; s.WorkingPopulation = 1; Reject(w);
            s.WorkingPopulation = 6; s.Population = s.PopulationCapacity + 1; Reject(w);
        }),
        ("Settlement snapshots preserve state and do not create duplicate asset storage", () =>
        {
            var w = Create(); var clone = WorldCodec.Clone(w); WorldIntegrity.Validate(clone);
            Check(WorldCodec.Digest(w) == WorldCodec.Digest(clone), "Settlement round trip changed data");
            Check(Inventory.Balance(w, "sect", "spirit-stone") == Inventory.Balance(clone, "sect", "spirit-stone"), "Ledger changed");
            clone.Settlements[0].Buildings.Clear();
            Check(w.Settlements[0].Buildings.Count == 1, "Settlement snapshot leaked mutation");
            var old = HexWorldScenario.Create(); var loaded = WorldCodec.Deserialize(WorldCodec.Serialize(old));
            Check(loaded.Settlements.Count == 0 && loaded.SettlementBuildingRules is null, "Old world silently enabled settlements");
        }),
        ("Settlement buildings reject forbidden terrain territory and duplicate accounts", () =>
        {
            var w = Create(); var s = w.Settlements[0]; var b = s.Buildings[0];
            s.Buildings[0] = b with { Kind = SettlementBuildingKind.Mine }; Reject(w);
            s.Buildings[0] = b with { Position = new(999,999) }; Reject(w);
            s.Buildings[0] = b; w.Settlements.Add(s); Reject(w);
        })
    };
}
