using Cultivation.Domain;
using Cultivation.Simulation;

internal static class R1OffGridBattleRegressionTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("R1 off-grid battle freezes world time and catches up once", () =>
        {
            var world = LifeEventScenario.Create();
            var target = world.Characters.First(c => c.Id != "player" && c.MonsterKind == MonsterKind.None);
            var tile = world.HexMap!.Tiles.Single(t => t.PlaceId == "town").Coord;
            world.Characters = world.Characters.Select(c => c.Id == "player"
                ? c with { Health = 100, BaseAttack = 100, DemoLocationId = "town", HexPosition = tile }
                : c.Id == target.Id ? c with { Hostile = true, Health = 40, BaseAttack = 0, DemoLocationId = "town", HexPosition = tile } : c).ToList();
            var engine = new SimulationEngine(world);
            engine.AdvanceTo(1020);
            engine.Submit(new ActionIntent("r1-offgrid-fight", "player", target.Id, "combat", 1, 0, DecisionSource.Player, ActionKind.Engage));
            engine.AdvanceTo(1020);
            var battleWorld = engine.Snapshot();
            if (battleWorld.BoardBattle is not { Phase: BoardBattlePhase.Active }) throw new Exception("Battle did not start");
            battleWorld.BoardBattle.PlayerAutoCombat = true;
            engine = new SimulationEngine(battleWorld);
            while (engine.Snapshot().BoardBattle?.Phase == BoardBattlePhase.Active) engine.Step(true);
            if (engine.WorldTimeSeconds != 1020) throw new Exception("World time advanced during battle");
            long end = 1020 + battleWorld.Time!.Rules.BattleActionPoints * DiscreteTime.SecondsPerStep(battleWorld);
            engine.AdvanceTo(end, true);
            if (engine.WorldTimeSeconds != end || engine.Snapshot().Time!.BattleTime.Single(b => b.BattleId == "r1-offgrid-fight").RemainingTicks != 0)
                throw new Exception("Battle catch-up failed");
            engine.AdvanceTo(end, true);
            if (engine.Snapshot().Events.Count(e => e.Kind == "BattleWorldTimeReplayed" && e.ActionId == "r1-offgrid-fight") != 1)
                throw new Exception("Battle catch-up repeated");
        })
    };
}