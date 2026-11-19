using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class Phase2Tests
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Issue(WorldState w, string owner, string item, long amount) => w.Ledger.Add(new(w.NextSequence++, 0, $"test:{owner}:{item}", "$genesis", owner, item, amount, "Genesis"));
    private static ActionIntent Act(string id, string actor, ActionKind kind, long quantity = 1) => new(id, actor, "sect", "pill", quantity, 0, actor == "player" ? DecisionSource.Player : DecisionSource.Npc, kind, MissionId: "rejuvenation");
    private static SimulationEngine Published(WorldState? world = null, long deadline = 10080)
    {
        var w = world ?? MissionScenario.Create();
        var engine = new SimulationEngine(w);
        var publish = MissionScenario.Publish(w);
        engine.Submit(publish with { Offer = publish.Offer! with { DeadlineTick = deadline } });
        Check(engine.Step().Single().Accepted, "Publication failed");
        return engine;
    }

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Dynamic market cannot buy goods with missing funds or sell missing stock", () =>
        {
            var w = MissionScenario.Create();
            w.Ledger.RemoveAll(e => e.To == "market" && e.ResourceId == "spirit-stone" || e.To == "npc:04" && e.ResourceId == "spirit-stone" || e.To == "market" && e.ResourceId == "pill");
            Issue(w, "npc:00", "herb", 1);
            var engine = new SimulationEngine(w);
            engine.Submit(new("unfunded-shop", "npc:00", "market", "herb", 1, 0, DecisionSource.Npc, ActionKind.Sell));
            engine.Submit(new("unfunded-buyer", "npc:04", "market", "herb", 1, 0, DecisionSource.Npc, ActionKind.Buy));
            engine.Submit(new("empty-stock", "npc:01", "market", "pill", 1, 0, DecisionSource.Npc, ActionKind.Buy));
            Check(engine.Step().All(r => !r.Accepted) && engine.Snapshot().Trades.Count == 0, "Trade created money or missing goods");
        }),
        ("Player and NPC share mission acceptance delivery and payment rules", () =>
        {
            var w = MissionScenario.Create(); Issue(w, "npc:00", "pill", 1);
            var ready = Published(w).Snapshot();
            var npc = new SimulationEngine(ready);
            ready.ControlledCharacterId = "npc:00";
            var player = new SimulationEngine(ready);
            var join = Act("join", "npc:00", ActionKind.AcceptMission);
            npc.Submit(join); player.Submit(join with { Source = DecisionSource.Player });
            Check(npc.Step().Single().Accepted && player.Step().Single().Accepted, "Acceptance differed");
            var deliver = Act("deliver", "npc:00", ActionKind.DeliverMission);
            npc.Submit(deliver); player.Submit(deliver with { Source = DecisionSource.Player });
            Check(npc.Step().Single().Accepted && player.Step().Single().Accepted, "Delivery differed");
            Check(npc.Snapshot().Ledger.SequenceEqual(player.Snapshot().Ledger) && npc.Snapshot().Deliveries.SequenceEqual(player.Snapshot().Deliveries), "Payment differed by decision source");
        }),
        ("Competing publications cannot reserve the same organization funds", () =>
        {
            var w = MissionScenario.Create(); var engine = new SimulationEngine(w);
            foreach (string id in new[] { "first", "second" })
            {
                var offer = MissionScenario.Publish(w, id);
                engine.Submit(offer with { Offer = offer.Offer! with { Quantity = 50, MaximumBudget = 6000 } });
            }
            Check(engine.Step().Count(r => r.Accepted) == 1 && engine.Snapshot().Missions.Count == 1, "Organization budget reserved twice");
            Check(Inventory.Balance(engine.Snapshot(), "sect", "spirit-stone") == 4000, "Wrong reserved balance");
        }),
        ("Same-tick cancellation rejects dependent production without spending", () =>
        {
            var engine = Published();
            engine.Submit(Act("join", "npc:00", ActionKind.AcceptMission)); engine.Step();
            var before = engine.Snapshot();
            engine.Submit(Act("cancel", "player", ActionKind.CancelMission));
            engine.Submit(new("obsolete-plan", "npc:00", "market", "herb", 1, 0, DecisionSource.Npc, ActionKind.Buy, MissionId: "rejuvenation"));
            var results = engine.Step();
            Check(results[0].Accepted && !results[1].Accepted, "Cancelled mission still executed dependent purchase");
            Check(Inventory.Balance(before, "npc:00", "spirit-stone") == Inventory.Balance(engine.Snapshot(), "npc:00", "spirit-stone"), "Obsolete plan spent money");
        }),
        ("Price changes invalidate persisted local plans", () =>
        {
            var engine = Published();
            engine.Submit(Act("join", "npc:00", ActionKind.AcceptMission)); engine.Step();
            var w = engine.Snapshot();
            w.Plans.Add(new("npc:00", "Mission:rejuvenation", [ActionKind.Buy, ActionKind.Brew, ActionKind.DeliverMission], 50, "rejuvenation", 64, 110));
            engine = new(w); engine.Step(true);
            Check(engine.Snapshot().Events.Any(e => e.Kind == "PlanInvalidated" && e.ActionId == "npc:00"), "Changed quote did not invalidate old plan");
        }),
        ("Mission publication reserves funds and enforces authority and budget", () =>
        {
            var w = MissionScenario.Create();
            var engine = Published(w);
            var published = engine.Snapshot();
            Check(Inventory.Balance(published, "sect", "spirit-stone") == 7600 && Inventory.Balance(published, "$mission:rejuvenation", "spirit-stone") == 2400, "Budget not reserved");
            var offer = MissionScenario.Publish(w, "bad");
            engine.Submit(offer with { ActorId = "npc:00", Source = DecisionSource.Npc });
            engine.Submit(offer with { Id = "too-expensive", Offer = offer.Offer! with { Id = "costly", MaximumBudget = 2399 } });
            engine.Submit(offer with { Id = "overflow", Offer = offer.Offer! with { Id = "overflow", Quantity = long.MaxValue } });
            engine.Submit(offer with { Id = "duplicate-mission", Offer = offer.Offer! with { Id = "rejuvenation" } });
            Check(engine.Step().All(r => !r.Accepted), "Invalid mission accepted");
            Check(engine.Snapshot().Missions.Count == 1, "Invalid publication mutated missions");
        }),
        ("Concurrent partial deliveries never exceed quantity or budget", () =>
        {
            var w = MissionScenario.Create(); Issue(w, "npc:00", "pill", 15); Issue(w, "npc:01", "pill", 15);
            var engine = Published(w);
            engine.Submit(Act("join0", "npc:00", ActionKind.AcceptMission));
            engine.Submit(Act("join1", "npc:01", ActionKind.AcceptMission));
            Check(engine.Step().All(r => r.Accepted), "Could not join");
            Parallel.Invoke(() => engine.Submit(Act("deliver0", "npc:00", ActionKind.DeliverMission, 15)), () => engine.Submit(Act("deliver1", "npc:01", ActionKind.DeliverMission, 15)));
            Check(engine.Step().All(r => r.Accepted), "Partial delivery failed");
            var final = engine.Snapshot(); var mission = final.Missions.Single();
            Check(mission.Delivered == 20 && mission.Status == MissionStatus.Completed && final.Deliveries.Sum(d => d.Payment) == 2400, "Mission exceeded limits");
            Check(Inventory.Balance(final, "sect", "pill") == 25 && Inventory.Balance(final, "$mission:rejuvenation", "spirit-stone") == 0, "Wrong mission inventory or budget");
            Check(final.Deliveries.Select(d => d.Quantity).Order().SequenceEqual(new long[] { 5, 15 }), "Excess quantity was not capped");
            engine.Submit(Act("deliver0", "npc:00", ActionKind.DeliverMission, 15));
            engine.Submit(Act("after-complete", "npc:01", ActionKind.DeliverMission));
            Check(engine.Step().All(r => !r.Accepted) && engine.Snapshot().Deliveries.Count == 2, "Duplicate payout occurred");
        }),
        ("Expiry rejects deadline delivery and refunds unused budget once", () =>
        {
            var w = MissionScenario.Create(); Issue(w, "npc:00", "pill", 4);
            var engine = Published(w, 6);
            engine.Submit(Act("join", "npc:00", ActionKind.AcceptMission)); engine.Step();
            engine.Submit(Act("part", "npc:00", ActionKind.DeliverMission, 3)); engine.Step();
            engine.Step(); engine.Step();
            engine.Submit(Act("deadline", "npc:00", ActionKind.DeliverMission));
            Check(!engine.Step().Single().Accepted, "Deadline was inclusive");
            var final = engine.Snapshot();
            Check(final.Missions.Single().Status == MissionStatus.Expired && Inventory.Balance(final, "sect", "spirit-stone") == 9640, "Expiry refund wrong");
            int refunds = final.Ledger.Count(e => e.Reason == "MissionRefund");
            engine.Step(); engine.Step();
            Check(engine.Snapshot().Ledger.Count(e => e.Reason == "MissionRefund") == refunds && refunds == 1, "Refund repeated");
        }),
        ("Cancellation is authorized and preserves partial delivery payments", () =>
        {
            var w = MissionScenario.Create(); Issue(w, "npc:00", "pill", 2);
            var engine = Published(w);
            engine.Submit(Act("join", "npc:00", ActionKind.AcceptMission)); engine.Step();
            engine.Submit(Act("part", "npc:00", ActionKind.DeliverMission, 2)); engine.Step();
            engine.Submit(Act("forbidden-cancel", "npc:00", ActionKind.CancelMission));
            Check(!engine.Step().Single().Accepted, "NPC cancelled leader's mission");
            engine.Submit(Act("cancel", "player", ActionKind.CancelMission));
            Check(engine.Step().Single().Accepted, "Leader could not cancel");
            var final = engine.Snapshot();
            Check(final.Missions.Single().Status == MissionStatus.Cancelled && Inventory.Balance(final, "sect", "spirit-stone") == 9760 && final.Deliveries.Sum(d => d.Payment) == 240, "Cancellation reversed valid payment");
        }),
        ("Stock prices move, enforce limits and avoid profitable immediate round trips", () =>
        {
            var w = MissionScenario.Create();
            var engine = new SimulationEngine(w);
            var quote = MarketPricing.Quote(w, "herb");
            ActionIntent Buy(string id, string actor) => new(id, actor, "market", "herb", 1, 0, DecisionSource.Npc, ActionKind.Buy, LimitPrice: quote.BuyPrice);
            engine.Submit(Buy("buy0", "npc:00")); engine.Submit(Buy("buy1", "npc:01"));
            var results = engine.Step();
            Check(results[0].Accepted && !results[1].Accepted, "Stale limit price was accepted");
            var bought = engine.Snapshot();
            Check(MarketPricing.Quote(bought, "herb").BuyPrice > quote.BuyPrice && bought.Trades.Count == 1, "Stock did not affect quote");
            long before = Inventory.Balance(w, "npc:00", "spirit-stone");
            engine.Submit(new("sell-back", "npc:00", "market", "herb", 1, 0, DecisionSource.Npc, ActionKind.Sell)); engine.Step();
            Check(Inventory.Balance(engine.Snapshot(), "npc:00", "spirit-stone") <= before, "Round trip minted wealth");
            w.MarketListings[0] = w.MarketListings[0] with { TargetStock = long.MaxValue };
            Check(MarketPricing.Quote(w, "herb").BuyPrice == w.MarketListings[0].MaximumPrice, "Quote did not clamp at upper bound");
        }),
        ("Mission utility uses skill, risk, funds, local source and deadline", () =>
        {
            var actor = new CharacterState("actor", "Alchemist") { AlchemyChance = 0.95 };
            var rules = new EconomyRules(HerbPrice: 65, PillPrice: 110, HerbsPerPill: 1, GatherTicks: 8);
            var task = new MissionObservation("task", "sect", "pill", 20, 120, 100, false, "board:sect:valley", 0);
            var o = new LocalObservation(actor, 0, 100, 0, 0, 100, 1000, 50, 0, rules, "market:valley", 0, Missions: new[] { task });
            var planner = new UtilityPlanner();
            Check(planner.Evaluate(o).Any(c => c.Intent.Kind == ActionKind.AcceptMission), "Capable actor ignored mission");
            Check(!planner.Evaluate(o with { Actor = actor with { AlchemyChance = 0 } }).Any(c => c.Intent.Kind == ActionKind.AcceptMission), "Incapable actor joined without supply");
            Check(!planner.Evaluate(o with { Missions = new[] { task with { Source = "global" } } }).Any(c => c.Intent.Kind == ActionKind.AcceptMission), "Used global mission knowledge");
            Check(!planner.Evaluate(o with { Missions = new[] { task with { DeadlineTick = 1 } } }).Any(c => c.Intent.Kind == ActionKind.AcceptMission), "Ignored deadline");
            Check(!planner.Evaluate(o with { Actor = actor with { AlchemyChance = 0.05, Caution = 10 }, RecentFailures = 5 }).Any(c => c.Intent.Kind == ActionKind.AcceptMission), "Ignored failure risk");
        }),
        ("Rejuvenation supply chain completes within budget and resumes identically", () =>
        {
            var initial = MissionScenario.Create();
            var engine = Published(initial);
            for (int n = 0; n < 30; n++) engine.Step(true);
            string directory = Path.Combine(Path.GetTempPath(), "cultivation-mission-" + Guid.NewGuid().ToString("N"));
            var store = new JsonWorldStore();
            var path = Path.Combine(directory, "checkpoint.json");
            SimulationEngine restored;
            try { store.Save(path, engine.Snapshot()); restored = new(store.Load(path)); }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            for (int n = 0; n < 100; n++) { engine.Step(true); restored.Step(true); }
            var final = engine.Snapshot();
            Check(WorldCodec.Digest(final) == WorldCodec.Digest(restored.Snapshot()), "Mission continuation diverged");
            var replay = Published(initial);
            for (int n = 0; n < 130; n++) replay.Step(true);
            Check(WorldCodec.Digest(final) == WorldCodec.Digest(replay.Snapshot()), "Mission replay diverged");
            var task = final.Missions.Single();
            Check(task.Delivered == 20 && task.Status == MissionStatus.Completed && final.Deliveries.Sum(d => d.Payment) == 2400, "Representative task incomplete");
            Check(final.Deliveries.Select(d => d.ActorId).Distinct().Count() > 1 && final.Events.Any(e => e.Kind == "BrewFailed") && final.Events.Any(e => e.Kind == "PriceChanged"), "Supply chain did not vary");
            Check(final.Trades.Any(t => t.Kind == ActionKind.Buy && t.ResourceId == "herb") && final.Trades.Any(t => t.Kind == ActionKind.Sell && t.ResourceId == "herb"), "No raw-material supply chain");
            long Money(WorldState world) => world.Ledger.Where(e => e.ResourceId == "spirit-stone" && e.From == "$genesis").Sum(e => e.Quantity);
            var accounts = final.Ledger.Where(e => e.ResourceId == "spirit-stone").Select(e => e.To).Distinct().ToArray();
            Check(accounts.Sum(a => Inventory.Balance(final, a, "spirit-stone")) == Money(initial), "Currency conservation failed");
        }),
        ("Tampered mission reservation and receipts are rejected", () =>
        {
            var world = Published().Snapshot();
            world.Missions[0] = world.Missions[0] with { Delivered = 1 };
            bool rejected = false;
            try { _ = new SimulationEngine(world); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Tampered mission accepted");
        })
    };
}
