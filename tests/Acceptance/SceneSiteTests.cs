using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class SceneSiteTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(WorldState world)
    { try { _ = new SimulationEngine(world); } catch (InvalidDataException) { return; } throw new Exception("Invalid scene accepted"); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Scene sites cover six types and preserve co-located independent identities", () =>
        {
            var w = HexWorldScenario.Create(); var sites = w.HexMap!.Sites;
            Check(sites.Select(s => s.Kind).Distinct().Count() == 6, "Missing scene kind");
            Check(SceneSites.At(w, new(2,3)).Count() == 2, "Co-located entrances lost");
            var town = sites.Single(s => s.Id == "settlement:qingyun"); var gang = sites.Single(s => s.OrganizationId == "gang");
            Check(town.Id != gang.Id && !ReferenceEquals(town.Slots, gang.Slots), "Scene layouts shared mutable town");
            string other = WorldCodec.Serialize(w); int index = sites.FindIndex(s => s.Id == town.Id);
            sites[index] = town with { MainLevel = 2 };
            Check(gang.MainLevel == 1 && gang.Slots.Count(s => s.RequiredLevel <= gang.MainLevel) == 2, "Level leaked to another site");
            Check(WorldCodec.Deserialize(other).HexMap!.Sites.Single(s => s.Id == town.Id).MainLevel == 1, "Snapshot isolation failed");
        }),
        ("Scene entry needs physical arrival and cannot use a forged anchor", () =>
        {
            var w = HexWorldScenario.Create(); var map = w.HexMap!;
            var sect = map.Sites.Single(s => s.OrganizationId == "sect");
            Check(!SceneSites.CanEnter(w, sect), "Remote entry allowed");
            Check(!SceneSites.CanEnter(w, sect with { Anchor = new(2,3) }), "Forged anchor bypassed arrival");
            var engine = new SimulationEngine(w);
            engine.Submit(new("arrive-site", "player", "sect", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel)); engine.Step();
            Check(!SceneSites.CanEnter(engine.Snapshot(), sect), "Travel counted as arrival");
            for (int n = 0; n < 20; n++) engine.Step();
            Check(SceneSites.CanEnter(engine.Snapshot(), sect), "Arrived player could not enter");
        }),
        ("Scene definitions reject duplicate identities slots invalid owners and blocked anchors", () =>
        {
            var w = HexWorldScenario.Create(); w.HexMap!.Sites.Add(w.HexMap.Sites[0]); Reject(w);
            w = HexWorldScenario.Create(); w.HexMap!.Sites[0].Slots.Add(w.HexMap.Sites[0].Slots[0]); Reject(w);
            w = HexWorldScenario.Create(); w.HexMap!.Sites[0] = w.HexMap.Sites[0] with { OrganizationId = "gang" }; Reject(w);
            w = HexWorldScenario.Create(); w.HexMap!.Sites[0] = w.HexMap.Sites[0] with { Anchor = new(5,1) }; Reject(w);
        }),
        ("Scene metadata round trips without copying inventory or adding sites to old saves", () =>
        {
            var w = HexWorldScenario.Create(); long stones = Inventory.Balance(w, "sect", "spirit-stone");
            string path = Path.Combine(Path.GetTempPath(), "site-save-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new JsonWorldStore(); store.Save(path, w); var loaded = store.Load(path);
                Check(WorldCodec.Digest(loaded) == WorldCodec.Digest(w) && Inventory.Balance(loaded, "sect", "spirit-stone") == stones, "Scene roundtrip changed assets");
                Check(NewGameFactory.CreateLike(loaded, "新場景").HexMap!.Sites.Count == 6, "New save lost scene profile");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            var old = HexWorldScenario.Create(); old.HexMap!.Sites.Clear();
            Check(new SimulationEngine(old).Snapshot().HexMap!.Sites.Count == 0, "Old world silently populated scenes");
        })
    };
}
