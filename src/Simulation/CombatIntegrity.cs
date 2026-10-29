using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class CombatIntegrity
{
    internal static void Validate(WorldState w)
    {
        var r = w.CombatRules;
        if (w.Characters.Any(c => !double.IsFinite(c.CombatDrive) || c.CombatDrive is < 0 or > 10 || c.CombatExperience < 0)) throw new InvalidDataException("Invalid combat traits.");
        if (r is null)
        {
            if (w.Combats.Count > 0 || w.Loot.Count > 0) throw new InvalidDataException("Combat state without rules.");
            return;
        }
        if (r.RoundTicks <= 0 || r.MaximumRounds is < 1 or > 1000 || r.DamageVariance is < 0 or > 1000 || r.PowerMultiplier is < 1 or > 10 ||
            !double.IsFinite(r.RetreatChance) || r.RetreatChance is < 0 or > 1 || r.MinimumEngageHealth is < 1 or > 100 || r.MaximumLootStones < 0 || r.VictoryExperience < 0 || !w.Organizations.Any(o => o.Id == "consumed")) throw new InvalidDataException("Invalid combat rules.");
        if (w.Combats.Select(c => c.Id).Distinct().Count() != w.Combats.Count) throw new InvalidDataException("Duplicate combat ID.");
        var busy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in w.Combats)
        {
            var a = w.Characters.SingleOrDefault(a => a.Id == c.InitiatorId); var b = w.Characters.SingleOrDefault(a => a.Id == c.DefenderId);
            if (a is null || b is null || a.Id == b.Id || !Enum.IsDefined(c.Status) || c.StartedTick < 0 || c.StartedTick > w.Clock.Tick || c.Rounds < 0 || c.Rounds > r.MaximumRounds || c.InitiatorCommandTick > w.Clock.Tick || c.DefenderCommandTick > w.Clock.Tick) throw new InvalidDataException("Invalid combat participants or time.");
            if (c.Allies is { Count: > 0 })
            {
                var team = new[] { c.InitiatorId }.Concat(c.Allies).ToArray();
                if (w.PartyRules is null || team.Length > w.PartyRules.MaximumMembers || team.Distinct().Count() != team.Length || team.Contains(c.DefenderId) ||
                    team.Any(id => !w.Characters.Any(ch => ch.Id == id)) || c.AllyCommandTick > w.Clock.Tick ||
                    new[] { c.PreparedAllies, c.Withdrawn, c.CommandedAllies }.Any(list => list is not null && (list.Distinct().Count() != list.Count || list.Any(id => !team.Contains(id))))) throw new InvalidDataException("Invalid team combat history");
            }
            if (c.Status == CombatStatus.Active)
            {
                if (c.Allies is { Count: > 0 })
                {
                    if (c.Allies.Count > 2 || c.Allies.Distinct().Count() != c.Allies.Count || c.Allies.Contains(a.Id) || c.Allies.Contains(b.Id) || c.Allies.Any(id => !w.Characters.Any(ch => ch.Id == id))) throw new InvalidDataException("Invalid team combat participants");
                    var participants = PartyCombat.Participants(c).Append(b.Id).ToArray();
                    if (c.EndedTick is not null || c.WinnerId is not null || c.NextRoundTick <= w.Clock.Tick || c.Rounds >= r.MaximumRounds ||
                        participants.Any(id => !busy.Add(id) || w.Scheduled.Any(t => t.Intent.ActorId == id) || !w.Characters.Any(ch => ch.Id == id && ch.RegionId == b.RegionId && ch.DemoLocationId == b.DemoLocationId))) throw new InvalidDataException("Invalid active team combat");
                    continue;
                }
                if (c.EndedTick is not null || c.WinnerId is not null || c.Rounds >= r.MaximumRounds || c.NextRoundTick <= w.Clock.Tick && !RemoteCombat.ValidWaitingRound(w, c) || a.RegionId != b.RegionId || a.Health <= 0 || b.Health <= 0 || !busy.Add(a.Id) || !busy.Add(b.Id) || w.Scheduled.Any(t => t.Intent.ActorId == a.Id || t.Intent.ActorId == b.Id)) throw new InvalidDataException("Invalid active combat.");
            }
            else if (c.EndedTick is null || c.EndedTick < c.StartedTick || c.EndedTick > w.Clock.Tick ||
                (c.Status == CombatStatus.Victory ? c.WinnerId != a.Id && c.WinnerId != b.Id : c.WinnerId is not null)) throw new InvalidDataException("Invalid combat result.");
        }
        if (w.Loot.Select(l => (l.CombatId, l.ResourceId)).Distinct().Count() != w.Loot.Count) throw new InvalidDataException("Duplicate combat loot.");
        var ledger = w.Ledger.Where(e => e.Reason == "CombatLoot").ToLookup(e => e.ActionId);
        foreach (var loot in w.Loot)
        {
            var c = w.Combats.SingleOrDefault(c => c.Id == loot.CombatId);
            if (c is null || c.Status != CombatStatus.Victory || c.WinnerId != loot.WinnerId || loot.LoserId != (c.InitiatorId == loot.WinnerId ? c.DefenderId : c.InitiatorId) || loot.Tick != c.EndedTick || loot.Quantity <= 0 ||
                loot.Quantity > DungeonLoot.Cap(w, w.Characters.Single(ch => ch.Id == loot.LoserId), loot.ResourceId) ||
                !ledger["$combat-loot:" + c.Id].Any(e => e.From == loot.LoserId && e.To == loot.WinnerId && e.ResourceId == loot.ResourceId && e.Quantity == loot.Quantity)) throw new InvalidDataException("Invalid loot receipt.");
        }
    }
}
