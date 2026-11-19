using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ExpeditionIntegrity
{
    internal static void Validate(WorldState w)
    {
        if (w.PartyRules is not { } party)
        {
            if (w.Parties.Count > 0 || w.PartyInvitations.Count > 0 || w.PartyLoot.Count > 0 || w.Combats.Any(c => c.Allies is { Count: > 0 })) throw new InvalidDataException("Party state without rules");
        }
        else
        {
            if (!w.DemoMapEnabled || w.Dungeon is null || party.MaximumMembers is < 2 or > 3 || party.InvitationTicks <= 0 || party.MinimumHealth is < 1 or > 100) throw new InvalidDataException("Invalid party rules");
            var busy = new HashSet<string>();
            foreach (var p in w.Parties)
            {
                if (p.Members.Count < 2 || p.Members.Count > party.MaximumMembers || !p.Members.Contains(p.LeaderId) || p.Members.Any(id => !busy.Add(id)) ||
                    p.Members.Any(id => !w.Characters.Any(c => c.Id == id && !c.Hostile && c.OrganizationId is not null && c.OrganizationId == w.Characters.Single(a => a.Id == p.LeaderId).OrganizationId))) throw new InvalidDataException("Invalid party membership");
            }
            if (w.Parties.Select(p => p.Id).Distinct().Count() != w.Parties.Count || w.PartyInvitations.Select(v => v.Id).Distinct().Count() != w.PartyInvitations.Count ||
                w.PartyInvitations.Any(v => v.FromId == v.ToId || !w.Characters.Any(c => c.Id == v.FromId) || !w.Characters.Any(c => c.Id == v.ToId) || v.ExpiresTick < 0 || !Enum.IsDefined(v.Status))) throw new InvalidDataException("Invalid party invitation");
        }
        if (w.HuntRules is not { } hunts)
        {
            if (w.Hunts.Count > 0 || w.HuntReceipts.Count > 0) throw new InvalidDataException("Hunt state without rules");
        }
        else
        {
            if (w.SectFacilities is null || hunts.IntervalTicks <= 1 || hunts.RequiredKills is < 1 or > 1000000 || hunts.RewardPerKill is < 1 or > 1000000 || !w.Organizations.Any(o => o.Id == hunts.OrganizationId)) throw new InvalidDataException("Invalid hunt rules");
            if (w.Hunts.Select(h => h.Id).Distinct().Count() != w.Hunts.Count || w.Hunts.Any(h => h.OrganizationId != hunts.OrganizationId || h.Target is not (MonsterKind.Wolf or MonsterKind.Bandit) || h.Required <= 0 || h.Kills < 0 || h.Kills > h.Required || h.RewardPerKill <= 0 || h.DeadlineTick <= 0 || h.Participants.Distinct().Count() != h.Participants.Count || h.Participants.Any(id => !w.Characters.Any(c => c.Id == id)))) throw new InvalidDataException("Invalid hunt mission");
            if (w.HuntReceipts.Select(r => r.Id).Distinct().Count() != w.HuntReceipts.Count || w.HuntReceipts.Select(r => (r.CombatId, r.ActorId)).Distinct().Count() != w.HuntReceipts.Count) throw new InvalidDataException("Duplicate hunt reward");
            foreach (var group in w.HuntReceipts.GroupBy(r => (r.MissionId, r.CombatId)))
            {
                var h = w.Hunts.SingleOrDefault(h => h.Id == group.Key.MissionId); var b = w.Combats.SingleOrDefault(b => b.Id == group.Key.CombatId);
                if (h is null || b is null || b.Status != CombatStatus.Victory || b.WinnerId != b.InitiatorId ||
                    w.Characters.Single(c => c.Id == b.DefenderId).MonsterKind != h.Target || b.EndedTick >= h.DeadlineTick ||
                    !h.Participants.Any(PartyCombat.Participants(b).Contains) || group.Sum(r => r.Contribution) > h.RewardPerKill ||
                    group.Any(r => !PartyCombat.Participants(b).Contains(r.ActorId) || r.Tick != b.EndedTick || r.Contribution < 0 || r.Id != $"hunt-reward:{b.Id}:{r.ActorId}")) throw new InvalidDataException("Invalid hunt receipt backing");
            }
            if (w.Hunts.Any(h => h.Kills != w.HuntReceipts.Where(r => r.MissionId == h.Id).Select(r => r.CombatId).Distinct().LongCount())) throw new InvalidDataException("Hunt progress mismatch");
        }
        if (w.PartyLoot.Select(l => l.Id).Distinct().Count() != w.PartyLoot.Count || w.PartyLoot.Select(l => (l.CombatId, l.ToId, l.ResourceId)).Distinct().Count() != w.PartyLoot.Count) throw new InvalidDataException("Duplicate party loot");
        foreach (var l in w.PartyLoot)
        {
            var b = w.Combats.SingleOrDefault(b => b.Id == l.CombatId);
            if (b is null || b.Status != CombatStatus.Victory || b.WinnerId != b.InitiatorId || b.Allies is not { Count: > 0 } || l.FromId != b.DefenderId || !PartyCombat.Participants(b).Contains(l.ToId) || l.Tick != b.EndedTick || l.Quantity <= 0 ||
                !w.Ledger.Any(e => e.ActionId == l.Id && e.From == l.FromId && e.To == l.ToId && e.ResourceId == l.ResourceId && e.Quantity == l.Quantity && e.Reason == "PartyLoot")) throw new InvalidDataException("Invalid party loot backing");
            var monster = w.Characters.Single(c => c.Id == l.FromId);
            if (DungeonLoot.Definition(w, monster.MonsterKind, l.ResourceId) is null) throw new InvalidDataException("Invalid party monster loot type");
        }
        foreach (var group in w.PartyLoot.GroupBy(l => (l.CombatId, l.ResourceId)))
            if (group.Sum(l => l.Quantity) > DungeonLoot.Cap(w, w.Characters.Single(c => c.Id == group.First().FromId), group.Key.ResourceId)) throw new InvalidDataException("Party loot exceeds cap");
    }
}
