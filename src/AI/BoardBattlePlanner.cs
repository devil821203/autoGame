using Cultivation.Domain;

namespace Cultivation.AI;

public static class BoardBattlePlanner
{
    public static BoardTurn Choose(BoardBattle battle, string actorId, IReadOnlyList<CharacterState> visible)
    {
        var actor = battle.Fighters.Single(f => f.ActorId == actorId);
        var enemies = battle.Fighters.Where(f => f.Side != actor.Side && visible.Any(c => c.Id == f.ActorId && c.Health > 0)).ToArray();
        var cells = new[] { actor.Cell, new(actor.Cell.Row - 1, actor.Cell.Depth), new(actor.Cell.Row + 1, actor.Cell.Depth),
            new(actor.Cell.Row, actor.Cell.Depth - 1), new(actor.Cell.Row, actor.Cell.Depth + 1) }
            .Where(c => BoardBattleGeometry.CanMove(actor, c, battle.Fighters)).ToArray();
        foreach (var cell in cells)
        {
            var target = enemies.Where(e => BoardBattleGeometry.CanTarget(actor with { Cell = cell }, e, enemies))
                .OrderBy(e => visible.Single(c => c.Id == e.ActorId).Health).ThenBy(e => e.ActorId, StringComparer.Ordinal).FirstOrDefault();
            if (target is not null) return new(battle.Id, battle.Round + 1, cell == actor.Cell ? null : cell, target.ActorId);
        }
        if (enemies.Length == 0) return new(battle.Id, battle.Round + 1);
        var best = cells.OrderBy(c => enemies.Min(e => c.Depth + Math.Abs(c.Row - e.Cell.Row))).ThenBy(c => c == actor.Cell ? 0 : 1).First();
        return new(battle.Id, battle.Round + 1, best == actor.Cell ? null : best);
    }
}
