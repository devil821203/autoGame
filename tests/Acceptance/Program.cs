using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;
using Cultivation.Headless;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
static ActionIntent Transfer(string id = "transfer", long quantity = 10, long duration = 0) =>
    new(id, "player", "sect", "spirit-stone", quantity, duration, DecisionSource.Player);
static void Rejects(Action action)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Expected invalid save rejection.");
}

static WorldState StaminaWorld(int value, bool unconscious = false)
{
    var w = GrowthScenario.Create();
    w.Characters = w.Characters.Select(c => c.Id == w.ControlledCharacterId ? c with {
        DemoLocationId = "sect", HexPosition = w.HexMap!.Tiles.Single(t => t.PlaceId == "sect").Coord, Stamina = new(value, unconscious, unconscious) } : c).ToList();
    return w;
}
static ActionIntent RestIntent(string id = "rest") => new(id, "player", "player", "stamina", 1, 0, DecisionSource.Player, ActionKind.Rest);
var tests = new (string Name, Action Body)[]
{
    ("Stage7 board model keeps world positions separate and round trips", () =>
    {
        var w = DemoWorld.Create();
        var before = w.Characters.Select(c => (c.Id, c.DemoLocationId, c.HexPosition, c.Health)).ToArray();
        w.BoardRules = new();
        w.BoardBattle = new() { Fighters = [new("player", BattleSide.Left, new(0, 0), 10, 1), new("npc", BattleSide.Right, new(0, 0), 10, 1)] };
        var engine = new SimulationEngine(w); var copy = engine.Snapshot();
        Check(copy.BoardBattle!.Fighters.Count == 2 && copy.BoardBattle.Round == 0, "Board state not preserved");
        Check(copy.Characters.Select(c => (c.Id, c.DemoLocationId, c.HexPosition, c.Health)).SequenceEqual(before), "Board placement changed world state");
        Check(WorldCodec.Digest(w) == WorldCodec.Digest(copy), "Board save round trip changed state");
        Check(DemoWorld.Create().BoardBattle is null, "Legacy mode enabled board");
    }),
    ("Stage7 board model rejects invalid teams cells references and turn state", () =>
    {
        WorldState Fresh()
        {
            var w = DemoWorld.Create(); w.BoardRules = new();
            w.BoardBattle = new() { Fighters = [new("player", BattleSide.Left, new(0, 0), 10, 1), new("npc", BattleSide.Right, new(0, 0), 10, 1)] };
            return w;
        }
        var w = Fresh(); w.BoardBattle!.Fighters[0] = w.BoardBattle.Fighters[0] with { Cell = new(5, 0) }; Rejects(() => new SimulationEngine(w));
        w = Fresh(); w.BoardBattle!.Fighters[0] = w.BoardBattle.Fighters[0] with { ActorId = "missing" }; Rejects(() => new SimulationEngine(w));
        w = Fresh();
        for (int n = 0; n < 5; n++) { string id = "extra:" + n; w.Characters.Add(new(id, id)); w.BoardBattle!.Fighters.Add(new(id, BattleSide.Left, new(n, 1), 10, 1)); }
        Rejects(() => new SimulationEngine(w));
        w = Fresh(); w.BoardBattle!.Orders.Add(new("player", new("wrong-battle", 1))); Rejects(() => new SimulationEngine(w));
        w = Fresh(); w.BoardRules = null; Rejects(() => new SimulationEngine(w));
    }),    ("Stage6 mindset combines modifiers before rounding and bounds large gains", () =>
    {
        var w = StaminaWorld(100); var a = w.Characters.Single(c => c.Id == "player");
        Check(a.Mindset == 50, "Initial mindset not neutral");
        Check(CharacterGrowthCalculator.Growth(w, a with { Mindset = 0 }, 10) == 5, "Low mindset");
        Check(CharacterGrowthCalculator.Growth(w, a with { Mindset = 100 }, 10) == 15, "High mindset");
        var tired = a with { Mindset = 100, Stamina = new(20) };
        Check(CharacterGrowthCalculator.Growth(w, tired, 10) == 7, "Modifiers not combined before rounding");
        var status = StaminaCalculator.Status(w, tired)!;
        Check(status.GrowthMultiplier == 0.75 && status.Fatigued && status.RecoveryPerTick == 30, "Status disagrees with execution");
        Check(CharacterGrowthCalculator.Growth(w, a with { Mindset = 100 }, long.MaxValue) == long.MaxValue, "Large gain overflow");
        var legacy = HexWorldScenario.Create(); Check(CharacterGrowthCalculator.Growth(legacy, legacy.Characters[0], 10) == 10, "Legacy gain changed");
    }),
    ("Stage6 mindset modifies real cultivation for player and NPC with exact save continuation", () =>
    {
        foreach (var source in new[] { DecisionSource.Player, DecisionSource.Npc })
        {
            var w = StaminaWorld(100); w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Mindset = 100 } : c).ToList();
            if (source == DecisionSource.Npc) w.ControlledCharacterId = "npc:00";
            var a = w.Characters.Single(c => c.Id == "player"); var e = new SimulationEngine(w);
            e.Submit(new("mind-cultivate", "player", "player", "pill", 1, 0, source, ActionKind.Cultivate));
            Check(e.Step().Single().Accepted, "Cultivation rejected");
            var resumed = new SimulationEngine(WorldCodec.Clone(e.Snapshot()));
            for (int n = 0; n < 5; n++) { e.Step(); resumed.Step(); }
            var end = e.Snapshot().Characters.Single(c => c.Id == "player");
            Check(end.Cultivation == a.Cultivation + CharacterGrowthCalculator.Growth(w, a, CharacterProgression.GrowthPerPill(a, w.Rules, w.Advancement)), "Mindset not applied to real cultivation");
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Mindset save continuation changed");
        }
    }),
    ("Stage6 mindset rejects invalid state and prevents modified cultivation overflow", () =>
    {
        var w = StaminaWorld(100); w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Mindset = 101 } : c).ToList();
        Rejects(() => new SimulationEngine(w));
        w = StaminaWorld(100); w.MindsetRules = w.MindsetRules! with { BaseMultiplier = double.NaN }; Rejects(() => WorldIntegrity.Validate(w));
        w = StaminaWorld(100); w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Mindset = 100, Cultivation = long.MaxValue - 10 } : c).ToList();
        Check(ActionValidator.Validate(w, new("overflow-growth", "player", "player", "pill", 1, 0, DecisionSource.Player, ActionKind.Cultivate)) is not null, "Modified gain overflow accepted");
        var old = HexWorldScenario.Create(); Check(WorldCodec.Clone(old).MindsetRules is null && old.Characters.All(c => c.Mindset is null), "Legacy activated mindset");
    }),
    ("Stage6 gathering mastery uses mindset while item output stays unchanged", () =>
    {
        var w = AutonomousWorld.Create(); w.MindsetRules = new(); w.StaminaRules = new();
        w.Characters = w.Characters.Select(c => c with { Mindset = 100, Stamina = new(100) }).ToList();
        w.Rules = w.Rules with { ExperiencePerAction = 4 };
        var actor = w.Characters.Single(c => c.Id == "player"); long herbs = Inventory.Balance(w, actor.Id, "herb");
        var e = new SimulationEngine(w);
        e.Submit(new("mind-gather", actor.Id, "grove", "herb", 1, 0, DecisionSource.Player, ActionKind.Gather));
        Check(e.Step().Single().Accepted, "Gather rejected");
        for (int n = 0; n < 5; n++) e.Step();
        var end = e.Snapshot();
        Check(end.Characters.Single(c => c.Id == actor.Id).GatheringExperience == actor.GatheringExperience + 6, "Mindset did not affect actual mastery");
        Check(Inventory.Balance(end, actor.Id, "herb") == herbs + 1, "Mindset changed item output");
    }),    ("Stage6 AI values its own modified growth without changing monetary rewards", () =>
    {
        var a = new CharacterState("test", "test") { AlchemyChance = 1 };
        var o = new LocalObservation(a, 0, 100, 2, 1, 100, 100, 100, 100, new(), "market:valley", 0);
        var planner = new UtilityPlanner();
        double low = planner.Evaluate(o with { GrowthMultiplier = 0.5 }).Single(c => c.Intent.Kind == ActionKind.Cultivate).Score;
        double high = planner.Evaluate(o with { GrowthMultiplier = 1.5 }).Single(c => c.Intent.Kind == ActionKind.Cultivate).Score;
        Check(Math.Abs(high - low * 3) < 0.00001, "AI ignored own growth multiplier");
    }),    ("Stage6 travel completes before exhaustion and rests starting next tick", () =>
    {
        var e = new SimulationEngine(StaminaWorld(10));
        e.Submit(new("exhaust-trip", "player", "town", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
        Check(e.Step().Single().Accepted, "Trip rejected");
        Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 10, "Spent before execution completed");
        for (int n = 0; n < 20 && e.Snapshot().Scheduled.Any(t => t.Intent.ActorId == "player"); n++) e.Step();
        var end = e.Snapshot(); var actor = end.Characters.Single(c => c.Id == "player");
        Check(actor.DemoLocationId == "town" && actor.Stamina is { Current: 0, Unconscious: true }, "Must arrive before exhaustion, with no same-tick recovery");
        Check(end.Events.Count(x => x.ActionId == "exhaust-trip" && x.Kind == "StaminaSpent") == 1, "Trip charged more than once");
        e.Submit(new("blocked-trip", "player", "sect", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
        Check(!e.Step().Single().Accepted, "Unconscious actor acted");
        Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 10, "Next tick did not force rest");
    }),    ("Stage6 cultivation applies growth and consumes materials before exhaustion", () =>
    {
        var w = StaminaWorld(10); var original = w.Characters.Single(c => c.Id == "player");
        long pills = Inventory.Balance(w, "player", "pill"); var e = new SimulationEngine(w);
        e.Submit(new("tired-cultivate", "player", "player", "pill", 1, 0, DecisionSource.Player, ActionKind.Cultivate));
        Check(e.Step().Single().Accepted, "Cultivation rejected");
        Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 10, "Scheduled action spent early");
        e = new SimulationEngine(e.Snapshot());
        for (int n = 0; n < 20 && e.Snapshot().Scheduled.Any(t => t.Intent.ActorId == "player"); n++) e.Step();
        var end = e.Snapshot(); var actor = end.Characters.Single(c => c.Id == "player");
        Check(actor.Cultivation == original.Cultivation + StaminaCalculator.Growth(w, original, CharacterProgression.GrowthPerPill(original, w.Rules, w.Advancement)), "Growth did not finish before debit");
        Check(Inventory.Balance(end, "player", "pill") == pills - 1 && actor.Stamina!.Current == 0, "Material or stamina settlement incorrect");
        e.Step(); Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 10, "No next tick forced rest");
    }),    ("Stage6 autonomous actors rest and preserve resumable state", () =>
    {
        var w = GrowthScenario.Create();
        w.Characters = w.Characters.Select(c => c.Id == "npc:00" ? c with { Stamina = new(5) } : c).ToList();
        var e = new SimulationEngine(w); e.Step(true);
        Check(e.Snapshot().Characters.Single(c => c.Id == "npc:00").Stamina!.Resting, "NPC did not choose rest");
        var clone = new SimulationEngine(WorldCodec.Clone(e.Snapshot()));
        for (int n = 0; n < 40; n++) { e.Step(true); clone.Step(true); }
        Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(clone.Snapshot()), "Resume differs");
        Check(e.Snapshot().Events.Any(x => x.Kind == "StaminaSpent"), "AI made no productive actions");
    }),    ("Stage6 rest is tick based and unconscious wakes strictly above 30", () =>
    {
        var e = new SimulationEngine(StaminaWorld(0, true));
        for (int i = 1; i <= 3; i++) { e.Step(); var a = e.Snapshot().Characters.Single(c => c.Id == "player"); Check(a.Stamina!.Current == i * 10 && a.Stamina.Unconscious, "Premature wake or duplicate recovery"); }
        e = new SimulationEngine(WorldCodec.Clone(e.Snapshot())); e.Step();
        var final = e.Snapshot().Characters.Single(c => c.Id == "player");
        Check(final.Stamina!.Current == 40 && !final.Stamina.Unconscious && final.Stamina.Resting, "Recovery did not wake exactly above 30 while continuing toward full resources");
        Check(final.Health == 100, "Rest modified health");
    }),
    ("Stage6 environments fatigue and rejected actions share one calculator", () =>
    {
        var w = StaminaWorld(20); var a = w.Characters.Single(c => c.Id == "player"); var r = w.StaminaRules!;
        Check(StaminaCalculator.Recovery(a, r) == 30, "Ordinary recovery");
        Check(StaminaCalculator.Recovery(a with { DemoLocationId = "dungeon" }, r) == 15, "Outdoor recovery");
        Check(StaminaCalculator.Recovery(a with { Stamina = a.Stamina! with { HomeLocationId = "sect", HasBed = true } }, r) == 45, "Bed recovery");
        Check(StaminaCalculator.Growth(w, a, 10) == 5 && StaminaCalculator.Growth(w, a with { Stamina = new(30) }, 10) == 10, "Fatigue boundary");
        var e = new SimulationEngine(w); e.Submit(RestIntent()); Check(e.Step().Single().Accepted, "Rest rejected");
        Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 20, "Rest recovered on start tick");
        e.Step(); Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 50, "Normal rest wrong");
        e.Submit(new("bad", "player", "missing", "herb", 1, 0, DecisionSource.Player, ActionKind.Gather));
        Check(!e.Step().Single().Accepted, "Busy actor acted");
        Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 80, "Rejected action spent stamina");
    }),
    ("Stage6 costs are once per accepted action and legacy remains disabled", () =>
    {
        var w = StaminaWorld(100); var e = new SimulationEngine(w);
        e.Submit(new("travel-stamina", "player", "town", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
        var result = e.Step().Single(); Check(result.Accepted, result.Reason);
        Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 100, "Travel cost charged before completion");
        e.Submit(new("travel-stamina", "player", "town", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel)); e.Step();
        Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Current == 100, "Duplicate spent before completion");
        Check(NewGameFactory.CreateLike(e.Snapshot(), "新行者").StaminaRules is not null, "New game lost mode");
        var old = HexWorldScenario.Create(); Check(WorldCodec.Clone(old).StaminaRules is null, "Legacy enabled stamina");
        Check(ActionValidator.Validate(old, RestIntent()) is not null, "Legacy permits rest");
        var bad = StaminaWorld(101); Rejects(() => new SimulationEngine(bad));
    }),    ("Persistent ingredient plan buys, brews and sells through shared rules", () =>
    {
        var w = AutonomousWorld.Create();
        w.Characters = w.Characters.Where(c => c.Id is "player" or "npc:01").Select(c => c.Id == "npc:01" ? c with { AlchemyChance = 1, GrowthWeight = 0 } : c).ToList();
        w.Ledger.RemoveAll(e => e.To.StartsWith("npc:", StringComparison.Ordinal) && e.To != "npc:01" || e.To == "npc:01" && e.ResourceId == "herb" || e.To == "grove" || e.To == "market" && e.ResourceId == "pill");
        var engine = new SimulationEngine(w);
        engine.Step(true);
        Check(engine.Snapshot().Plans.Single().Steps.SequenceEqual(new[] { ActionKind.Buy, ActionKind.Brew, ActionKind.Sell }), "Ingredient plan missing steps");
        for (int n = 0; n < 8; n++) engine.Step(true);
        var final = engine.Snapshot();
        Check(final.Events.Any(e => e.Kind == "BrewSucceeded"), "Plan never brewed");
        Check(final.Memories.Any(m => m.Kind == ActionKind.Sell && m.ResourceId == "pill" && m.StoneDelta == w.Rules.PillPrice), "Plan never sold output");
    }),
    ("Remembered costs alter production choices and expire", () =>
    {
        var actor = new CharacterState("test", "Alchemist") { AlchemyChance = 0.9 };
        var o = new LocalObservation(actor, 25, 100, 2, 0, 100, 100, 10, 10, new(), "market:valley", 25);
        var planner = new UtilityPlanner();
        Check(planner.Evaluate(o).Any(c => c.Intent.Kind == ActionKind.Brew), "Baseline brew infeasible");
        var costly = new ActionMemory(actor.Id, 25, ActionKind.Buy, true, "herb", -100, 1, "market:valley");
        Check(!planner.Evaluate(o with { Memories = new[] { costly } }).Any(c => c.Intent.Kind == ActionKind.Brew), "Actual purchase cost ignored");
        Check(planner.Evaluate(o with { Memories = new[] { costly with { Tick = 0 } } }).Any(c => c.Intent.Kind == ActionKind.Brew), "Expired cost still influenced decisions");
        Check(planner.Evaluate(o with { Memories = new[] { costly with { ActorId = "other" } } }).Any(c => c.Intent.Kind == ActionKind.Brew), "Used another actor's private memory");
        Check(CharacterDevelopment.SuccessChance(actor with { AlchemyExperience = long.MaxValue }, o.Rules) <= 1, "Skill probability exceeded one");
    }),
    ("Health, reputation needs and gathering skill have real effects", () =>
    {
        var w = AutonomousWorld.Create();
        var actor = w.Characters.Single(c => c.Id == "npc:00");
        var needs = CharacterDevelopment.Needs(actor with { Health = 20 }, 0, 0, 0, w.Rules);
        Check(needs.Healing > 0 && needs.Safety > 0 && needs.Reputation > 0 && needs.NeedsPill, "Needs missing");
        Check(!CharacterDevelopment.Services(actor).Contains("煉製丹藥"), "Non-alchemist offered alchemy");
        Check(CharacterDevelopment.Services(actor with { AlchemyChance = 0.5 }).Contains("煉製丹藥"), "Alchemy service missing");
        int index = w.Characters.FindIndex(c => c.Id == actor.Id);
        w.Characters[index] = actor with { GatheringExperience = w.Rules.GatheringExperiencePerSpeedLevel };
        var engine = new SimulationEngine(w);
        engine.Submit(new("skilled-gather", actor.Id, "grove", "herb", 1, 0, DecisionSource.Npc, ActionKind.Gather));
        engine.Step();
        Check(engine.Snapshot().Scheduled.Single().DueTick == 2, "Gathering skill did not shorten duration");
        engine.Step();
        Check(Inventory.Balance(engine.Snapshot(), actor.Id, "herb") == 1, "Skilled gathering lost output");
    }),
    ("30 and 50 NPC worlds complete autonomous production and growth", () =>
    {
        foreach (int count in new[] { 30, 50 })
        {
            var engine = new SimulationEngine(AutonomousWorld.Create(12345, count));
            for (int n = 0; n < 60; n++) engine.Step(true);
            var final = engine.Snapshot();
            Check(final.Characters.Count == count + 1 && final.Events.Any(e => e.Kind == "BrewSucceeded") && final.Events.Any(e => e.Kind == "Trade") && final.Characters.Sum(c => c.Cultivation) > 0, "Population loop failed");
            WorldIntegrity.Validate(final);
        }
    }),
    ("Persistent healing plan resumes and records actual cost and consumption", () =>
    {
        var w = AutonomousWorld.Create();
        w.Characters = w.Characters.Where(c => c.Id is "player" or "npc:00").ToList();
        w.Ledger.RemoveAll(e => e.To.StartsWith("npc:", StringComparison.Ordinal) && e.To != "npc:00");
        var engine = new SimulationEngine(w);
        engine.Step(true);
        var saved = engine.Snapshot();
        Check(saved.Plans.Single().Steps.SequenceEqual(new[] { ActionKind.Heal }), "Purchase did not retain healing goal");
        Check(saved.Memories.Single().StoneDelta == -w.Rules.PillPrice, "Purchase cost not remembered");
        var restored = new SimulationEngine(WorldCodec.Clone(saved));
        for (int n = 0; n < 5; n++) { engine.Step(true); restored.Step(true); }
        Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(restored.Snapshot()), "Plan continuation differs");
        Check(engine.Snapshot().Characters.Single(c => c.Id == "npc:00").Health > 40, "Healing goal never completed");
        Check(engine.Snapshot().Memories.Any(m => m.Kind == ActionKind.Heal && m.ItemDelta == -1), "Consumption not remembered");
    }),
    ("Decision budget is bounded and rotates fairly", () =>
    {
        var w = AutonomousWorld.Create();
        w.Rules = w.Rules with { DecisionBudget = 3 };
        var engine = new SimulationEngine(w);
        for (int n = 0; n < 20; n++) engine.Step(true);
        var final = engine.Snapshot();
        Check(final.Events.Where(e => e.Kind == "DecisionBudget").All(e => int.Parse(e.Detail) <= 3), "Budget exceeded");
        Check(final.Events.Where(e => e.Kind == "Decision").Select(e => e.ActionId.Split(':')[2]).Distinct().Count() == 40, "NPC starved of decisions");
    }),
    ("Expired plans invalidate and old memories expire", () =>
    {
        var w = AutonomousWorld.Create();
        w.Plans.Add(new("npc:00", "Expired", [ActionKind.Brew], 0));
        w.Rules = w.Rules with { MemoryTicks = 2 };
        w.Memories.Add(new("npc:00", 0, ActionKind.Buy, true, "pill", -15, 1, "market:valley"));
        var engine = new SimulationEngine(w);
        for (int n = 0; n < 5; n++) engine.Step(true);
        var final = engine.Snapshot();
        Check(final.Events.Any(e => e.Kind == "PlanInvalidated" && e.ActionId == "npc:00"), "Expired plan retained");
        Check(final.Memories.All(m => m.Tick >= final.Clock.Tick - w.Rules.MemoryTicks - 1), "Old memories retained");
    }),
    ("Player and NPC share healing and production rules", () =>
    {
        var w = AutonomousWorld.Create();
        var actor = w.Characters.FindIndex(c => c.Id == "npc:00");
        w.Ledger.Add(new(w.NextSequence++, 0, "initial-pill", "$genesis", "npc:00", "pill", 1, "Genesis"));
        var npc = new SimulationEngine(w);
        var playerWorld = WorldCodec.Clone(w); playerWorld.ControlledCharacterId = "npc:00";
        var player = new SimulationEngine(playerWorld);
        var heal = new ActionIntent("heal", "npc:00", "npc:00", "pill", 1, 0, DecisionSource.Npc, ActionKind.Heal);
        npc.Submit(heal); player.Submit(heal with { Source = DecisionSource.Player });
        for (int n = 0; n < 4; n++) { npc.Step(); player.Step(); }
        Check(npc.Snapshot().Characters[actor].Health == player.Snapshot().Characters[actor].Health, "Healing differs by source");
        Check(npc.Snapshot().Ledger.SequenceEqual(player.Snapshot().Ledger), "Healing cost differs by source");
    }),
    ("Needs and aspirations change goals without changing inventory", () =>
    {
        var actor = new CharacterState("test", "Test") { DesiredStones = 100, DesiredCultivation = 100 };
        var rules = new EconomyRules();
        Check(CharacterDevelopment.Needs(actor, 0, 0, 0, rules).Wealth > CharacterDevelopment.Needs(actor, 100, 0, 0, rules).Wealth, "Wealth need did not respond");
        var o = new LocalObservation(actor with { WealthWeight = 0.3, GrowthWeight = 1 }, 0, 100, 0, 1, 0, 100, 0, 0, rules, "market:valley", 0);
        var planner = new UtilityPlanner();
        Check(planner.Evaluate(o with { Actor = o.Actor with { Aspiration = Aspiration.Cultivation } }).First().Intent.Kind == ActionKind.Cultivate, "Cultivation aspiration ignored");
        Check(planner.Evaluate(o with { Actor = o.Actor with { Aspiration = Aspiration.Wealth } }).First().Intent.Kind == ActionKind.Sell, "Wealth aspiration ignored");
        Check(!planner.Evaluate(o with { Actor = o.Actor with { Cultivation = 100 } }).Any(c => c.Intent.Kind == ActionKind.Cultivate), "Satisfied growth need remained active");
    }),
    ("Skills grow only on completion and survive scheduled save continuation", () =>
    {
        var w = AutonomousWorld.Create();
        var engine = new SimulationEngine(w);
        engine.Submit(new("training", "npc:01", "npc:01", "herb", 1, 0, DecisionSource.Npc, ActionKind.Brew));
        engine.Step();
        Check(engine.Snapshot().Characters.Single(c => c.Id == "npc:01").AlchemyExperience == 0, "Experience awarded before completion");
        var restored = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(engine.Snapshot())));
        for (int n = 0; n < 3; n++) { engine.Step(); restored.Step(); }
        Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(restored.Snapshot()), "Skill continuation differs");
        var actor = engine.Snapshot().Characters.Single(c => c.Id == "npc:01");
        Check(actor.AlchemyExperience == w.Rules.ExperiencePerAction, "Completion did not award experience");
        Check(CharacterDevelopment.SuccessChance(actor, w.Rules) > CharacterDevelopment.SuccessChance(actor with { AlchemyExperience = 0 }, w.Rules), "Skill did not improve success chance");
        Check(CharacterDevelopment.SuccessChance(actor with { AlchemyTalent = 2 }, w.Rules) > CharacterDevelopment.SuccessChance(actor with { AlchemyTalent = 1 }, w.Rules), "Talent ignored");
        engine.Submit(new("gather-training", "npc:00", "grove", "herb", 1, 0, DecisionSource.Npc, ActionKind.Gather));
        engine.Step(); engine.Step(); engine.Step();
        Check(engine.Snapshot().Characters.Single(c => c.Id == "npc:00").GatheringExperience == w.Rules.ExperiencePerAction, "Gathering experience missing");
    }),
    ("Invalid development data rejected and legacy defaults readable", () =>
    {
        var legacy = WorldCodec.Deserialize("{\"Characters\":[{\"Id\":\"player\",\"Name\":\"Player\"}]}");
        WorldIntegrity.Validate(legacy);
        var w = AutonomousWorld.Create();
        w.Characters[0] = w.Characters[0] with { AlchemyExperience = -1 };
        Rejects(() => new SimulationEngine(w));
        w = AutonomousWorld.Create();
        w.Rules = w.Rules with { SkillChancePerExperience = -1 };
        Rejects(() => new SimulationEngine(w));
    }),
    ("Production rejects missing materials and consumes failed batches", () =>
    {
        var w = AutonomousWorld.Create();
        w.Characters[1] = w.Characters[1] with { AlchemyChance = double.Epsilon };
        var engine = new SimulationEngine(w);
        ActionIntent Brew(string id) => new(id, "npc:00", "npc:00", "herb", 1, 0, DecisionSource.Npc, ActionKind.Brew);
        engine.Submit(Brew("empty"));
        Check(!engine.Step().Single().Accepted, "Brewed without materials");
        engine.Submit(new("give", "npc:01", "npc:00", "herb", 2, 0, DecisionSource.Npc));
        engine.Step();
        engine.Submit(Brew("failed-batch"));
        Check(engine.Step().Single().Accepted, "Valid batch rejected");
        engine.Submit(Brew("busy"));
        Check(!engine.Step().Single().Accepted, "Busy actor started another batch");
        for (int n = 0; n < 3; n++) engine.Step();
        var final = engine.Snapshot();
        Check(Inventory.Balance(final, "npc:00", "herb") == 0 && Inventory.Balance(final, "consumed", "herb") == 2, "Failure did not consume ingredients");
        Check(final.Events.Any(e => e.ActionId == "failed-batch" && e.Kind == "BrewFailed"), "Failure missing");
        Check(final.Characters.Single(c => c.Id == "npc:00").AlchemyExperience == final.Rules.ExperiencePerAction, "Failed batch did not award learning experience");
    }),
    ("Finite shop trades atomically and deduplicates payment", () =>
    {
        var engine = new SimulationEngine(AutonomousWorld.Create());
        var before = engine.Snapshot();
        var buy = new ActionIntent("buy", "npc:00", "market", "pill", 1, 0, DecisionSource.Npc, ActionKind.Buy);
        engine.Submit(buy); engine.Submit(buy);
        Check(engine.Step().Count(r => r.Accepted) == 1, "Duplicate trade accepted");
        var after = engine.Snapshot();
        Check(Inventory.Balance(after, "npc:00", "pill") == 1, "Goods missing");
        Check(Inventory.Balance(after, "npc:00", "spirit-stone") == Inventory.Balance(before, "npc:00", "spirit-stone") - before.Rules.PillPrice, "Wrong payment");
        for (int n = 0; n < 5; n++) engine.Submit(buy with { Id = "poor:" + n });
        Check(engine.Step().Count(r => r.Accepted) == 1, "Overspent shop purchase");
    }),
    ("Utility choices depend on traits and local information", () =>
    {
        var planner = new UtilityPlanner();
        var o = new LocalObservation(new("actor", "Actor") { WealthWeight = 2, GrowthWeight = 0.1 }, 0, 100, 0, 1, 100, 100, 100, 10, new(), "market:valley", 0);
        Check(planner.Evaluate(o).First().Intent.Kind == ActionKind.Sell, "Wealth preference ignored");
        Check(planner.Evaluate(o with { Actor = o.Actor with { WealthWeight = 0.1, GrowthWeight = 10 } }).First().Intent.Kind == ActionKind.Cultivate, "Growth preference ignored");
        Check(planner.Evaluate(o with { Source = "global" }).Count == 0, "Used unknown information");
        Check(planner.Evaluate(o with { ObservedTick = -1 }).Count == 0, "Used expired quote");
    }),
    ("40 NPC autonomous loop conserves currency and resumes deterministically", () =>
    {
        var initial = AutonomousWorld.Create();
        var engine = new SimulationEngine(initial);
        for (int n = 0; n < 40; n++) AutonomousRunner.Step(engine);
        var checkpoint = engine.Snapshot();
        var restored = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(checkpoint)));
        for (int n = 0; n < 40; n++) { AutonomousRunner.Step(engine); AutonomousRunner.Step(restored); }
        var final = engine.Snapshot();
        Check(WorldCodec.Digest(final) == WorldCodec.Digest(restored.Snapshot()), "Autonomous save continuation differs");
        var replay = new SimulationEngine(AutonomousWorld.Create());
        for (int n = 0; n < 80; n++) AutonomousRunner.Step(replay);
        Check(WorldCodec.Digest(final) == WorldCodec.Digest(replay.Snapshot()), "Seed replay differs");
        Check(final.Events.Any(e => e.Kind == "BrewSucceeded") && final.Events.Any(e => e.Kind == "BrewFailed") && final.Events.Any(e => e.Kind == "Trade") && final.Characters.Sum(c => c.Cultivation) > 0, "Autonomous loop incomplete");
        long Money(WorldState w) => w.Characters.Select(c => c.Id).Concat(w.Organizations.Select(c => c.Id)).Sum(id => Inventory.Balance(w, id, "spirit-stone"));
        Check(Money(initial) == Money(final), "Currency created or lost");
        WorldIntegrity.Validate(final);
    }),
    ("Player and NPC share business rules", () =>
    {
        var playerWorld = DemoWorld.Create();
        var npcWorld = WorldCodec.Clone(playerWorld);
        npcWorld.ControlledCharacterId = "npc";
        var player = new SimulationEngine(playerWorld);
        var npc = new SimulationEngine(npcWorld);
        player.Submit(Transfer());
        npc.Submit(Transfer() with { Source = DecisionSource.Npc });
        Check(player.Step().Single().Accepted && npc.Step().Single().Accepted, "Same actor and resources must succeed through both adapters");
        Check(player.Snapshot().Ledger.SequenceEqual(npc.Snapshot().Ledger), "Ledger differs by decision source");
        player.Submit(Transfer("poor", long.MaxValue));
        npc.Submit(Transfer("poor", long.MaxValue) with { Source = DecisionSource.Npc });
        Check(player.Step().Single().Reason == npc.Step().Single().Reason, "Resource validation differs");
    }),
    ("Control authority", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        engine.Submit(Transfer() with { ActorId = "npc" });
        Check(!engine.Step().Single().Accepted, "Player controlled NPC");
        engine.Submit(Transfer("spoof") with { Source = DecisionSource.Npc });
        Check(!engine.Step().Single().Accepted, "AI controlled player");
    }),
    ("No overspend or duplicate transfer", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        var initial = engine.Snapshot();
        long balance = Inventory.Balance(initial, "player", "spirit-stone");
        var action = Transfer("first", balance);
        engine.Submit(action);
        engine.Submit(action);
        engine.Submit(Transfer("second", 1));
        var results = engine.Step();
        Check(results.Count(r => r.Accepted) == 1, "More than one debit accepted");
        var final = engine.Snapshot();
        Check(Inventory.Balance(final, "player", "spirit-stone") == 0, "Wrong balance");
        Check(final.Ledger.Count == initial.Ledger.Count + 1, "Duplicate ledger entry");
    }),
    ("Delayed transfer reserves resources and completes once", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        var before = engine.Snapshot();
        long amount = Inventory.Balance(before, "player", "spirit-stone");
        engine.Submit(Transfer("delay", amount, 2));
        engine.Step();
        Check(Inventory.Balance(engine.Snapshot(), "player", "spirit-stone") == 0, "Not reserved");
        Check(Inventory.Balance(engine.Snapshot(), "sect", "spirit-stone") == Inventory.Balance(before, "sect", "spirit-stone"), "Completed early");
        engine.Submit(Transfer("overspend", 1));
        Check(!engine.Step().Single().Accepted, "Reserved funds reused");
        engine.Step();
        Check(engine.Snapshot().Scheduled.Count == 0, "Schedule not completed");
        Check(Inventory.Balance(engine.Snapshot(), "sect", "spirit-stone") == Inventory.Balance(before, "sect", "spirit-stone") + amount, "Missing delivery");
        int entries = engine.Snapshot().Ledger.Count;
        engine.Step();
        Check(engine.Snapshot().Ledger.Count == entries, "Completed twice");
    }),
    ("Concurrent submissions are serialized without overspend", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        long balance = Inventory.Balance(engine.Snapshot(), "player", "spirit-stone");
        Parallel.For(0, 20, i => engine.Submit(Transfer("parallel:" + i, balance)));
        Check(engine.Step().Count(r => r.Accepted) == 1, "Competing debits accepted");
    }),
    ("Invalid quantity and recipient cannot mutate ledger", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        engine.Submit(Transfer("zero", 0));
        engine.Submit(Transfer("negative", -1));
        engine.Submit(Transfer("target") with { TargetId = "missing" });
        Check(engine.Step().All(r => !r.Accepted), "Invalid action accepted");
        Check(engine.Snapshot().Ledger.Count == 3, "Ledger changed");
        var capacityWorld = DemoWorld.Create();
        int sectEntry = capacityWorld.Ledger.FindIndex(e => e.To == "sect");
        capacityWorld.Ledger[sectEntry] = capacityWorld.Ledger[sectEntry] with { Quantity = long.MaxValue - 5 };
        var capacity = new SimulationEngine(capacityWorld);
        capacity.Submit(Transfer("reserved-capacity", 4, 2));
        capacity.Submit(Transfer("overflow", 2));
        var capacityResults = capacity.Step();
        Check(capacityResults[0].Accepted && !capacityResults[1].Accepted, "Incoming reservation was not counted");
        capacity.Step(); capacity.Step();
        Check(Inventory.Balance(capacity.Snapshot(), "sect", "spirit-stone") == long.MaxValue - 1, "Reserved delivery overflowed");
    }),
    ("Detached snapshots cannot modify simulation", () =>
    {
        var initial = DemoWorld.Create();
        var engine = new SimulationEngine(initial);
        var digest = WorldCodec.Digest(engine.Snapshot());
        initial.Ledger.Clear();
        var snapshot = engine.Snapshot();
        snapshot.Ledger.Clear();
        Check(digest == WorldCodec.Digest(engine.Snapshot()), "External mutation reached engine");
    }),
    ("Frame cadence does not affect fixed ticks", () =>
    {
        var first = new SimulationEngine(DemoWorld.Create());
        var second = new SimulationEngine(DemoWorld.Create());
        first.Submit(Transfer(duration: 2)); second.Submit(Transfer(duration: 2));
        var a = new TickPump(first, TimeSpan.FromMilliseconds(250));
        var b = new TickPump(second, TimeSpan.FromMilliseconds(250));
        for (int i = 0; i < 100; i++) a.Advance(TimeSpan.FromMilliseconds(10));
        for (int i = 0; i < 4; i++) b.Advance(TimeSpan.FromMilliseconds(250));
        Check(WorldCodec.Digest(first.Snapshot()) == WorldCodec.Digest(second.Snapshot()), "Cadence changed world");
    }),
    ("Same seed and ordered inputs reproduce world", () =>
    {
        string Run()
        {
            var engine = new SimulationEngine(DemoWorld.Create(42));
            engine.Submit(Transfer(duration: 3));
            for (int i = 0; i < 8; i++) engine.Step();
            return WorldCodec.Digest(engine.Snapshot());
        }
        Check(Run() == Run(), "Replay differs");
        var random = new SeededRandom(42);
        random.NextUInt32();
        var restored = new SeededRandom(random.State);
        Check(random.NextUInt32() == restored.NextUInt32(), "Random continuation differs");
    }),
    ("Save/load preserves pending, schedule, dedup and continuation", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        engine.Submit(Transfer("scheduled", 10, 3));
        engine.Step();
        engine.Submit(Transfer("pending", 5));
        var directory = Path.Combine(Path.GetTempPath(), "cultivation-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "world.json");
        try
        {
            var store = new JsonWorldStore();
            store.Save(path, engine.Snapshot());
            bool protectedSave = false;
            try { store.Save(path, DemoWorld.Create(99)); }
            catch (IOException) { protectedSave = true; }
            Check(protectedSave, "Existing save overwritten");
            var restored = new SimulationEngine(store.Load(path));
            engine.Submit(Transfer("scheduled", 10, 3));
            restored.Submit(Transfer("scheduled", 10, 3));
            for (int i = 0; i < 5; i++) { engine.Step(); restored.Step(); }
            Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(restored.Snapshot()), "Continuation differs");
        }
        finally
        {
            // Only the unique directory created by this test is removed.
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }),
    ("Corrupt worlds are rejected", () =>
    {
        var world = DemoWorld.Create();
        world.SchemaVersion = 99;
        Rejects(() => new SimulationEngine(world));
        world = DemoWorld.Create();
        world.Ledger.Add(new(world.NextSequence++, 0, "bad", "player", "sect", "spirit-stone", 999999, "Transfer"));
        Rejects(() => new SimulationEngine(world));
        world = DemoWorld.Create();
        world.Ledger.Add(new(world.NextSequence++, 0, "orphan", "player", "$escrow:orphan", "spirit-stone", 1, "Reserved"));
        Rejects(() => new SimulationEngine(world));
    }),
    ("Probe only returns intents", () =>
    {
        var probe = new TransferProbe("sect");
        Check(probe.Decide(new("npc", 0, 100)) is not null, "No probe intent");
        Check(probe.Decide(new("npc", 0, 0)) is null, "Probe ignored known funds");
        Check(probe.Decide(new("npc", 1, 100)) is null, "Probe repeated");
    })
};

