using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
using System.Text.Json.Nodes;

internal static class HexWorldTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(WorldState world)
    { try { _ = new SimulationEngine(world); } catch (InvalidDataException) { return; } throw new Exception("Corrupt hex state accepted"); }
    private static ActionIntent Travel(string id, string target, string actor = "player", DecisionSource source = DecisionSource.Player) =>
        new(id, actor, target, "travel", 1, 0, source, ActionKind.Travel);
    private static ActionIntent Build(WorldState w, string id, HexCoord at, string actor = "player", DecisionSource source = DecisionSource.Player) =>
        new(id, actor, at.ToString(), "spirit-stone", w.HexMap!.BuildingDefinitions[0].Cost, 0, source,
            ActionKind.ConstructBuilding, MissionId: w.HexMap.BuildingDefinitions[0].Id, OrganizationId: "sect");
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Hex layout layers, weighted paths and blocked destinations", () =>
        {
            var w = HexWorldScenario.Create(); var map = w.HexMap!;
            Check(map.Tiles.Count == 80 && map.Tiles.Any(t => t.Mountain) && map.Tiles.Any(t => t.Forest) && map.Tiles.Any(t => t.Terrain == HexTerrain.Desert), "Missing natural layers");
            Check(map.Tiles.Any(t => t.LocalOrganizationId == "sect") && w.Organizations.All(o => (int)o.Kind != 0), "Organization territory missing or removed country remains");
            Check(new HexCoord(0, 0).Neighbors().Distinct().Count() == 6, "Invalid adjacency");
            var from = HexNavigation.Resolve(map, "town")!.Value; var target = HexNavigation.Resolve(map, "dungeon")!.Value;
            Check(HexNavigation.Path(map, from, target)!.SequenceEqual(HexNavigation.Path(map, from, target)!), "Unstable path");
            Check(HexNavigation.Path(map, from, new(5, 1)) is null && HexNavigation.Path(map, from, new(1, 1)) is null, "Water or mountains traversed");
            var shortMap = new HexMapState { Movement = new(1, 8, 9), Tiles = [new(new(0,0)), new(new(1,0), Forest: true), new(new(2,0)), new(new(0,1)), new(new(1,1))] };
            Check(!HexNavigation.Path(shortMap, new(0,0), new(2,0))!.Contains(new(1,0)), "Ignored weighted movement");
        }),
        ("Hex travel progresses one cell, preserves busy rules and resumes exactly", () =>
        {
            var w = HexWorldScenario.Create(); var engine = new SimulationEngine(w);
            var from = w.Characters.Single(c => c.Id == "player").HexPosition;
            engine.Submit(Travel("trip", "dungeon")); Check(engine.Step().Single().Accepted, "Trip rejected");
            Check(engine.Snapshot().Characters.Single(c => c.Id == "player").HexPosition == from, "Moved at submission");
            engine.Submit(new("busy", "player", "player", "pill", 1, 0, DecisionSource.Player, ActionKind.Cultivate));
            Check(!engine.Step().Single().Accepted, "Travelling actor worked");
            var mid = engine.Snapshot(); var moved = mid.Characters.Single(c => c.Id == "player").HexPosition;
            Check(moved != from && from!.Value.Neighbors().Contains(moved!.Value), "Did not complete adjacent step");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(mid)));
            for (int n = 0; n < 30; n++) { engine.Step(); resumed.Step(); }
            Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Continuation diverged");
            Check(engine.Snapshot().Characters.Single(c => c.Id == "player").DemoLocationId == "dungeon", "Trip failed to arrive");
        }),
        ("Hex travel shares player NPC validation and rejects impossible movement", () =>
        {
            var w = HexWorldScenario.Create(); var engine = new SimulationEngine(w);
            engine.Submit(Travel("lake", "hex:5:1")); Check(!engine.Step().Single().Accepted, "Lake accepted");
            engine.Submit(Travel("npc", "sect", "npc:01", DecisionSource.Npc)); Check(engine.Step().Single().Accepted, "NPC hex movement unavailable");
            var corrupt = engine.Snapshot(); corrupt.HexMap!.Journeys[0].Remaining.Insert(0, new(99,99)); Reject(corrupt);
            corrupt = HexWorldScenario.Create(); corrupt.HexMap!.Tiles.Add(corrupt.HexMap.Tiles[0]); Reject(corrupt);
        }),
        ("Hex construction reserves, cancels once and refunds the organization", () =>
        {
            var w = HexWorldScenario.Create(); w.TestProcurement = null; w.ProcurementQuotes.Clear();
            var engine = new SimulationEngine(w);
            long money = Inventory.Balance(w, "sect", "spirit-stone"); long cost = w.HexMap!.BuildingDefinitions[0].Cost;
            engine.Submit(Build(w, "build", new(4,3))); Check(engine.Step().Single().Accepted, "Build rejected");
            Check(Inventory.Balance(engine.Snapshot(), "sect", "spirit-stone") == money - cost, "Budget not reserved");
            var missingMap = engine.Snapshot(); missingMap.HexMap = null; Reject(missingMap);
            var cancel = new ActionIntent("cancel", "player", "build", "spirit-stone", 1, 0, DecisionSource.Player, ActionKind.CancelBuilding, OrganizationId: "sect");
            engine.Submit(cancel); Check(engine.Step().Single().Accepted, "Cancel rejected");
            Check(Inventory.Balance(engine.Snapshot(), "sect", "spirit-stone") == money, "Refund mismatch");
            engine.Submit(cancel); Check(!engine.Step().Single().Accepted, "Refund replay accepted");
            engine.Submit(cancel with { Id = "again" }); Check(!engine.Step().Single().Accepted, "Second refund accepted");
            Check(engine.Snapshot().Scheduled.Count == 0, "Orphaned construction schedule");
        }),
        ("Hex construction validates ownership, slot, leader, budget and completion", () =>
        {
            var w = HexWorldScenario.Create(); var engine = new SimulationEngine(w);
            engine.Submit(Build(w, "outside", new(2,3))); Check(!engine.Step().Single().Accepted, "Built outside territory");
            engine.Submit(Build(w, "member", new(4,3), "npc:04", DecisionSource.Npc)); Check(!engine.Step().Single().Accepted, "Ordinary member built");
            var poor = HexWorldScenario.Create(); poor.HexMap!.BuildingDefinitions[0] = poor.HexMap.BuildingDefinitions[0] with { Cost = long.MaxValue };
            var poorEngine = new SimulationEngine(poor); poorEngine.Submit(Build(poor, "poor", new(4,3))); Check(!poorEngine.Step().Single().Accepted, "Insufficient budget accepted");
            engine.Submit(Build(w, "good", new(4,3))); Check(engine.Step().Single().Accepted, "Valid build rejected");
            for (int n = 0; n < w.HexMap!.BuildingDefinitions[0].Ticks; n++) engine.Step();
            var final = engine.Snapshot(); Check(final.HexMap!.Buildings.Single(b => b.Id == "good").Status == BuildingStatus.Complete, "Never completed");
            engine.Submit(Build(w, "occupied", new(4,3))); Check(!engine.Step().Single().Accepted, "Double building accepted");
            Check(final.Ledger.Count(e => e.ActionId == "good" && e.Reason == "BuildingConsumed") == 1, "Consumption mismatch");
        }),
        ("Hex lost territory suspends construction without consuming funds and can refund", () =>
        {
            var w = HexWorldScenario.Create(); var engine = new SimulationEngine(w);
            engine.Submit(Build(w, "lost-land", new(4,3))); engine.Step();
            var changed = engine.Snapshot(); int tile = changed.HexMap!.Tiles.FindIndex(t => t.Coord == new HexCoord(4,3));
            changed.HexMap.Tiles[tile] = changed.HexMap.Tiles[tile] with { LocalOrganizationId = "gang" };
            engine = new SimulationEngine(changed);
            for (int n = 0; n < 8; n++) engine.Step();
            var paused = engine.Snapshot();
            Check(paused.HexMap!.Buildings.Single(b => b.Id == "lost-land").Status == BuildingStatus.Suspended, "Construction did not suspend");
            Check(Inventory.Balance(paused, "$escrow:lost-land", "spirit-stone") == w.HexMap!.BuildingDefinitions[0].Cost, "Suspension consumed funds");
            engine.Submit(new("refund-lost", "player", "lost-land", "spirit-stone", 1, 0, DecisionSource.Player, ActionKind.CancelBuilding, OrganizationId: "sect"));
            Check(engine.Step().Single().Accepted, "Suspended building could not be cancelled");
            var corrupt = HexWorldScenario.Create(); corrupt.HexMap!.BuildingDefinitions[0] = corrupt.HexMap.BuildingDefinitions[0] with { Cost = 0 }; Reject(corrupt);
        }),
        ("Hex constructed herb facility uses existing accepted work and stock", () =>
        {
            var w = HexWorldScenario.Create(); var engine = new SimulationEngine(w);
            engine.Submit(Build(w, "garden", new(4,3))); engine.Step(); for (int n = 0; n < 8; n++) engine.Step();
            engine.Submit(Travel("to-sect", "sect")); engine.Step(); for (int n = 0; n < 20; n++) engine.Step();
            engine.Submit(new("accept", "player", "sect", "herb", 1, 0, DecisionSource.Player, ActionKind.AcceptSectWork)); Check(engine.Step().Single().Accepted, "Work acceptance failed");
            engine.Submit(Travel("to-garden", "hex:4:3")); engine.Step(); for (int n = 0; n < 10; n++) engine.Step();
            engine.Submit(new("gather", "player", "grove", "herb", 1, 0, DecisionSource.Player, ActionKind.SectGather)); Check(engine.Step().Single().Accepted, "New building did not enable existing service");
            for (int n = 0; n < 5; n++) engine.Step();
            Check(engine.Snapshot().SectWorkReceipts.Any(r => r.Id == "gather" && r.Success), "No real work output");
        }),
        ("Hex saves preserve travel and construction while schema two remains demo", () =>
        {
            var engine = new SimulationEngine(HexWorldScenario.Create());
            engine.Submit(Build(engine.Snapshot(), "construction", new(4,3))); engine.Step();
            engine.Submit(Travel("npc-trip", "sect", "npc:01", DecisionSource.Npc)); engine.Step();
            string path = Path.Combine(Path.GetTempPath(), "hex-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new JsonWorldStore(); store.Save(path, engine.Snapshot());
                var resumed = new SimulationEngine(store.Load(path));
                for (int n = 0; n < 20; n++) { engine.Step(); resumed.Step(); }
                Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Physical save diverged");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            var demo = PlayerDemoScenario.Create("legacy"); var node = JsonNode.Parse(WorldCodec.Serialize(demo))!.AsObject(); node["SchemaVersion"] = 2; node.Remove("HexMap");
            try
            {
                File.WriteAllText(path, node.ToJsonString()); string source = File.ReadAllText(path);
                var loaded = new JsonWorldStore().Load(path); Check(loaded.HexMap is null && loaded.SchemaVersion == 3 && File.ReadAllText(path) == source, "Legacy migration changed world or source");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }),
        ("Hex autonomous economy advances and new game rebuilds the same scenario", () =>
        {
            var w = HexWorldScenario.Create(); var engine = new SimulationEngine(w);
            for (int n = 0; n < 160; n++) engine.Step(true);
            var final = engine.Snapshot();
            Check(final.Events.Any(e => e.Kind == "HexArrived" && !e.Detail.StartsWith("player:", StringComparison.Ordinal)), "NPCs never travelled");
            Check(final.SectWorkReceipts.Any(r => r.Success), "Economic work never succeeded");
            var reset = NewGameFactory.CreateLike(final, "重開"); Check(reset.HexMap is not null && reset.Clock.Tick == 0 && reset.HexMap.Journeys.Count == 0, "New hex save lost scenario");
        })
    };
}
