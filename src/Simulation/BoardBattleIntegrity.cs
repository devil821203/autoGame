using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class BoardBattleIntegrity
{
    internal static void Validate(WorldState w)
    {
        if (w.BoardRules is null && w.BoardBattle is null) return;
        if (w.BoardRules is not { } rules || w.BoardBattle is not { } b || rules.MaximumRounds is < 1 or > 1000 ||
            string.IsNullOrWhiteSpace(b.Id) || b.Round < 0 || b.Round > rules.MaximumRounds || !Enum.IsDefined(b.Phase) ||
            b.Fighters.Count is < 2 or > 10 || b.Fighters.Select(f => f.ActorId).Distinct(StringComparer.Ordinal).Count() != b.Fighters.Count ||
            Enum.GetValues<BattleSide>().Any(side => b.Fighters.Count(f => f.Side == side) is < 1 or > 5) ||
            b.Fighters.Any(f => !Enum.IsDefined(f.Side) || !f.Cell.InBounds || f.Speed is < 0 or > 10000 || f.Range is < 1 or > 100 ||
                !w.Characters.Any(c => c.Id == f.ActorId)))
            throw new InvalidDataException("Invalid board battle definition");
        if (b.Fighters.Select(f => (f.Side, f.Cell)).Distinct().Count() != b.Fighters.Count) throw new InvalidDataException("Occupied board cell" );
        if ((b.Winner is { } winner && !Enum.IsDefined(winner)) || b.Phase == BoardBattlePhase.Active && b.Winner is not null ||
            b.LastInitiative.Distinct(StringComparer.Ordinal).Count() != b.LastInitiative.Count ||
            b.LastInitiative.Any(id => !b.Fighters.Any(f => f.ActorId == id)) ||
            b.Orders.Select(o => o.ActorId).Distinct(StringComparer.Ordinal).Count() != b.Orders.Count ||
            b.Orders.Any(o => !b.Fighters.Any(f => f.ActorId == o.ActorId) || o.Order.BattleId != b.Id ||
                o.Order.Round != b.Round + 1 || o.Order.MoveTo is { InBounds: false }))
            throw new InvalidDataException("Invalid board battle turn state");
        if (w.Combats.Any(c => c.Status == CombatStatus.Active && b.Fighters.Any(f => f.ActorId == c.InitiatorId || f.ActorId == c.DefenderId || c.Allies?.Contains(f.ActorId) == true)) ||
            b.Phase == BoardBattlePhase.Active && w.Scheduled.Any(s => b.Fighters.Any(f => f.ActorId == s.Intent.ActorId)))
            throw new InvalidDataException("Board combatant is already busy");
    }
}


