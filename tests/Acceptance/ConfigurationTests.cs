using System.Text.Json.Nodes;
using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Simulation;

internal static class ConfigurationTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Rejects(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid configuration was accepted");
    }
    private static void WithConfig(Action<GameConfiguration> body)
    {
        string path = Path.Combine(Path.GetTempPath(), "cultivation-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            string source = GameConfiguration.Find().DirectoryPath;
            foreach (string file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                string target = Path.Combine(path, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            body(new(path));
        }
        finally { Directory.Delete(path, recursive: true); }
    }
    private static void Edit(GameConfiguration c, string file, Action<JsonNode> edit)
    {
        string path = Path.Combine(c.DirectoryPath, file);
        var node = JsonNode.Parse(File.ReadAllText(path))!; edit(node); File.WriteAllText(path, node.ToJsonString());
    }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("NPC aspiration quotas persist through every scenario and new game reset", () =>
        {
            var worlds = new[] { AutonomousWorld.Create(count: 30), AutonomousWorld.Create(), AutonomousWorld.Create(count: 50),
                MissionScenario.Create(), AdvancementScenario.Create(), CombatScenario.Create(), SocialScenario.Create(),
                PlayerDemoScenario.Create("志向配額"), SectEconomyScenario.Create("志向配額") };
            void Validate(WorldState w)
            {
                var npcs = w.Characters.Where(a => a.Id != w.ControlledCharacterId && !a.Hostile && a.MonsterKind == MonsterKind.None).ToArray();
                foreach (var aspiration in Enum.GetValues<Aspiration>().Where(a => a != Aspiration.Balanced))
                    Check(npcs.Count(a => a.Aspiration == aspiration) == 3, $"Wrong {aspiration} quota with {npcs.Length} NPCs");
                Check(npcs.Count(a => a.Aspiration == Aspiration.Balanced) == npcs.Length - 12, "Remaining NPCs are not balanced");
            }
            foreach (var w in worlds)
            {
                Validate(w); Validate(WorldCodec.Deserialize(WorldCodec.Serialize(w)));
                Validate(NewGameFactory.CreateLike(w, "志向配額"));
            }
        }),
        ("External production and character configuration changes real brewing costs and duration", () => WithConfig(c =>
        {
            Edit(c, "economy/production.json", n => { n["HerbsPerPill"] = 3; n["BrewTicks"] = 6; });
            Edit(c, "characters/autonomous.json", n => n.AsArray().Single(x => x!["Id"]!.GetValue<string>() == "npc:01")!["AlchemyChance"] = 1);
            var w = AutonomousWorld.Create(configuration: c); var e = new SimulationEngine(w);
            e.Submit(new("configured-brew", "npc:01", "npc:01", "herb", 1, 0, DecisionSource.Npc, ActionKind.Brew));
            Check(e.Step().Single().Accepted && e.Snapshot().Scheduled.Single().DueTick == 7, "Configured duration not used");
            Check(Inventory.Balance(e.Snapshot(), "npc:01", "herb") == 1, "Configured ingredients not reserved");
            for (int n = 0; n < 6; n++) e.Step();
            Check(Inventory.Balance(e.Snapshot(), "npc:01", "pill") == 1, "Configured actor did not brew");
        })),
        ("External sect reward and procurement settings are used and saved independently of files", () => WithConfig(c =>
        {
            Edit(c, "organizations/facilities.json", n => n["GatherContribution"] = 17);
            Edit(c, "economy/procurement.json", n => n["DemandPerItem"] = 7);
            var w = SectEconomyScenario.Create(configuration: c);
            Check(w.TestProcurement!.DemandPerItem == 7, "Configured demand not used");
            w.Characters[0] = w.Characters[0] with { DemoLocationId = "grove" };
            w.SectWorkAssignments.Add(new("player", "sect", "herb")); var e = new SimulationEngine(w);
            e.Submit(new("configured-work", "player", "grove", "herb", 1, 0, DecisionSource.Player, ActionKind.SectGather)); e.Step();
            string saved = WorldCodec.Serialize(e.Snapshot());
            Edit(c, "organizations/facilities.json", n => n["GatherContribution"] = 99);
            File.WriteAllText(Path.Combine(c.DirectoryPath, "economy/production.json"), "invalid JSON");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(saved));
            for (int n = 0; n < w.Rules.GatherTicks; n++) { e.Step(); resumed.Step(); }
            Check(Inventory.Balance(resumed.Snapshot(), "player", "contribution") == 17, "Save used new external reward");
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Configured continuation diverged");
        })),
        ("Configured travel timing and item labels survive save load", () => WithConfig(c =>
        {
            Edit(c, "world/demo.json", n => { n["TownTravelTicks"] = 9; n["SecondsPerTick"] = 120; });
            Edit(c, "items/catalog.json", n => n.AsArray().Single(x => x!["Id"]!.GetValue<string>() == "herb")!["Name"] = "測試靈草");
            var w = PlayerDemoScenario.Create("設定測試", configuration: c); var e = new SimulationEngine(w);
            e.Submit(new("configured-trip", "player", "grove", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
            Check(e.Step().Single().Accepted && e.Snapshot().Scheduled.Single().DueTick == 10, "Configured travel duration not used");
            var saved = WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot()));
            Check(saved.Clock.SimulatedSecondsPerTick == 120 && saved.Items.Single(i => i.Id == "herb").Name == "測試靈草", "World metadata lost");
        })),
        ("Invalid missing malformed unknown and negative configuration is rejected", () => WithConfig(c =>
        {
            string path = Path.Combine(c.DirectoryPath, "economy/production.json"), original = File.ReadAllText(path);
            File.WriteAllText(path, "{"); Rejects(() => AutonomousWorld.Create(configuration: c));
            File.WriteAllText(path, original); Edit(c, "economy/production.json", n => n["BrewTickTypo"] = 6);
            Rejects(() => AutonomousWorld.Create(configuration: c));
            File.WriteAllText(path, original); Edit(c, "economy/production.json", n => n["HerbsPerPill"] = -1);
            Rejects(() => AutonomousWorld.Create(configuration: c));
            File.Delete(path); Rejects(() => AutonomousWorld.Create(configuration: c));
        })),
        ("Combat aspiration increases engagement and power strike while keeping safety and information gates", () =>
        {
            var w = CombatScenario.Create(); var planner = new UtilityPlanner();
            var actor = new CharacterState("probe", "戰鬥志向測試") { BaseAttack = 20, BaseDefense = 2, Caution = 0.5, CombatDrive = 2 };
            var opponent = new OpponentObservation("enemy", 40, 3, 0, "sight:valley", 1);
            var o = new LocalObservation(actor, 1, 100, 0, 1, 100, 100, 100, 100, w.Rules, "market:valley", 1,
                CombatRules: w.CombatRules, Opponents: new[] { opponent }, Aspirations: w.Aspirations);
            var combat = o with { Actor = actor with { Aspiration = Aspiration.Combat } };
            double Score(LocalObservation x, ActionKind kind) => planner.Evaluate(x).Single(a => a.Intent.Kind == kind).Score;
            Check(Score(combat, ActionKind.Engage) > Score(o, ActionKind.Engage), "Combat aspiration did not increase engagement");
            Check(Score(combat, ActionKind.Gather) == Score(o, ActionKind.Gather), "Combat aspiration altered gathering benefit");
            var battle = new BattleObservation("battle", opponent, false);
            Check(Score(combat with { Battle = battle }, ActionKind.PowerStrike) > Score(o with { Battle = battle }, ActionKind.PowerStrike), "Combat aspiration did not increase strong attack intent");
            Check(!planner.Evaluate(combat with { Actor = combat.Actor with { Health = 10 } }).Any(a => a.Intent.Kind == ActionKind.Engage), "Unsafe health gate bypassed");
            Check(!planner.Evaluate(combat with { Opponents = new[] { opponent with { ObservedTick = 0 } } }).Any(a => a.Intent.Kind == ActionKind.Engage), "Stale information used");
            Check(!planner.Evaluate(combat with { Pills = 0, Opponents = new[] { opponent with { Attack = 1000 } } }).Any(a => a.Intent.Kind == ActionKind.Engage), "Overwhelming risk ignored");
            Check(w.Characters.Single(a => a.Id == "npc:00").Aspiration == Aspiration.Combat, "Combat scenario did not assign combat aspiration");
        }),
        ("Combat multiplier from JSON affects dungeon combat benefit without changing herb event benefit", () => WithConfig(c =>
        {
            var w = SectEconomyScenario.Create(configuration: c); var planner = new UtilityPlanner();
            var actor = w.Characters[0] with { Aspiration = Aspiration.Combat, WealthWeight = 1, Caution = 0 };
            var o = new LocalObservation(actor, 1, 100, 0, 0, 100, 100, 100, 100, w.Rules, "market:valley", 1,
                CombatRules: w.CombatRules, Dungeon: w.Dungeon, Aspirations: w.Aspirations);
            double Score(LocalObservation x) => planner.Evaluate(x).Single(a => a.Intent.Kind == ActionKind.ExploreDungeon).Score;
            Edit(c, "characters/aspirations.json", n => n.AsArray().Single(x => x!["Id"]!.GetValue<string>() == "Combat")!["CombatMultiplier"] = 3);
            var tuned = SectEconomyScenario.Create(configuration: c);
            Check(Score(o with { Aspirations = tuned.Aspirations }) > Score(o), "Configured combat multiplier unused");
            Check(Score(o with { Dungeon = w.Dungeon! with { EncounterChance = 0 }, Aspirations = tuned.Aspirations }) == Score(o with { Dungeon = w.Dungeon! with { EncounterChance = 0 } }), "Combat preference boosted pure herb event");
            var reloaded = WorldCodec.Deserialize(WorldCodec.Serialize(tuned));
            Check(reloaded.Aspirations.Single(a => a.Id == Aspiration.Combat).CombatMultiplier == 3, "Combat settings not saved");
        })),
        ("Legacy saves without configuration catalogs retain original aspirations and rules", () =>
        {
            var original = SocialScenario.Create(); var node = JsonNode.Parse(WorldCodec.Serialize(original))!.AsObject();
            foreach (string key in new[] { "Items", "Currencies", "Aspirations", "MapRules", "MissionDefaults" }) node.Remove(key);
            var old = WorldCodec.Deserialize(node.ToJsonString()); var engine = new SimulationEngine(old); engine.Step(true);
            Check(old.Aspirations.Count == 0 && (int)Aspiration.Cultivation == 3 && (int)Aspiration.Combat == 4, "Legacy aspiration IDs changed");
            Check(DemoMap.TravelTicks("town", "sect", old.MapRules) == 3 && old.Rules == original.Rules, "Legacy rule defaults changed");
        })
    };
}
