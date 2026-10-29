using Cultivation.Domain;
using Cultivation.Simulation;

internal static class TestScenarioCatalogTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("Current scenario catalog rebuilds deterministic fresh worlds without touching the source", () => {
            foreach (var profile in TestScenarioCatalog.Read().Scenarios) {
                var initial = TestScenarioCatalog.Create(profile.Id); string digest = WorldCodec.Digest(initial);
                var rebuilt = TestScenarioCatalog.Create(profile.Id);
                Check(WorldCodec.Digest(rebuilt) == digest && initial.Clock.Tick == 0, "Fresh scenario differs: " + profile.Id);
                var engine = new SimulationEngine(initial); engine.Step(true);
                Check(WorldCodec.Digest(initial) == digest && rebuilt.Clock.Tick == 0, "Source changed: " + profile.Id);
            }
        }),
        ("Scenario capabilities remain separated and removed gameplay stays disabled", () => {
            var m = TestScenarioCatalog.Create("methods"); var s = TestScenarioCatalog.Create("settlement"); var b = TestScenarioCatalog.Create("battle");
            Check(m.Methods is not null && m.SettlementOrganizationDemo is null && s.Methods is null && s.ExternalMarket is not null &&
                s.OrganizationLaborRules is not null && b.BoardBattle is not null && b.Methods is null, "Scenario capabilities leaked");
            Check(new[] { m, s, b }.All(w => w.MedicineOnly && !w.AlchemyEnabled &&
                w.Currencies!.Where(c => c.Kind == CurrencyKind.SpiritStone).All(c => !c.Enabled)), "Disabled gameplay reactivated");
        }),
        ("New game from a method world retains methods and the goal NPC while resetting progress", () => {
            var current = TestScenarioCatalog.Create("methods"); current.Methods!.Learners["npc:method-trainee"]["QingyunFist"] = 125;
            string digest = WorldCodec.Digest(current); var fresh = NewGameFactory.CreateLike(current, "新行者");
            Check(fresh.Methods is not null && fresh.Methods.TrainingFocusActors!.Contains("npc:method-trainee") &&
                fresh.Methods.Learners.Values.All(skills => skills.Values.All(x => x == 0)) && fresh.Clock.Tick == 0, "Method profile/progress reset failed");
            Check(WorldCodec.Digest(current) == digest, "Old world overwritten");
        }),
        ("Scenario selector rejects unknown profile IDs", () => {
            try { TestScenarioCatalog.Create("unknown-profile"); } catch (ArgumentException) { return; }
            throw new Exception("Unknown scenario accepted");
        })
    };
}
