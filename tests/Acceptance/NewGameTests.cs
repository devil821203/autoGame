using Cultivation.Domain;
using Cultivation.Simulation;

internal static class NewGameTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("New game reconstructs every known scenario and never mutates the current world", () =>
        {
            Func<WorldState>[] scenarios = [() => DemoWorld.Create(), () => AutonomousWorld.Create(count: 30),
                () => MissionScenario.Create(), () => AdvancementScenario.Create(), () => CombatScenario.Create(),
                () => SocialScenario.Create(), () => PlayerDemoScenario.Create("新行者"), () => SectEconomyScenario.Create("新行者")];
            foreach (var create in scenarios)
            {
                var initial = create(); var engine = new SimulationEngine(initial);
                for (int n = 0; n < 12; n++) engine.Step(true);
                var progressed = engine.Snapshot();
                progressed.Pending.Add(new(progressed.NextSequence++, new("old-pending", "player", "sect", "spirit-stone", 1, 0, DecisionSource.Player)));
                string before = WorldCodec.Digest(progressed);
                var fresh = NewGameFactory.CreateLike(progressed, "新行者");
                initial.Characters[0] = initial.Characters[0] with { Name = "新行者" };
                if (WorldCodec.Digest(fresh) != WorldCodec.Digest(initial) || WorldCodec.Digest(progressed) != before)
                    throw new Exception("Scenario did not fully reset or original world mutated");
                if (fresh.Clock.Tick != 0 || fresh.Pending.Count != 0 || fresh.Scheduled.Count != 0 || fresh.Events.Count != 0)
                    throw new Exception("Runtime state leaked into new game");
            }
        }),
        ("Invalid new game name leaves the current world unchanged", () =>
        {
            var original = SectEconomyScenario.Create(); string digest = WorldCodec.Digest(original);
            bool rejected = false;
            try { NewGameFactory.CreateLike(original, " "); } catch (ArgumentException) { rejected = true; }
            if (!rejected || WorldCodec.Digest(original) != digest) throw new Exception("Invalid new game changed original world");
        })
    };
}
