using Cultivation.Domain;
using Cultivation.Simulation;

internal static class BoardBattleTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static ActionIntent Turn(WorldState w, string id = "board-test", BattleCell? move = null, string? target = null,
        string actor = "player", DecisionSource source = DecisionSource.Player) => new(id, actor, w.BoardBattle!.Id, "board-turn", 1, 0, source,
            ActionKind.BoardTurn, BoardTurn: new(w.BoardBattle.Id, w.BoardBattle.Round + 1, move, target));
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Player automatic combat can be toggled and follows the shared rules", () =>
        {
            var w = BoardBattleScenario.Create(); var e = new SimulationEngine(w);
            ActionIntent Toggle(string id, bool on) => new(id, "player", w.BoardBattle!.Id, "auto-combat", on ? 1 : 0, 0, DecisionSource.Player, ActionKind.SetBoardAutoCombat);
            Check(!w.BoardBattle!.PlayerAutoCombat, "Auto unexpectedly enabled");
            e.Submit(Toggle("auto-on", true)); Check(e.Step().Single().Accepted, "Enable rejected");
            var on = e.Snapshot(); Check(on.BoardBattle!.PlayerAutoCombat && on.Events.Count(v => v.Kind == "BoardAttacked" && v.Detail.StartsWith("player->")) == 1, "Auto did not attack on enable round");
            e.Submit(Toggle("auto-off", false)); e.Step(); var off = e.Snapshot();
            Check(!off.BoardBattle!.PlayerAutoCombat && off.Events.Count(v => v.Kind == "BoardAttacked" && v.Detail.StartsWith("player->")) == 1, "Disable did not stop auto");
            Check(ActionValidator.Validate(off, Toggle("wrong-source", true) with { Source = DecisionSource.Npc, ActorId = "ally" }) is not null, "NPC could change player setting");
        }),
        ("Manual player turn takes precedence over automatic battle", () =>
        {
            var w = BoardBattleScenario.Create(); w.BoardBattle!.PlayerAutoCombat = true; var e = new SimulationEngine(w);
            e.Submit(Turn(w)); e.Step(); var end = e.Snapshot();
            Check(!end.Events.Any(v => v.Kind == "BoardAttacked" && v.Detail.StartsWith("player->")), "Auto overrode manual wait");
            Check(end.Events.Count(v => v.Kind == "BoardTurnStarted" && v.Detail.EndsWith(":player")) == 1, "Player took extra turn");
        }),
        ("Player automatic combat persists and stops at terminal battle", () =>
        {
            var w = BoardBattleScenario.Create(); w.BoardBattle!.PlayerAutoCombat = true;
            var e = new SimulationEngine(w); var resumed = new SimulationEngine(WorldCodec.Clone(w));
            for (int n = 0; n < 100; n++) { e.Step(true); resumed.Step(true); }
            var end = e.Snapshot(); Check(WorldCodec.Digest(end) == WorldCodec.Digest(resumed.Snapshot()), "Auto save continuation diverged");
            Check(end.BoardBattle!.Phase == BoardBattlePhase.Finished, "Auto battle never finished");
            int count = end.Events.Count(v => v.Kind == "BoardAttacked"); e.Step(true);
            Check(e.Snapshot().Events.Count(v => v.Kind == "BoardAttacked") == count, "Auto attacked after battle");
            Check(!NewGameFactory.CreateLike(end, "新行者").BoardBattle!.PlayerAutoCombat, "New game retained enabled auto");
        }),        ("Board rounds advance once per Tick with frozen speed and ordinal ties", () =>
        {
            var w = BoardBattleScenario.Create(); var e = new SimulationEngine(w); e.Step(); var end = e.Snapshot();
            Check(end.BoardBattle!.Round == 1 && end.Clock.Tick == 1, "Round/Tick ratio wrong");
            Check(end.BoardBattle.LastInitiative.SequenceEqual(new[] { "player", "ally", "enemy-archer", "npc" }), "Initiative tie order wrong");
            Check(end.Characters.All(c => c.Health == 100), "Uncommanded actors did not wait");
        }),
        ("Board turn moves then attacks once and rejects duplicate turns", () =>
        {
            var w = BoardBattleScenario.Create(); w.BoardBattle!.Fighters[0] = w.BoardBattle.Fighters[0] with { Cell = new(2, 1) };
            var e = new SimulationEngine(w); e.Submit(Turn(w, move: new(2, 0), target: "npc")); e.Submit(Turn(w, "duplicate", target: "npc"));
            var result = e.Step(); var end = e.Snapshot();
            Check(result.Count(r => r.Accepted) == 1 && end.Characters.Single(c => c.Id == "npc").Health == 90, "Turn duplicated or attack preceded move");
            Check(end.Events.Single(v => v.Kind == "BoardMoved").Sequence < end.Events.Single(v => v.Kind == "BoardAttacked").Sequence, "Phase order wrong");
            Check(end.Characters.Single(c => c.Id == "player").DemoLocationId == w.Characters.Single(c => c.Id == "player").DemoLocationId, "Board move changed world location");
            e.Submit(Turn(end, "second-turn", move: new(2, 1))); e.Step(); Check(e.Snapshot().BoardBattle!.Round == 2, "Next round order unavailable");
        }),
        ("Board orders reject cross control stale rounds illegal cells and occupied incapacitated cells", () =>
        {
            var w = BoardBattleScenario.Create();
            Check(ActionValidator.Validate(w, Turn(w, actor: "ally")) is not null, "Player controlled ally");
            Check(ActionValidator.Validate(w, Turn(w) with { BoardTurn = new(w.BoardBattle!.Id, 2) }) is not null, "Stale/future round accepted");
            Check(ActionValidator.Validate(w, Turn(w, move: new(5, 0))) is not null, "Out of bounds accepted");
            Check(ActionValidator.Validate(w, Turn(w, move: new(2, 2))) is not null, "Two step move accepted");
            Check(ActionValidator.Validate(w, Turn(w, target: "ally")) is not null, "Ally attack accepted");
            w.BoardBattle!.Fighters[1] = w.BoardBattle.Fighters[1] with { Cell = new(2, 1) };
            w.Characters = w.Characters.Select(c => c.Id == "ally" ? c with { Health = 0 } : c).ToList();
            Check(ActionValidator.Validate(w, Turn(w, move: new(2, 1))) is not null, "Incapacitated actor did not occupy cell");
            Check(ActionValidator.Validate(w, new("other-action", "player", "sect", "spirit-stone", 1, 0, DecisionSource.Player)) is not null, "Combatant could trade outside battle");
        }),
        ("Board target rules enforce nearest active same row and ranged distance", () =>
        {
            var a = new BoardFighter("a", BattleSide.Left, new(2, 0), 1, 10);
            var front = new BoardFighter("f", BattleSide.Right, new(2, 0), 1, 1);
            var rear = new BoardFighter("r", BattleSide.Right, new(2, 2), 1, 1);
            Check(BoardBattleGeometry.CanTarget(a, front, new[] { front, rear }) && !BoardBattleGeometry.CanTarget(a, rear, new[] { front, rear }), "Melee bypassed nearest");
            Check(BoardBattleGeometry.CanTarget(a, rear, new[] { rear }), "No active front but rear blocked");
            var other = rear with { Cell = new(3, 3) };
            Check(!BoardBattleGeometry.CanTarget(a, other, new[] { other }), "Melee crossed row");
            Check(BoardBattleGeometry.CanTarget(a with { Ranged = true, Range = 5 }, other, new[] { other }), "Ranged limit rejected");
            Check(!BoardBattleGeometry.CanTarget(a with { Ranged = true, Range = 4 }, other, new[] { other }), "Out of range accepted");
        }),
        ("Board last incapacity stops remaining initiative and awards no unconfirmed loot", () =>
        {
            var w = BoardBattleScenario.Create(); w.Characters = w.Characters.Select(c => c.Id == "npc" ? c with { Health = 1 } : c.Id == "enemy-archer" ? c with { Health = 0 } : c).ToList();
            var e = new SimulationEngine(w); e.Submit(Turn(w, target: "npc")); e.Step(); var end = e.Snapshot();
            Check(end.BoardBattle!.Phase == BoardBattlePhase.Finished && end.BoardBattle.Winner == BattleSide.Left, "Last enemy did not end battle");
            Check(end.Events.Count(v => v.Kind == "BoardTurnStarted") == 1 && end.Events.Count(v => v.Kind == "BoardBattleEnded") == 1, "Round continued or ended twice");
            Check(end.Ledger.Count == w.Ledger.Count && end.Loot.Count == 0, "Prototype awarded loot");
        }),
        ("Board maximum round executes every turn then draws", () =>
        {
            var w = BoardBattleScenario.Create(); w.BoardRules = new(1); var e = new SimulationEngine(w); e.Step(); var end = e.Snapshot();
            Check(end.BoardBattle!.Phase == BoardBattlePhase.Finished && end.BoardBattle.Winner is null, "Round cap did not draw");
            Check(end.Events.Count(v => v.Kind == "BoardTurnStarted") == 4, "Round cap stopped inside first turn");
        }),
        ("Board NPC autonomy and pending orders resume identically and reset same scenario", () =>
        {
            var w = BoardBattleScenario.Create(); var e = new SimulationEngine(w); e.Submit(Turn(w, target: "npc"));
            var resumed = new SimulationEngine(e.Snapshot());
            for (int n = 0; n < 20; n++) { e.Step(true); resumed.Step(true); }
            var end = e.Snapshot(); Check(WorldCodec.Digest(end) == WorldCodec.Digest(resumed.Snapshot()), "Pending turn resume diverged");
            Check(end.Events.Any(v => v.Kind == "BoardAttacked" && !v.Detail.StartsWith("player->", StringComparison.Ordinal)), "NPCs never attacked");
            var fresh = NewGameFactory.CreateLike(end, "新行者"); Check(fresh.BoardBattle!.Round == 0 && fresh.Characters.All(c => c.Health == 100), "New game lost scenario/reset");
        }),
        ("Board five versus five preserves cap and resolves deterministic rounds", () =>
        {
            var w = BoardBattleScenario.Create(); w.BoardBattle!.Fighters.Clear();
            for (int n = 0; n < 10; n++)
            {
                string id = n == 0 ? "player" : n == 5 ? "npc" : "unit:" + n;
                if (!w.Characters.Any(c => c.Id == id)) w.Characters.Add(new(id, id));
                w.BoardBattle.Fighters.Add(new(id, n < 5 ? BattleSide.Left : BattleSide.Right, new(n % 5, 0), 10, 1));
            }
            var e = new SimulationEngine(w); e.Step(true);
            Check(e.Snapshot().BoardBattle!.LastInitiative.Count == 10, "Five versus five missing turns");
        })
    };
}

