using Cultivation.Domain;
using Cultivation.Simulation;

internal static class BatchTrainingTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static ActionIntent Batch(string id = "batch", long count = 6) => new(id, "player", "player", "method:QingyunFist", count, 0, DecisionSource.Player, ActionKind.TrainMortalAttribute);
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("Batch meditation and ordinary attribute training share the same repeat contract", () => {
            foreach (bool meditation in new[] { false, true }) {
                var e = new SimulationEngine(TestScenarioCatalog.Create("methods"));
                var i = new ActionIntent("ordinary-batch", "player", "player", meditation ? "qi" : "Intelligence", 6, 0, DecisionSource.Player,
                    meditation ? ActionKind.MeditateQi : ActionKind.TrainMortalAttribute);
                Check(e.ExecutePlayerActivity(i).Accepted, "Ordinary batch rejected"); var w = e.Snapshot();
                var s = w.Characters.Single(a => a.Id == "player").MortalCultivation!;
                Check(w.Clock.Tick == 6 && (meditation ? s.QiInitiationExperience : s.Experience.GetValueOrDefault(MortalAttribute.Intelligence)) == 6,
                    "Ordinary batch reward mismatch");
            }
        }),
        ("Batch training reevaluates fatigue for each completed segment", () => {
            var w = TestScenarioCatalog.Create("methods"); int n = w.Characters.FindIndex(a => a.Id == "player");
            w.Characters[n] = w.Characters[n] with { Stamina = w.Characters[n].Stamina! with { Current = 31 } };
            var e = new SimulationEngine(w); Check(e.ExecutePlayerActivity(Batch()).Accepted, "Fatigue batch rejected"); w = e.Snapshot();
            Check(w.Methods!.Learners["player"]["QingyunFist"] == 4 && w.Characters[n].Stamina!.Current == 25,
                "Fatigue was multiplied once for the whole batch instead of per segment");
        }),
        ("Six training repetitions spend six AP for sixty minutes with six separate rewards", () => {
            var e = new SimulationEngine(TestScenarioCatalog.Create("methods")); Check(e.ExecutePlayerActivity(Batch()).Accepted, "Batch rejected");
            var w = e.Snapshot(); var a = w.Characters.Single(a => a.Id == "player");
            Check(w.Clock.Tick == 6 && GameTimeDisplay.Duration(w, w.Clock.Tick) == "60分鐘" &&
                w.Time!.Charges.Single(c => c.ActionId == "batch").Cost == 6 && w.Time.Budgets.Single(b => b.ActorId == "player").Remaining == 24 &&
                a.Stamina!.Current == 94 && w.Methods!.Learners["player"]["QingyunFist"] == 6 &&
                w.Events.Count(x => x.ActionId == "batch" && x.Kind == "MethodTrainingCompleted") == 6, "Wrong duration/payment/reward");
        }),
        ("Partial training batch pays upfront but rewards progressively and resumes from saves", () => {
            var e = new SimulationEngine(TestScenarioCatalog.Create("methods")); e.Submit(Batch()); e.Step();
            var w = e.Snapshot(); Check(w.MortalTrainingWork.Single(t => t.Intent.ActorId == "player").RemainingIterations == 5 &&
                w.Methods!.Learners["player"]["QingyunFist"] == 1 && w.Time!.Budgets.Single(b => b.ActorId == "player").Remaining == 24, "Early total reward or missing reservation");
            var resumed = new SimulationEngine(WorldCodec.Clone(w)); for (int n = 0; n < 5; n++) { e.Step(); resumed.Step(); }
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Batch save continuation diverged");
        }),
        ("Exhaustion stops remaining repetitions without refunding prepaid AP", () => {
            var w = TestScenarioCatalog.Create("methods"); int n = w.Characters.FindIndex(a => a.Id == "player");
            w.Characters[n] = w.Characters[n] with { Stamina = w.Characters[n].Stamina! with { Current = 2 } };
            var e = new SimulationEngine(w); e.Submit(Batch()); e.Step(); e.Step(); w = e.Snapshot();
            Check(w.MortalTrainingWork.All(t => t.Intent.ActorId != "player") && w.Characters[n].Stamina!.Current == 0 &&
                w.Methods!.Learners["player"]["QingyunFist"] == 1 && w.Time!.Budgets.Single(b => b.ActorId == "player").Remaining == 24 &&
                w.Events.Any(x => x.ActionId == "batch" && x.Kind == "MortalTrainingInterrupted"), "Exhaustion did not stop batch or AP refunded");
        }),
        ("Attack interrupts a partially completed batch and preserves only completed gains", () => {
            var w = TestScenarioCatalog.Create("methods"); var player = w.Characters.Single(a => a.Id == "player");
            string enemy = w.Characters.First(a => a.Id != "player" && a.MonsterKind == MonsterKind.None).Id;
            w.Characters = w.Characters.Select(a => a.Id == "player" ? a with { Hostile = true } : a.Id == enemy ? a with {
                DemoLocationId = player.DemoLocationId, HexPosition = player.HexPosition, RegionId = player.RegionId } : a).ToList();
            var e = new SimulationEngine(w); e.Submit(Batch()); e.Step();
            e.Submit(new("attack-batch", enemy, "player", "combat", 1, 0, DecisionSource.Npc, ActionKind.Engage));
            Check(e.Step().Single().Accepted, "Attack rejected"); w = e.Snapshot();
            Check(w.MortalTrainingWork.All(t => t.Intent.ActorId != "player") && w.Methods!.Learners["player"]["QingyunFist"] == 1 &&
                w.Time!.Charges.Single(c => c.ActionId == "batch").Cost == 6, "Interrupted batch gain/payment wrong");
        }),
        ("Batch count AP and historical mode restrictions reject without advancing time", () => {
            var e = new SimulationEngine(TestScenarioCatalog.Create("methods")); string digest = WorldCodec.Digest(e.Snapshot());
            Check(!e.ExecutePlayerActivity(Batch(count: 31)).Accepted && !e.ExecutePlayerActivity(Batch("zero", 0)).Accepted &&
                WorldCodec.Digest(e.Snapshot()) == digest, "Invalid batch changed state");
            var poor = TestScenarioCatalog.Create("methods"); int n = poor.Time!.Budgets.FindIndex(b => b.ActorId == "player");
            poor.Time.Budgets[n] = poor.Time.Budgets[n] with { Remaining = 5 }; var shortAp = new SimulationEngine(poor);
            string before = WorldCodec.Digest(shortAp.Snapshot());
            Check(!shortAp.ExecutePlayerActivity(Batch()).Accepted && WorldCodec.Digest(shortAp.Snapshot()) == before, "Insufficient total AP partially started batch");
            var old = new SimulationEngine(MortalCultivationScenario.Create()); old.Submit(Batch(count: 2));
            Check(!old.Step().Single().Accepted, "Historical mode accepted multiple repetitions");
        })
    };
}
