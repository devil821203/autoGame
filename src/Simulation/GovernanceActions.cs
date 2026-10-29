using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class GovernanceActions
{
    internal static bool Handles(ActionKind k) => k is ActionKind.SetOrganizationPolicy or ActionKind.ApplyToOrganization or ActionKind.LeaveOrganization or ActionKind.PromoteMember or ActionKind.DemoteMember or ActionKind.ExpelMember or ActionKind.RewardMember;
    internal static bool ValidPolicy(OrganizationPolicy p) => p.MaximumMembers is >= 1 and <= 10000 && p.MinimumReputation >= 0;
    internal static bool CanManageMissions(WorldState w, CharacterState a, OrganizationState o) => o.LeaderId == a.Id || a.OrganizationId == o.Id && a.Position == OrganizationPosition.Elder && w.OrganizationPolicies.Any(p => p.OrganizationId == o.Id);
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.SocialRules is null || i.DurationTicks != 0 || i.MissionId is not null || i.Quantity <= 0 || i.Kind != ActionKind.RewardMember && i.Quantity != 1 || i.ResourceId != (i.Kind == ActionKind.RewardMember ? "spirit-stone" : "organization")) return "Invalid governance action";
        var a = w.Characters.Single(c => c.Id == i.ActorId);
        string? orgId = i.Kind is ActionKind.SetOrganizationPolicy or ActionKind.ApplyToOrganization or ActionKind.LeaveOrganization ? i.TargetId : i.OrganizationId;
        var o = w.Organizations.SingleOrDefault(o => o.Id == orgId);
        if (o is null || o.RegionId != a.RegionId || w.Scheduled.Any(s => s.Intent.ActorId == a.Id)) return "No available local organization";
        var p = w.OrganizationPolicies.SingleOrDefault(p => p.OrganizationId == o.Id);
        if (i.Kind == ActionKind.ApplyToOrganization)
            return p is not null && p.OpenApplications && !a.Hostile && a.OrganizationId is null && a.Reputation >= p.MinimumReputation && w.Characters.Count(c => c.OrganizationId == o.Id) < p.MaximumMembers && w.Characters.Count(c=>c.OrganizationId==o.Id)<OrganizationCapacity.MemberLimit(w,o.Id) ? null : "Admission requirements not met";
        if (i.Kind == ActionKind.LeaveOrganization)
            return a.OrganizationId == o.Id && o.LeaderId != a.Id && a.Position != OrganizationPosition.Leader ? null : "Leader must arrange succession before leaving";
        if (o.LeaderId != a.Id) return "Only leader may govern";
        if (i.Kind == ActionKind.SetOrganizationPolicy)
            return i.Policy is { } next && next.OrganizationId == o.Id && ValidPolicy(next) ? null : "Invalid organization policy";
        var target = w.Characters.SingleOrDefault(c => c.Id == i.TargetId);
        if (target is null || target.Id == a.Id || target.OrganizationId != o.Id || target.Position == OrganizationPosition.Leader || target.RegionId != o.RegionId) return "Invalid local member";
        if (i.Kind == ActionKind.PromoteMember && target.Position != OrganizationPosition.Member || i.Kind == ActionKind.DemoteMember && target.Position != OrganizationPosition.Elder) return "Invalid appointment";
        if (i.Kind == ActionKind.RewardMember && (Inventory.Balance(w, o.Id, "spirit-stone") < i.Quantity || !InventoryCapacity.CanReceive(w, target.Id, "spirit-stone", i.Quantity))) return "Insufficient available reward funds";
        return null;
    }

    internal static void Execute(WorldState w, ActionIntent i)
    {
        if (i.Kind == ActionKind.SetOrganizationPolicy)
        {
            w.OrganizationPolicies.RemoveAll(p => p.OrganizationId == i.TargetId);
            w.OrganizationPolicies.Add(i.Policy!);
        }
        else if (i.Kind == ActionKind.RewardMember)
        {
            ActionExecutor.Transfer(w, i, i.OrganizationId!, i.TargetId, "OrganizationReward");
            SocialActions.Adjust(w, i.TargetId, i.ActorId, 2, 2, 0);
        }
        else
        {
            bool self = i.Kind is ActionKind.ApplyToOrganization or ActionKind.LeaveOrganization;
            int index = w.Characters.FindIndex(c => c.Id == (self ? i.ActorId : i.TargetId));
            var a = w.Characters[index];
            w.Characters[index] = i.Kind switch
            {
                ActionKind.ApplyToOrganization => a with { OrganizationId = i.TargetId, Position = OrganizationPosition.Member },
                ActionKind.LeaveOrganization or ActionKind.ExpelMember => a with { OrganizationId = null, Position = OrganizationPosition.None },
                ActionKind.PromoteMember => a with { Position = OrganizationPosition.Elder },
                _ => a with { Position = OrganizationPosition.Member }
            };
            if (i.Kind == ActionKind.ExpelMember) SocialActions.Adjust(w, i.TargetId, i.ActorId, -5, -5, 5);
            if (i.Kind is ActionKind.LeaveOrganization or ActionKind.ExpelMember) SectFacilityActions.ClearMembership(w, a.Id, i.Id);
        }
        ActionExecutor.Emit(w, i.Id, "Governance", $"{i.Kind}:{i.ActorId}:{i.TargetId}");
    }
}
