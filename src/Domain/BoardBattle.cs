namespace Cultivation.Domain;

public enum BattleSide { Left, Right }
public enum BoardBattlePhase { Active, Finished }
public readonly record struct BattleCell(int Row, int Depth)
{
    public bool InBounds => Row is >= 0 and < 5 && Depth is >= 0 and < 5;
}
public sealed record BoardFighter(string ActorId, BattleSide Side, BattleCell Cell,
    int Speed, int Range, bool Ranged = false);
public sealed record BoardTurn(string BattleId, int Round, BattleCell? MoveTo = null, string? AttackTargetId = null);
public sealed record BoardQueuedTurn(string ActorId, BoardTurn Order);
public sealed class BoardBattle
{
    public bool PlayerAutoCombat { get; set; }
    public string Id { get; set; } = "board-demo";
    public int Round { get; set; }
    public BoardBattlePhase Phase { get; set; }
    public BattleSide? Winner { get; set; }
    public List<BoardFighter> Fighters { get; set; } = [];
    public List<BoardQueuedTurn> Orders { get; set; } = [];
    public List<string> LastInitiative { get; set; } = [];
}
public sealed record BoardBattleRules(int MaximumRounds = 100);
public sealed record BoardScenarioSettings(List<BoardFighter> Fighters);


public static class BoardBattleGeometry
{
    public static int Distance(BattleCell own, BattleCell enemy) => own.Depth + 1 + enemy.Depth + Math.Abs(own.Row - enemy.Row);
    public static bool CanTarget(BoardFighter actor, BoardFighter target, IEnumerable<BoardFighter> livingEnemies) =>
        actor.Side != target.Side && Distance(actor.Cell, target.Cell) <= actor.Range &&
        (actor.Ranged || actor.Cell.Row == target.Cell.Row && !livingEnemies.Any(e => e.Cell.Row == target.Cell.Row && e.Cell.Depth < target.Cell.Depth));
    public static bool CanMove(BoardFighter actor, BattleCell destination, IEnumerable<BoardFighter> fighters) =>
        destination.InBounds && Math.Abs(actor.Cell.Row - destination.Row) + Math.Abs(actor.Cell.Depth - destination.Depth) <= 1 &&
        !fighters.Any(f => f.ActorId != actor.ActorId && f.Side == actor.Side && f.Cell == destination);
}

