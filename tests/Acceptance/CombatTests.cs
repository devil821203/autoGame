using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class CombatTests
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static ActionIntent Command(string id, ActionKind kind, string actor = "player", string target = "bandit:weak") =>
        new(id, actor, target, kind is ActionKind.PowerStrike or ActionKind.CombatHeal ? "pill" : "combat", 1, 0,
            actor == "player" ? DecisionSource.Player : DecisionSource.Npc, kind);
    private static SimulationEngine Started(WorldState? w = null)
    {
        var engine = new SimulationEngine(w ?? CombatScenario.Create());
        engine.Submit(Command("fight", ActionKind.Engage));
        Check(engine.Step().Single().Accepted, "Could not start combat"); return engine;
    }
    private static void Rejects(WorldState w)
    {
        bool rejected = false; try { _ = new SimulationEngine(w); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid combat save accepted");
    }

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Defeated nonlethal characters can recover through shared healing rules", () =>
        {
            var w = CombatScenario.Create(); int index = w.Characters.FindIndex(c => c.Id == "bandit:weak");
            w.Characters[index] = w.Characters[index] with { BaseAttack = 1000, BaseDefense = 100 };
            var engine = Started(w); engine.Step(); engine.Step();
            Check(engine.Snapshot().Characters[0].Health == 1 && engine.Snapshot().Combats.Single().Status == CombatStatus.Victory, "Defeat did not end nonlethally");
            engine.Submit(new("recover", "player", "player", "pill", 1, 0, DecisionSource.Player, ActionKind.Heal));
            Check(engine.Step().Single().Accepted, "Defeated actor could not recover");
            for (int n = 0; n < w.Rules.CultivateTicks; n++) engine.Step();
            Check(engine.Snapshot().Characters[0].Health == 41 && Inventory.Balance(engine.Snapshot(), "player", "pill") == 2, "Recovery missing or free");
        }),
        ("Combat rounds use fixed ticks and block conflicting actions", () =>
        {
            var engine = Started(); var before = engine.Snapshot();
            engine.Submit(new("gather-in-battle", "player", "grove", "herb", 1, 0, DecisionSource.Player, ActionKind.Gather));
            Check(!engine.Step().Single().Accepted, "Combat actor worked simultaneously");
            Check(engine.Snapshot().Characters[0].Health == before.Characters[0].Health, "Damage happened before round");
            engine.Step(); var after = engine.Snapshot();
            Check(after.Combats.Single().Rounds == 1 && after.Characters[0].Health < before.Characters[0].Health && after.Characters.Single(c => c.Id == "bandit:weak").Health < 60, "Round damage missing");
        }),
        ("Combat skills consume pills once and heal actual injuries", () =>
        {
            var w = CombatScenario.Create(); w.Characters[0] = w.Characters[0] with { Health = 60 };
            var engine = Started(w);
            engine.Submit(Command("power", ActionKind.PowerStrike));
            engine.Submit(Command("second-command", ActionKind.CombatHeal));
            Check(engine.Step().Count(r => r.Accepted) == 1 && Inventory.Balance(engine.Snapshot(), "player", "pill") == 2, "Combat command or consumption duplicated");
            engine.Step(); var hurt = engine.Snapshot();
            Check(60 - hurt.Characters.Single(c => c.Id == "bandit:weak").Health >= 19 && !hurt.Combats.Single().InitiatorBoost, "Power strike missing or not cleared");
            int health = hurt.Characters[0].Health;
            engine.Submit(Command("heal", ActionKind.CombatHeal)); Check(engine.Step().Single().Accepted, "Combat healing rejected");
            Check(engine.Snapshot().Characters[0].Health == Math.Min(100, health + w.Rules.HealingPerPill) && Inventory.Balance(engine.Snapshot(), "player", "pill") == 1, "Healing did not use one pill");
        }),
        ("Victory transfers finite loot once without death or currency creation", () =>
        {
            var w = CombatScenario.Create(); w.Characters[0] = w.Characters[0] with { BaseAttack = 100, BaseDefense = 50 };
            var engine = Started(w); engine.Step(); engine.Step(); var final = engine.Snapshot();
            Check(final.Combats.Single().Status == CombatStatus.Victory && final.Combats.Single().WinnerId == "player", "Victory missing");
            Check(final.Characters.Single(c => c.Id == "bandit:weak").Health == 1, "Nonlethal rule violated");
            Check(Inventory.Balance(final, "player", "spirit-stone") == Inventory.Balance(w, "player", "spirit-stone") + w.CombatRules!.MaximumLootStones, "Loot missing");
            Check(Inventory.Balance(final, "bandit:weak", "spirit-stone") == 80 - w.CombatRules.MaximumLootStones && final.Loot.Count == 2, "Loot was minted");
            int entries = final.Ledger.Count; for (int n = 0; n < 10; n++) engine.Step();
            Check(engine.Snapshot().Ledger.Count == entries, "Loot repeated after completion");
        }),
        ("Retreat success and failure are real random outcomes without loot", () =>
        {
            foreach (double chance in new[] { 0.0, 1.0 })
            {
                var w = CombatScenario.Create(); w.CombatRules = w.CombatRules! with { RetreatChance = chance };
                var engine = Started(w); engine.Submit(Command("retreat", ActionKind.Retreat));
                Check(engine.Step().Single().Accepted, "Retreat command rejected");
                var final = engine.Snapshot();
                Check((final.Combats.Single().Status == CombatStatus.Retreated) == (chance == 1) && final.Loot.Count == 0, "Retreat probability or loot wrong");
            }
        }),
        ("Neutral attacks and competing combat ownership are rejected", () =>
        {
            var engine = new SimulationEngine(CombatScenario.Create());
            engine.Submit(Command("neutral", ActionKind.Engage, target: "npc:01"));
            Check(!engine.Step().Single().Accepted, "Neutral target attacked");
            engine.Submit(Command("first", ActionKind.Engage)); engine.Submit(Command("competing", ActionKind.Engage, "npc:00"));
            Check(engine.Step().Count(r => r.Accepted) == 1 && engine.Snapshot().Combats.Count == 1, "Target entered competing combats");
        }),
        ("Player and NPC use identical combat rounds skills and costs", () =>
        {
            var w = CombatScenario.Create(); var player = new SimulationEngine(w);
            w.ControlledCharacterId = "npc:00"; var npc = new SimulationEngine(w);
            var engage = Command("fight", ActionKind.Engage);
            player.Submit(engage); npc.Submit(engage with { Source = DecisionSource.Npc }); player.Step(); npc.Step();
            var skill = Command("skill", ActionKind.PowerStrike);
            player.Submit(skill); npc.Submit(skill with { Source = DecisionSource.Npc }); player.Step(); npc.Step();
            for (int n = 0; n < 20; n++) { player.Step(); npc.Step(); }
            Check(System.Text.Json.JsonSerializer.Serialize(player.Snapshot().Characters) == System.Text.Json.JsonSerializer.Serialize(npc.Snapshot().Characters) &&
                player.Snapshot().Ledger.SequenceEqual(npc.Snapshot().Ledger) && player.Snapshot().Combats.SequenceEqual(npc.Snapshot().Combats), "Combat differed by decision source");
        }),
        ("Mid-combat file save preserves prepared skills random state and continuation", () =>
        {
            var engine = Started(); engine.Submit(Command("skill", ActionKind.PowerStrike)); engine.Step();
            string dir = Path.Combine(Path.GetTempPath(), "cultivation-combat-" + Guid.NewGuid().ToString("N")); SimulationEngine restored;
            try { var path = Path.Combine(dir, "world.json"); var store = new JsonWorldStore(); store.Save(path, engine.Snapshot()); restored = new(store.Load(path)); }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            for (int n = 0; n < 25; n++) { engine.Step(); restored.Step(); }
            Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(restored.Snapshot()), "Combat continuation differed");
        }),
        ("NPC combat utility uses visibility risk retreat and healing preparation", () =>
        {
            var a = new CharacterState("actor", "Fighter") { BaseAttack = 30, BaseDefense = 5, GrowthWeight = 8, CombatDrive = 5, Caution = 0.1 };
            var target = new OpponentObservation("enemy", 40, 6, 1, "sight:valley", 0);
            var o = new LocalObservation(a, 0, 1000, 0, 0, 0, 1000, 10, 10, new(), "market:valley", 0, CombatRules: new(), Opponents: new[] { target });
            var planner = new UtilityPlanner();
            Check(planner.Evaluate(o).Any(c => c.Intent.Kind == ActionKind.Engage), "Capable fighter ignored opportunity");
            Check(!planner.Evaluate(o with { Actor = a with { Caution = 10 }, Opponents = new[] { target with { Attack = 100, Defense = 100 } } }).Any(c => c.Intent.Kind == ActionKind.Engage), "Ignored danger");
            Check(!planner.Evaluate(o with { Opponents = new[] { target with { Source = "global" } } }).Any(c => c.Intent.Kind == ActionKind.Engage), "Used global enemy information");
            var wounded = o with { Actor = a with { Health = 10 }, Battle = new("fight", target with { Attack = 40 }, false) };
            Check(planner.Evaluate(wounded).First().Intent.Kind == ActionKind.Retreat, "Wounded fighter did not retreat");
            Check(planner.Evaluate(o with { Actor = a with { Health = 20 } }).First().Intent.Kind == ActionKind.Buy, "Did not prepare healing supplies");
        }),
        ("Round limit draws and invalid saved combat states are rejected", () =>
        {
            var w = CombatScenario.Create(); w.CombatRules = w.CombatRules! with { MaximumRounds = 1 };
            w.Characters[0] = w.Characters[0] with { BaseDefense = 100 };
            int target = w.Characters.FindIndex(c => c.Id == "bandit:weak"); w.Characters[target] = w.Characters[target] with { BaseDefense = 100 };
            var engine = Started(w); engine.Step(); engine.Step();
            Check(engine.Snapshot().Combats.Single().Status == CombatStatus.Draw && engine.Snapshot().Loot.Count == 0, "Round cap not enforced");
            var broken = Started().Snapshot(); broken.Combats.Add(broken.Combats[0] with { Id = "duplicate-participants" }); Rejects(broken);
            w = CombatScenario.Create(); w.CombatRules = w.CombatRules! with { RetreatChance = 2 }; Rejects(w);
        }),
        ("Autonomous combat occurs and remains reproducible after save", () =>
        {
            var engine = new SimulationEngine(CombatScenario.Create());
            for (int n = 0; n < 20; n++) engine.Step(true);
            var restored = new SimulationEngine(WorldCodec.Clone(engine.Snapshot()));
            for (int n = 0; n < 30; n++) { engine.Step(true); restored.Step(true); }
            Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(restored.Snapshot()) && engine.Snapshot().Combats.Count > 0, "Autonomous combat missing or divergent");
        })
    };
}
