using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class DungeonLootTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static WorldState World(bool wolf, bool team = false)
    {
        var w = SectEconomyScenario.Create();
        w.SectDuty = null; w.SectPayroll = null; w.TestProcurement = null;
        w.Dungeon = w.Dungeon! with { EncounterChance = 1, WolfChance = wolf ? 1 : 0, BanditStoneChance = 1, BanditPillChance = 1 };
        w.Characters = w.Characters.Select(c => c.Id is "player" or "npc:00" or "npc:01" ? c with { DemoLocationId = "dungeon", BaseAttack = 100 } : c).ToList();
        if (team) w.Parties.Add(new("loot-party", "player", ["player", "npc:00", "npc:01"]));
        return w;
    }
    private static SimulationEngine Explore(WorldState w)
    {
        var e = new SimulationEngine(w);
        e.Submit(new("configured-loot", "player", "dungeon", "combat", 1, 0, DecisionSource.Player, ActionKind.ExploreDungeon));
        Check(e.Step().Single().Accepted, "Exploration rejected"); return e;
    }
    private static void Finish(SimulationEngine e) { for (int n = 0; n < 12; n++) e.Step(); }
    private static void Reject(WorldState w)
    {
        try { _ = new SimulationEngine(w); }
        catch (InvalidDataException ex) { Check(ex.Message.Contains("combat/dungeon.json"), "Missing config path"); return; }
        throw new Exception("Invalid drop table accepted");
    }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("New worlds load drop quantities from config and retain them independently in saves", () => {
            string directory = Path.Combine(Path.GetTempPath(), "loot-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                string source = GameConfiguration.Find().DirectoryPath;
                foreach (string file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
                { string target = Path.Combine(directory, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
                string path = Path.Combine(directory, "combat", "dungeon.json");
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
                node["Loot"]!["Wolf"]!["MinimumQuantity"] = 6; node["Loot"]!["Wolf"]!["MaximumQuantity"] = 6;
                File.WriteAllText(path, node.ToJsonString());
                var world = SectEconomyScenario.Create(configuration: new GameConfiguration(directory));
                Check(world.Dungeon!.Loot!.Wolf.MaximumQuantity == 6, "Config ignored");
                var saved = WorldCodec.Serialize(world);
                node["Loot"]!["Wolf"]!["MaximumQuantity"] = 7; File.WriteAllText(path, node.ToJsonString());
                Check(WorldCodec.Deserialize(saved).Dungeon!.Loot!.Wolf.MaximumQuantity == 6, "Snapshot reread current config");
                node["Loot"]!["Wolf"]!["ResourceId"] = "missing"; File.WriteAllText(path, node.ToJsonString());
                try { _ = SectEconomyScenario.Create(configuration: new GameConfiguration(directory)); }
                catch (InvalidDataException ex) { Check(ex.Message.Contains("Loot.Wolf"), "Missing field path"); return; }
                throw new Exception("Invalid config item accepted");
            } finally { Directory.Delete(directory, true); }
        }),
        ("Configured monster drops reach solo and party winners without truncation or duplication", () => {
            foreach (bool team in new[] { false, true })
            {
                var w = World(true, team);
                w.Dungeon = w.Dungeon! with { Loot = w.Dungeon!.Loot! with { Wolf = new("beast-hide", 4, 4) } };
                var e = Explore(w); var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
                Finish(e); Finish(resumed); var after = e.Snapshot();
                Check(after.Ledger.Where(l => l.ActionId == "configured-loot" && l.Reason == "DungeonSpawnLoot").Sum(l => l.Quantity) == 4, "Spawn amount wrong");
                Check((team ? after.PartyLoot.Sum(l => l.Quantity) : after.Loot.Sum(l => l.Quantity)) == 4, "Reward truncated or duplicated");
                Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Save continuation diverged");
            }
        }),
        ("Drop resource IDs and bandit quantity ranges affect issuance and rewards", () => {
            var w = World(false);
            w.Dungeon = w.Dungeon! with { Loot = new(new("beast-hide", 1, 1), new("spirit-stone", 7, 9), new("herb", 3, 3)) };
            var e = Explore(w); Finish(e); var after = e.Snapshot();
            Check(after.Loot.Single(l => l.ResourceId == "spirit-stone").Quantity is >= 7 and <= 9, "Money range ignored");
            Check(after.Loot.Single(l => l.ResourceId == "herb").Quantity == 3 && after.Loot.All(l => l.ResourceId != w.RecoveryResourceId), "Configured item ignored");
        }),
        ("Malformed drop ranges unknown resources duplicate drops and probabilities are rejected", () => {
            foreach (var drop in new[] { new DungeonDrop("missing", 1, 1), new("herb", 0, 1), new("herb", 3, 2), new("herb", 1, long.MaxValue) })
            { var w = World(true); w.Dungeon = w.Dungeon! with { Loot = w.Dungeon!.Loot! with { Wolf = drop } }; Reject(w); }
            var duplicate = World(false); duplicate.Dungeon = duplicate.Dungeon! with { Loot = duplicate.Dungeon!.Loot! with { BanditRecovery = new("spirit-stone", 1, 1) } }; Reject(duplicate);
            var probability = World(true); probability.Dungeon = probability.Dungeon! with { BanditPillChance = -1 }; Reject(probability);
        }),
        ("Legacy drops and hide stocks survive display rename without loading new rules", () => {
            var w = World(true); w.Dungeon = w.Dungeon! with { Loot = null };
            w.Items = w.Items.Select(i => i.Id == "beast-hide" ? i with { Name = "狼皮" } : i).ToList();
            string directory = Path.Combine(Path.GetTempPath(), "hide-" + Guid.NewGuid().ToString("N"));
            try {
                string path = Path.Combine(directory, "legacy.json"); var store = new JsonWorldStore(); store.Save(path, w); var loaded = store.Load(path);
                Check(loaded.Items.Single(i => i.Id == "beast-hide").Name == "獸皮" && loaded.Dungeon!.Loot is null && loaded.Ledger.SequenceEqual(w.Ledger), "Legacy data changed");
                var e = Explore(loaded); Finish(e); Check(e.Snapshot().Loot.Single().Quantity == 1, "Legacy wolf drop changed");
            } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        })
    };
}