int failed = 0;
var allTests = tests.Concat(Phase2Tests.Cases).Concat(AdvancementTests.Cases).Concat(CombatTests.Cases).Concat(SocialTests.Cases).Concat(GovernanceTests.Cases).Concat(PersistenceMigrationTests.Cases).Concat(SectEconomyTests.Cases).Concat(SectFacilityTests.Cases).Concat(ConfigurationTests.Cases).Concat(NewGameTests.Cases).Concat(ExpeditionTests.Cases).Concat(PerformanceTests.Cases).ToArray();
allTests = allTests.Concat(HexWorldTests.Cases).ToArray();
allTests = allTests.Concat(SceneSiteTests.Cases).ToArray();
allTests = allTests.Concat(BoardBattleTests.Cases).Concat(RestHealthTests.Cases).Concat(PlayerAutoScheduleTests.Cases).Concat(TimeTests.Cases).Concat(LifeSkillTests.Cases).Concat(LifeSystemTests.Cases).ToArray();
if (args.Contains("--time-only")) allTests = TimeTests.Cases.ToArray();
if (args.Contains("--life-only")) allTests = LifeSkillTests.Cases.Concat(LifeSystemTests.Cases).ToArray();
allTests = allTests.Concat(HerbProcurementTests.Cases).Concat(MedicineOnlyTests.Cases).ToArray();
if (args.Contains("--procurement-only")) allTests = HerbProcurementTests.Cases.Concat(MedicineOnlyTests.Cases).ToArray();
allTests = allTests.Concat(MortalCultivationTests.Cases).Concat(MortalTrainingActionTests.Cases).ToArray();
if (args.Contains("--mortal-only")) allTests = MortalCultivationTests.Cases.Concat(MortalTrainingActionTests.Cases).ToArray();
allTests = allTests.Concat(SpecialistServiceTests.Cases).ToArray();
if (args.Contains("--specialists-only")) allTests = SpecialistServiceTests.Cases.ToArray();
allTests = allTests.Concat(BatchMeasurementTests.Cases).ToArray();
if (args.Contains("--batch-only")) allTests = BatchMeasurementTests.Cases.ToArray();
allTests = allTests.Concat(SettlementTests.Cases).Concat(SettlementEconomyTests.Cases).Concat(SettlementConstructionTests.Cases).Concat(OrganizationSiteTests.Cases).Concat(SettlementOwnershipTests.Cases).ToArray();
if (args.Contains("--settlements-only")) allTests = SettlementTests.Cases.Concat(SettlementEconomyTests.Cases).Concat(SettlementConstructionTests.Cases).Concat(OrganizationSiteTests.Cases).Concat(SettlementOwnershipTests.Cases).ToArray();
allTests = allTests.Concat(CurrencyTests.Cases).ToArray();
if (args.Contains("--currency-only")) allTests = CurrencyTests.Cases.ToArray();
allTests = allTests.Concat(DungeonLootTests.Cases).ToArray();
if (args.Contains("--dungeon-loot-only")) allTests = DungeonLootTests.Cases.ToArray();
allTests = allTests.Concat(SettlementDailyTests.Cases).ToArray();
if (args.Contains("--settlement-daily-only")) allTests = SettlementDailyTests.Cases.ToArray();
allTests = allTests.Concat(SettlementPopulationTests.Cases).ToArray();
if (args.Contains("--settlement-population-only")) allTests = SettlementPopulationTests.Cases.ToArray();
allTests = allTests.Concat(CountryRemovalTests.Cases).ToArray();
if (args.Contains("--country-removal-only")) allTests = CountryRemovalTests.Cases.ToArray();
allTests = allTests.Concat(SettlementOrganizationTests.Cases).ToArray();
if (args.Contains("--settlement-organization-only")) allTests = SettlementOrganizationTests.Cases.ToArray();
allTests = allTests.Concat(OrganizationDevelopmentTests.Cases).ToArray();
if (args.Contains("--organization-development-only")) allTests = OrganizationDevelopmentTests.Cases.ToArray();
allTests=allTests.Concat(ExternalMarketTests.Cases).ToArray();
if(args.Contains("--external-market-only"))allTests=ExternalMarketTests.Cases.ToArray();
allTests=allTests.Concat(OrganizationLaborTests.Cases).ToArray();
if(args.Contains("--organization-labor-only"))allTests=OrganizationLaborTests.Cases.ToArray();
allTests=allTests.Concat(EquipmentDurabilityTests.Cases).ToArray();
if(args.Contains("--equipment-durability-only"))allTests=EquipmentDurabilityTests.Cases.ToArray();
allTests = allTests.Concat(MethodTrainingTests.Cases).ToArray();
if (args.Contains("--method-training-only")) allTests = MethodTrainingTests.Cases.ToArray();
allTests = allTests.Concat(TestScenarioCatalogTests.Cases).ToArray();
if (args.Contains("--scenario-catalog-only")) allTests = TestScenarioCatalogTests.Cases.ToArray();
allTests = allTests.Concat(ActionDrivenTests.Cases).ToArray();
if (args.Contains("--action-driven-only")) allTests = ActionDrivenTests.Cases.ToArray();
allTests = allTests.Concat(BatchTrainingTests.Cases).ToArray();
if (args.Contains("--batch-training-only")) allTests = BatchTrainingTests.Cases.ToArray();
foreach (var test in allTests)
{
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + exception); }
}
Console.WriteLine($"{allTests.Length - failed}/{allTests.Length} passed");
return failed == 0 ? 0 : 1;











