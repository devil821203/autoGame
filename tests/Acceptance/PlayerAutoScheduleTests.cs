using System.Text.Json.Nodes;
using Cultivation.Domain;
using Cultivation.Simulation;

internal static class PlayerAutoScheduleTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Player world autonomy selects legal player-source actions", () =>
        {
            var w = PlayerAutoScheduleScenario.Create(); var e = new SimulationEngine(w);
            for (int n = 0; n < 20; n++) e.Step();
            var end = e.Snapshot();
            Check(end.ProcessedIntentIds.Any(id => id.StartsWith("player-auto:player:", StringComparison.Ordinal)), "Player never acted autonomously");
            Check(!end.Events.Any(v => v.Kind == "Rejected" && v.Detail == "Actor is not AI controlled"), "Player used NPC source");
            Check(ActionValidator.Validate(w, new("forged", "player", "sect", "spirit-stone", 1, 0, DecisionSource.Npc)) is not null, "NPC source gained player control");
            Check(e.LastDiagnostics is not null, "Diagnostics missing");
        }),
        ("Manual player orders take priority and busy work never reschedules", () =>
        {
            var w = PlayerAutoScheduleScenario.Create(); var e = new SimulationEngine(w);
            e.Submit(new("manual-journey", "player", "dungeon", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
            Check(e.Step().Single().Accepted, "Manual trip rejected");
            Check(!e.Snapshot().ProcessedIntentIds.Any(id => id.StartsWith("player-auto:", StringComparison.Ordinal)), "Auto overrode manual command");
            for (int n = 0; n < 30 && e.Snapshot().Scheduled.Any(t => t.Intent.ActorId == "player"); n++)
            {
                e.Step(); Check(!e.Snapshot().ProcessedIntentIds.Any(id => id.StartsWith("player-auto:", StringComparison.Ordinal)), "Busy player started another action");
            }
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").DemoLocationId == "dungeon", "Manual trip did not finish");
        }),
        ("Player autonomous rest uses shared recovery rules", () =>
        {
            var w = PlayerAutoScheduleScenario.Create();
            w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Health = 80, Stamina = new(100) } : c).ToList();
            var e = new SimulationEngine(w); e.Step();
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Resting, "Player did not choose rest");
            e.Step(); Check(e.Snapshot().Characters.Single(c => c.Id == "player").Health == 90, "Player rest differs from NPC rule");
        }),
        ("Player world autonomy persists resets and leaves legacy worlds unchanged", () =>
        {
            var e = new SimulationEngine(PlayerAutoScheduleScenario.Create()); e.Step(true);
            var resumed = new SimulationEngine(e.Snapshot());
            for (int n = 0; n < 25; n++) { e.Step(true); resumed.Step(true); }
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Player auto continuation differs");
            Check(NewGameFactory.CreateLike(e.Snapshot(), "新行者").PlayerAutoSchedule, "New game lost auto demo mode");
            var node = JsonNode.Parse(WorldCodec.Serialize(GrowthScenario.Create()))!; node.AsObject().Remove("PlayerAutoSchedule");
            var legacy = WorldCodec.Deserialize(node.ToJsonString()); Check(!legacy.PlayerAutoSchedule, "Old world enabled player autonomy");
        })
    };
}
