using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class PartyActions
{
    internal static PartyState? For(WorldState w, string actor) => w.Parties.SingleOrDefault(p => p.Members.Contains(actor));
    internal static bool Handles(ActionKind k) => k is ActionKind.InviteParty or ActionKind.AcceptParty or ActionKind.DeclineParty or ActionKind.LeaveParty or ActionKind.WaitParty;
    internal static bool Available(WorldState w, string actor) => CombatActions.Active(w, actor) is null && !w.Scheduled.Any(t => t.Intent.ActorId == actor);
    private static bool Nearby(CharacterState a, CharacterState b) => a.Id != b.Id && a.OrganizationId is not null && a.OrganizationId == b.OrganizationId && a.RegionId == b.RegionId && a.DemoLocationId == b.DemoLocationId && !a.Hostile && !b.Hostile;
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.PartyRules is not { } r || i.ResourceId != "party" || i.Quantity != 1 || i.DurationTicks != 0 || i.MissionId is not null || !Available(w, i.ActorId)) return "Party unavailable or actor busy";
        var a = w.Characters.Single(c => c.Id == i.ActorId); var party = For(w, a.Id);
        if (i.Kind == ActionKind.WaitParty) return party?.Id == i.TargetId && a.DemoLocationId == "dungeon" ? null : "No party to wait for";
        if (i.Kind == ActionKind.LeaveParty) return party?.Id == i.TargetId ? null : "No matching party";
        if (i.Kind == ActionKind.InviteParty)
        {
            var b = w.Characters.SingleOrDefault(c => c.Id == i.TargetId);
            return b is not null && Nearby(a, b) && a.Health >= r.MinimumHealth && b.Health >= r.MinimumHealth && Available(w, b.Id) && For(w, b.Id) is null &&
                (party is null || party.LeaderId == a.Id && party.Members.Count < r.MaximumMembers) &&
                !w.PartyInvitations.Any(v => v.FromId == a.Id && v.ToId == b.Id && v.ExpiresTick > w.Clock.Tick) && r.InvitationTicks <= long.MaxValue - w.Clock.Tick ? null : "Party invitation not feasible";
        }
        var invitation = w.PartyInvitations.SingleOrDefault(v => v.Id == i.TargetId && v.ToId == a.Id && v.Status == PartyInviteStatus.Pending && v.ExpiresTick > w.Clock.Tick);
        if (invitation is null) return "Invitation unavailable";
        if (i.Kind == ActionKind.DeclineParty) return null;
        var leader = w.Characters.Single(c => c.Id == invitation.FromId); var other = For(w, leader.Id);
        return party is null && Nearby(a, leader) && a.Health >= r.MinimumHealth && leader.Health >= r.MinimumHealth && Available(w, leader.Id) &&
            (other is null || other.LeaderId == leader.Id && other.Members.Count < r.MaximumMembers) ? null : "Party acceptance no longer feasible";
    }
    internal static void Execute(WorldState w, ActionIntent i)
    {
        if (i.Kind == ActionKind.WaitParty) { ActionExecutor.Emit(w, i.Id, "PartyWaiting", i.ActorId); return; }
        if (i.Kind == ActionKind.InviteParty) w.PartyInvitations.Add(new(i.Id, i.ActorId, i.TargetId, w.Clock.Tick + w.PartyRules!.InvitationTicks));
        else if (i.Kind == ActionKind.LeaveParty)
        {
            var p = For(w, i.ActorId)!; w.Parties.Remove(p);
            var remaining = p.Members.Where(id => id != i.ActorId).ToList();
            if (remaining.Count >= 2) w.Parties.Add(p with { Members = remaining, LeaderId = remaining.Contains(p.LeaderId) ? p.LeaderId : remaining[0] });
        }
        else
        {
            var v = w.PartyInvitations.Single(v => v.Id == i.TargetId);
            w.PartyInvitations[w.PartyInvitations.IndexOf(v)] = v with { Status = i.Kind == ActionKind.AcceptParty ? PartyInviteStatus.Accepted : PartyInviteStatus.Declined };
            if (i.Kind == ActionKind.AcceptParty)
            {
                var p = For(w, v.FromId);
                if (p is null) w.Parties.Add(new(v.Id, v.FromId, [v.FromId, i.ActorId]));
                else w.Parties[w.Parties.IndexOf(p)] = p with { Members = [.. p.Members, i.ActorId] };
            }
        }
        ActionExecutor.Emit(w, i.Id, "Party" + i.Kind, $"{i.ActorId}:{i.TargetId}");
    }
    internal static void Advance(WorldState w)
    {
        foreach (var v in w.PartyInvitations.Where(v => v.Status == PartyInviteStatus.Pending && v.ExpiresTick <= w.Clock.Tick).ToArray())
            w.PartyInvitations[w.PartyInvitations.IndexOf(v)] = v with { Status = PartyInviteStatus.Expired };
        foreach (var p in w.Parties.ToArray())
        {
            var leader = w.Characters.Single(c => c.Id == p.LeaderId);
            var members = p.Members.Where(id => w.Characters.Any(c => c.Id == id && c.OrganizationId is not null && c.OrganizationId == leader.OrganizationId && !c.Hostile)).ToList();
            if (members.Count == p.Members.Count) continue;
            w.Parties.Remove(p);
            if (members.Count >= 2) w.Parties.Add(p with { Members = members, LeaderId = members.Contains(p.LeaderId) ? p.LeaderId : members[0] });
            ActionExecutor.Emit(w, p.Id, "PartyMembershipChanged", string.Join(",", members));
        }
    }
    internal static string? ValidateExploration(WorldState w, string actor)
    {
        var p = For(w, actor);
        if (p is null) return null;
        return p.LeaderId == actor && p.Members.All(id => w.Characters.Any(c => c.Id == id && c.DemoLocationId == "dungeon" && c.RegionId == "valley" && c.Health >= w.PartyRules!.MinimumHealth) && Available(w, id)) ? null : "Wait for party leader and available healthy members at dungeon";
    }
}
