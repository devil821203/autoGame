using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.AI;

internal static class ActionDrivenTests
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static ActionIntent Train(string id) => new(id, "player", "player", "method:QingyunFist", 1, 0, DecisionSource.Player, ActionKind.TrainMortalAttribute);
    private sealed class EmptyPlanner : IActionPlanner {
        public int Calls;
        public IReadOnlyList<ScoredAction> Evaluate(LocalObservation observation) { Calls++; return []; }
    }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("Rest recovers one AP per ten minutes and keeps existing health and stamina recovery", () => {
            var w = TestScenarioCatalog.Create("methods");
            int n = w.Time!.Budgets.FindIndex(b => b.ActorId == "player"); w.Time.Budgets[n] = w.Time.Budgets[n] with { Remaining = 28 };
            n = w.Characters.FindIndex(a => a.Id == "player"); w.Characters[n] = w.Characters[n] with { Health = 90, Stamina = w.Characters[n].Stamina! with { Current = 99 } };
            var e = new SimulationEngine(w); Check(e.ExecutePlayerActivity(new("rest-test", "player", "player", "stamina", 1, 0, DecisionSource.Player, ActionKind.Rest)).Accepted, "Rest rejected");
            w = e.Snapshot(); var a = w.Characters.Single(a => a.Id == "player");
            Check(w.Clock.Tick == 2 && GameTimeDisplay.Duration(w, w.Clock.Tick) == "20分鐘" && w.Time!.Budgets.Single(b => b.ActorId == "player").Remaining == 30 &&
                a.Health == 100 && a.Stamina!.Current == 100 && !a.Stamina.Resting && !w.Time.Charges.Any(c => c.ActionId == "rest-test"), "Rest cost/rate/cap or physical recovery wrong");
        }),
        ("AP-only economy characters can rest and minute calendars preserve daily period lengths", () => {
            var w = TestScenarioCatalog.Create("settlement");
            int n = w.Time!.Budgets.FindIndex(b => b.ActorId == "player"); w.Time.Budgets[n] = w.Time.Budgets[n] with { Remaining = 29 };
            var e = new SimulationEngine(w); Check(e.ExecutePlayerActivity(new("rest-economy", "player", "player", "stamina", 1, 0, DecisionSource.Player, ActionKind.Rest)).Accepted, "AP-only rest rejected");
            w = e.Snapshot(); Check(w.Clock.Tick == 1 && w.Time!.Budgets.Single(b => b.ActorId == "player").Remaining == 30 && w.Time.ApRestingActors.Count == 0, "AP-only rest failed");
            Check(w.Time!.Rules.TicksPerDay == 144 && CalendarCalculator.Date(144, w.Time.Rules).Day == 2 &&
                w.SectPayroll!.IntervalTicks % 144 == 0, "Calendar/day-based salary period mismatch");
        }),
        ("Player autonomy resumes activities after zero AP rest instead of only advancing time", () => {
            var w = TestScenarioCatalog.Create("methods"); int n = w.Time!.Budgets.FindIndex(b => b.ActorId == "player");
            w.Time.Budgets[n] = w.Time.Budgets[n] with { Remaining = 0 };
            var e = new SimulationEngine(w); e.SetPlayerAutonomy(true); for (int j = 0; j < 40; j++) e.Step(true);
            w = e.Snapshot(); Check(w.Events.Any(x => x.Kind == "RestActionPointsRecovered" && x.Detail.StartsWith("player:")) &&
                w.Time!.Charges.Any(c => c.ActorId == "player"), "Player failed to rest and resume a real activity");
        }),
        ("Optional player autonomy actually starts shared activities", () => {
            var e = new SimulationEngine(TestScenarioCatalog.Create("methods")); e.SetPlayerAutonomy(true);
            for (int n = 0; n < 10; n++) e.Step(true);
            var w = e.Snapshot(); Check(w.Time!.Charges.Any(c => c.ActorId == w.ControlledCharacterId), "Autonomous player only moved the clock without an activity");
        }),
        ("Zero AP waiting retains its configured duration and completes once", () => {
            var w = TestScenarioCatalog.Create("methods");
            w.Time!.ActionDriven = w.Time.ActionDriven! with { WaitDurationTicks = 3 };
            int n = w.Time.Budgets.FindIndex(b => b.ActorId == "player"); w.Time.Budgets[n] = w.Time.Budgets[n] with { Remaining = 0 };
            var e = new SimulationEngine(w);
            Check(e.ExecutePlayerActivity(new("zero-wait", "player", "player", "time", 1, 0, DecisionSource.Player, ActionKind.Wait)).Accepted, "Zero AP wait rejected");
            w = e.Snapshot(); Check(w.Clock.Tick == 3 && w.Time!.Budgets.Single(b => b.ActorId == "player").Remaining == 0 &&
                w.Events.Count(x => x.Kind == "WaitCompleted" && x.ActionId == "zero-wait") == 1, "Wait duration/AP/completion wrong");
        }),
        ("Idle decision retry reduces actual planner calls across six world ticks", () => {
            var w = DemoWorld.Create(); var config = GameConfiguration.Find();
            w.Time = new() { Rules = config.Read<TimeRules>("time/calendar.json"), ActionDriven = config.Read<ActionDrivenRules>("time/action-driven.json") with {
                MinutesPerActionPoint = null, MinutesPerDay = null, RestActionPointsPerStep = null } };
            w.PlayerAutoSchedule = false;
            var planner = new EmptyPlanner(); var e = new SimulationEngine(w, planner);
            e.Step(true); int once = planner.Calls; Check(once > 0, "No initial decision");
            for (int n = 0; n < 4; n++) e.Step(true);
            Check(planner.Calls == once, "Idle planner ran each tick");
            var restored = new SimulationEngine(WorldCodec.Clone(e.Snapshot()), planner); restored.Step(true);
            Check(planner.Calls == once * 2, "Saved idle schedule never retried");
        }),
        ("Action driven invalid input and queries never move time while legal work finishes once", () => {
            var e = new SimulationEngine(TestScenarioCatalog.Create("methods")); var initial = e.Snapshot();
            Check(e.WaitsForPlayerAction && WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(initial), "Query changed state");
            Check(!e.ExecutePlayerActivity(Train("bad") with { ResourceId = "method:unknown" }).Accepted && e.Snapshot().Clock.Tick == 0, "Invalid action moved time");
            Check(e.ExecutePlayerActivity(Train("legal")).Accepted, "Legal work rejected");
            var w = e.Snapshot();
            Check(w.Clock.Tick == 1 && w.Methods!.Learners["player"]["QingyunFist"] == 1 && w.MortalTrainingWork.All(t => t.Intent.ActorId != "player"), "Completion/time wrong");
            string digest = WorldCodec.Digest(w); Check(!e.ExecutePlayerActivity(Train("legal")).Accepted && WorldCodec.Digest(e.Snapshot()) == digest, "Duplicate changed state");
        }),
        ("Action driven multi tick work waits to completion and pays only once", () => {
            var w = TestScenarioCatalog.Create("methods"); w.Methods = w.Methods! with { Catalog = w.Methods.Catalog with {
                TrainingRules = w.Methods.Catalog.TrainingRules with { DurationTicks = 3, ActionPointCost = 3 } } };
            var e = new SimulationEngine(w); Check(e.ExecutePlayerActivity(Train("long")).Accepted, "Long work rejected"); w = e.Snapshot();
            Check(w.Clock.Tick == 3 && w.Methods!.Learners["player"]["QingyunFist"] == 1 &&
                w.Time!.Charges.Count(c => c.ActionId == "long") == 1 && w.Characters.Single(a => a.Id == "player").Stamina!.Current == 99, "Long work repeated reward/cost");
            Check(GameTimeDisplay.Duration(w, w.Clock.Tick) == "30分鐘" && w.Time!.Charges.Single(c => c.ActionId == "long").Cost == 3, "Activity minute/AP conversion wrong");
        }),
        ("Waiting advances time without AP cost and optional autonomy survives a save", () => {
            var e = new SimulationEngine(TestScenarioCatalog.Create("methods"));
            Check(e.ExecutePlayerActivity(new("wait", "player", "player", "time", 1, 0, DecisionSource.Player, ActionKind.Wait)).Accepted, "Wait rejected");
            var w = e.Snapshot(); Check(w.Clock.Tick == 1 && !w.Time!.Charges.Any(c => c.ActionId == "wait") &&
                w.Time.Budgets.Single(b => b.ActorId == "player").Remaining == w.Time.Rules.DailyActionPoints, "Wait consumed AP");
            e.SetPlayerAutonomy(true); var saved = WorldCodec.Clone(e.Snapshot()); var resumed = new SimulationEngine(saved);
            Check(!resumed.WaitsForPlayerAction, "Autonomy lost"); resumed.SetPlayerAutonomy(false); Check(resumed.WaitsForPlayerAction, "Manual mode not restored");
        }),
        ("Idle NPCs skip repeated planning until their retry and health changes wake them", () => {
            var w = TestScenarioCatalog.Create("methods"); var actor = w.Characters.Single(a => a.Id == "npc:03");
            w.Time!.IdleDecisions[actor.Id] = new(0, 5, actor.Health, actor.DemoLocationId);
            for (int n = 0; n < 5; n++) { w.Clock = w.Clock with { Tick = n }; Check(!NpcDecisionTiming.Ready(w, actor), "Idle NPC reconsidered every tick"); }
            w.Clock = w.Clock with { Tick = 5 }; Check(NpcDecisionTiming.Ready(w, actor), "Retry never happened");
            w.Clock = w.Clock with { Tick = 1 };
            Check(NpcDecisionTiming.Ready(w, actor with { Health = 99 }) && NpcDecisionTiming.Ready(w, actor, 1), "Health/visible event did not wake actor");
        })
    };
}
