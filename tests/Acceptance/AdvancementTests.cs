using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class AdvancementTests
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static ActionIntent Action(string id, string actor, ActionKind kind, string item) => new(id, actor, actor, item, 1, 0, actor == "player" ? DecisionSource.Player : DecisionSource.Npc, kind);
    private static WorldState Ready()
    {
        var w = AdvancementScenario.Create();
        int index = w.Characters.FindIndex(c => c.Id == "npc:00");
        w.Characters[index] = w.Characters[index] with { MethodId = null, Cultivation = 50 };
        return w;
    }
    private static void Rejects(WorldState w)
    {
        bool rejected = false;
        try { _ = new SimulationEngine(w); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid advancement save accepted");
    }

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Learning reserves cost and completes once without free method", () =>
        {
            var w = Ready(); var rules = w.Advancement!;
            var engine = new SimulationEngine(w);
            var learn = Action("learn", "npc:00", ActionKind.LearnMethod, "spirit-stone");
            engine.Submit(learn); engine.Submit(learn);
            var result = engine.Step();
            Check(result.Count(r => r.Accepted) == 1 && engine.Snapshot().Characters.Single(c => c.Id == "npc:00").MethodId is null, "Learning immediate or duplicated");
            Check(Inventory.Balance(engine.Snapshot(), "npc:00", "spirit-stone") == Inventory.Balance(w, "npc:00", "spirit-stone") - rules.LearningCost, "Learning cost not reserved");
            engine.Submit(Action("busy", "npc:00", ActionKind.LearnMethod, "spirit-stone"));
            Check(!engine.Step().Single().Accepted, "Busy actor started another course");
            for (int n = 0; n < 3; n++) engine.Step();
            Check(engine.Snapshot().Characters.Single(c => c.Id == "npc:00").MethodId == rules.MethodId && Inventory.Balance(engine.Snapshot(), "consumed", "spirit-stone") == rules.LearningCost, "Learning did not consume exact cost");
            engine.Submit(Action("relearn", "npc:00", ActionKind.LearnMethod, "spirit-stone"));
            Check(!engine.Step().Single().Accepted, "Known method purchased twice");
        }),
        ("Breakthrough requires method cultivation pills and respects realm cap", () =>
        {
            var w = Ready(); var rules = w.Advancement!;
            var engine = new SimulationEngine(w);
            engine.Submit(Action("unqualified", "npc:00", ActionKind.Breakthrough, "pill"));
            Check(!engine.Step().Single().Accepted, "Broke through without method");
            int index = w.Characters.FindIndex(c => c.Id == "npc:00");
            w.Characters[index] = w.Characters[index] with { MethodId = rules.MethodId };
            var underqualified = WorldCodec.Clone(w);
            underqualified.Characters[index] = underqualified.Characters[index] with { Cultivation = 49 };
            var rejected = new SimulationEngine(underqualified);
            rejected.Submit(Action("low-growth", "npc:00", ActionKind.Breakthrough, "pill"));
            Check(!rejected.Step().Single().Accepted, "Insufficient cultivation accepted");
            var empty = WorldCodec.Clone(w); empty.Ledger.RemoveAll(e => e.To == "npc:00" && e.ResourceId == "pill");
            rejected = new(empty); rejected.Submit(Action("no-pills", "npc:00", ActionKind.Breakthrough, "pill"));
            Check(!rejected.Step().Single().Accepted, "Missing breakthrough resources accepted");
            engine = new(w);
            engine.Submit(Action("advance", "npc:00", ActionKind.Breakthrough, "pill"));
            Check(engine.Step().Single().Accepted, "Qualified breakthrough rejected");
            Check(engine.Snapshot().Characters[index].Realm == 0 && Inventory.Balance(engine.Snapshot(), "npc:00", "pill") == 1, "Breakthrough early or missing reservation");
            for (int n = 0; n < rules.BreakthroughTicks; n++) engine.Step();
            var advanced = engine.Snapshot();
            Check(advanced.Characters[index].Realm == 1 && advanced.Characters[index].Cultivation == 0, "Wrong breakthrough progression");
            Check(Inventory.Balance(advanced, "consumed", "pill") == rules.BreakthroughPills, "Breakthrough pills not consumed");
            w.Characters[index] = w.Characters[index] with { Realm = rules.MaximumRealm, Cultivation = 1000 };
            engine = new(w); engine.Submit(Action("capped", "npc:00", ActionKind.Breakthrough, "pill"));
            Check(!engine.Step().Single().Accepted, "Exceeded realm cap");
        }),
        ("Weapon effects require owned inventory and transfer requires unequip", () =>
        {
            var w = Ready(); var rules = w.Advancement!;
            var actor = w.Characters.Single(c => c.Id == "player");
            int before = CharacterProgression.Stats(actor, rules).Attack;
            var engine = new SimulationEngine(w);
            engine.Submit(Action("equip", "player", ActionKind.EquipWeapon, rules.WeaponId));
            Check(engine.Step().Single().Accepted, "Owned weapon rejected");
            var equipped = engine.Snapshot().Characters.Single(c => c.Id == "player");
            Check(CharacterProgression.Stats(equipped, rules).Attack == before + rules.WeaponAttack, "Equipment did not affect stats");
            string weaponInstance = equipped.EquippedWeaponId!;
            var transfer = new ActionIntent("transfer-equipped", "player", "sect", weaponInstance, 1, 0, DecisionSource.Player);
            engine.Submit(transfer); Check(!engine.Step().Single().Accepted, "Equipped weapon transferred");
            engine.Submit(Action("unequip", "player", ActionKind.UnequipWeapon, rules.WeaponId));
            engine.Submit(transfer with { Id = "transfer-free" });
            Check(engine.Step().All(r => r.Accepted) && Inventory.Balance(engine.Snapshot(), "sect", weaponInstance) == 1, "Unequip did not release weapon instance");
        }),
        ("Method practice levels increase subsequent cultivation gain", () =>
        {
            var w = Ready(); var rules = w.Advancement!;
            int index = w.Characters.FindIndex(c => c.Id == "player");
            w.Characters[index] = w.Characters[index] with { MethodId = rules.MethodId, MethodExperience = rules.MethodExperiencePerLevel - 1 };
            long initial = w.Characters[index].Cultivation;
            var engine = new SimulationEngine(w);
            engine.Submit(Action("practice1", "player", ActionKind.Cultivate, "pill")); engine.Step();
            for (int n = 0; n < w.Rules.CultivateTicks; n++) engine.Step();
            var practiced = engine.Snapshot().Characters[index];
            Check(CharacterProgression.MethodLevel(practiced, rules) == 2 && practiced.Cultivation - initial == w.Rules.GrowthPerPill + rules.GrowthBonusPerMethodLevel, "Method progression incorrect");
            engine.Submit(Action("practice2", "player", ActionKind.Cultivate, "pill")); engine.Step();
            for (int n = 0; n < w.Rules.CultivateTicks; n++) engine.Step();
            Check(engine.Snapshot().Characters[index].Cultivation - practiced.Cultivation == w.Rules.GrowthPerPill + rules.GrowthBonusPerMethodLevel * 2, "New method level did not affect cultivation");
        }),
        ("Player and NPC share advancement costs and results", () =>
        {
            var w = Ready(); var npc = new SimulationEngine(w);
            w.ControlledCharacterId = "npc:00"; var player = new SimulationEngine(w);
            var learn = Action("learn", "npc:00", ActionKind.LearnMethod, "spirit-stone");
            npc.Submit(learn); player.Submit(learn with { Source = DecisionSource.Player });
            for (int n = 0; n < 5; n++) { npc.Step(); player.Step(); }
            // Character records now contain source dictionaries; compare content across detached snapshots.
            Check(npc.Snapshot().Ledger.SequenceEqual(player.Snapshot().Ledger) &&
                System.Text.Json.JsonSerializer.Serialize(npc.Snapshot().Characters) == System.Text.Json.JsonSerializer.Serialize(player.Snapshot().Characters), "Advancement differs by input source");
        }),
        ("Scheduled advancement persists through actual file save and load", () =>
        {
            var engine = new SimulationEngine(Ready());
            engine.Submit(Action("learn", "npc:00", ActionKind.LearnMethod, "spirit-stone")); engine.Step();
            string directory = Path.Combine(Path.GetTempPath(), "cultivation-growth-" + Guid.NewGuid().ToString("N"));
            var store = new JsonWorldStore(); SimulationEngine restored;
            try { var path = Path.Combine(directory, "world.json"); store.Save(path, engine.Snapshot()); restored = new(store.Load(path)); }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            for (int n = 0; n < 6; n++) { engine.Step(); restored.Step(); }
            Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(restored.Snapshot()), "Scheduled learning continuation differs");
        }),
        ("Invalid affiliation equipment and advancement rules are rejected", () =>
        {
            var w = Ready(); w.Characters[1] = w.Characters[1] with { EquippedWeaponId = "imaginary" }; Rejects(w);
            w = Ready(); w.Characters[1] = w.Characters[1] with { OrganizationId = "missing" }; Rejects(w);
            w = Ready(); w.Advancement = w.Advancement! with { MethodExperiencePerLevel = 0 }; Rejects(w);
            w = Ready();
            Check(CharacterProgression.MethodLevel(w.Characters[0] with { MethodId = w.Advancement!.MethodId, MethodExperience = long.MaxValue }, w.Advancement!) == w.Advancement!.MaximumMethodLevel, "Method level overflowed cap");
        }),
        ("Autonomous growth learns equips and advances reproducibly", () =>
        {
            var engine = new SimulationEngine(AdvancementScenario.Create());
            for (int n = 0; n < 30; n++) engine.Step(true);
            var restored = new SimulationEngine(WorldCodec.Clone(engine.Snapshot()));
            for (int n = 0; n < 30; n++) { engine.Step(true); restored.Step(true); }
            var final = engine.Snapshot();
            Check(WorldCodec.Digest(final) == WorldCodec.Digest(restored.Snapshot()), "Autonomous growth continuation differs");
            Check(final.Characters.Any(c => c.Realm > 0) && final.Characters.Any(c => c.EquippedWeaponId is not null) && final.Memories.Any(m => m.Kind == ActionKind.LearnMethod), "Growth loop incomplete");
        })
    };
}
