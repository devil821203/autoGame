using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class MissionActions
{
    internal static string Account(string id) => "$mission:" + id;
    internal static bool IsMission(ActionKind kind) => kind is ActionKind.PublishMission or ActionKind.AcceptMission or ActionKind.DeliverMission or ActionKind.CancelMission;

    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (i.DurationTicks != 0 || i.Quantity <= 0 || i.ResourceId is not ("pill" or "herb" or "beast-hide")) return "Invalid mission action";
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        var organization = w.Organizations.SingleOrDefault(o => o.Id == i.TargetId);
        if (organization is null || organization.RegionId != actor.RegionId) return "No local mission board";
        if (i.Kind == ActionKind.PublishMission)
        {
            var offer = i.Offer;
            if (!GovernanceActions.CanManageMissions(w, actor, organization)) return "Only authorized organization officers may publish";
            if (offer is null || string.IsNullOrWhiteSpace(offer.Id) || offer.Id.StartsWith('$') || w.Missions.Any(m => m.Id == offer.Id) ||
                offer.Quantity <= 0 || offer.UnitPrice <= 0 || offer.DeadlineTick <= w.Clock.Tick || offer.MaximumBudget <= 0) return "Invalid mission offer";
            try
            {
                long budget = checked(offer.Quantity * offer.UnitPrice);
                if (budget > offer.MaximumBudget || Inventory.Balance(w, organization.Id, "spirit-stone") < budget) return "Insufficient mission budget";
                if (!InventoryCapacity.CanReceive(w, organization.Id, i.ResourceId, offer.Quantity)) return "Mission inventory capacity exceeded";
            }
            catch (OverflowException) { return "Mission budget overflow"; }
            return null;
        }
        var mission = w.Missions.SingleOrDefault(m => m.Id == i.MissionId);
        if (mission is null || mission.OrganizationId != organization.Id || mission.ResourceId != i.ResourceId) return "Unknown mission";
        if (mission.Status != MissionStatus.Active || w.Clock.Tick >= mission.DeadlineTick) return "Mission unavailable or expired";
        if (i.Kind == ActionKind.CancelMission) return GovernanceActions.CanManageMissions(w, actor, organization) ? null : "Only authorized organization officers may cancel";
        if (mission.MembersOnly && actor.OrganizationId != organization.Id) return "Mission requires membership";
        if (w.Scheduled.Any(t => t.Intent.ActorId == actor.Id)) return "Actor busy";
        if (i.Kind == ActionKind.AcceptMission) return mission.Participants.Contains(actor.Id) ? "Already accepted" : null;
        if (!mission.Participants.Contains(actor.Id)) return "Mission not accepted";
        long quantity = Math.Min(i.Quantity, mission.Required - mission.Delivered);
        if (quantity <= 0 || Inventory.Balance(w, actor.Id, mission.ResourceId) < quantity) return "Insufficient mission goods";
        long payment = checked(quantity * mission.UnitPrice);
        if (Inventory.Balance(w, Account(mission.Id), "spirit-stone") < payment) return "Missing mission reservation";
        if (!InventoryCapacity.CanReceive(w, actor.Id, "spirit-stone", payment) || !InventoryCapacity.CanReceive(w, organization.Id, mission.ResourceId, quantity)) return "Delivery capacity exceeded";
        return null;
    }

    internal static void Execute(WorldState w, ActionIntent i)
    {
        if (i.Kind == ActionKind.PublishMission)
        {
            var offer = i.Offer!;
            var organization = w.Organizations.Single(o => o.Id == i.TargetId);
            long budget = checked(offer.Quantity * offer.UnitPrice);
            w.Missions.Add(new(offer.Id, organization.Id, organization.RegionId, i.ResourceId, offer.Quantity,
                offer.UnitPrice, offer.DeadlineTick, budget, 0, MissionStatus.Active, [], w.OrganizationPolicies.SingleOrDefault(p => p.OrganizationId == organization.Id)?.MissionsMembersOnly ?? false));
            ActionExecutor.Transfer(w, i with { ResourceId = "spirit-stone", Quantity = budget }, organization.Id, Account(offer.Id), "MissionReserved");
            ActionExecutor.Emit(w, i.Id, "MissionPublished", offer.Id);
            return;
        }
        int index = w.Missions.FindIndex(m => m.Id == i.MissionId);
        var mission = w.Missions[index];
        if (i.Kind == ActionKind.AcceptMission)
        {
            w.Missions[index] = mission with { Participants = mission.Participants.Append(i.ActorId).ToList() };
            ActionExecutor.Emit(w, i.Id, "MissionAccepted", $"{i.ActorId}:{mission.Id}");
            return;
        }
        if (i.Kind == ActionKind.CancelMission) { Close(w, mission, MissionStatus.Cancelled, i.Id); return; }
        long quantity = Math.Min(i.Quantity, mission.Required - mission.Delivered);
        long payment = checked(quantity * mission.UnitPrice);
        ActionExecutor.Transfer(w, i with { Quantity = quantity }, i.ActorId, mission.OrganizationId, "MissionDelivery");
        ActionExecutor.Transfer(w, i with { Quantity = payment, ResourceId = "spirit-stone" }, Account(mission.Id), i.ActorId, "MissionPayment");
        w.Deliveries.Add(new(i.Id, mission.Id, w.Clock.Tick, i.ActorId, quantity, payment));
        var leader = w.Organizations.Single(o => o.Id == mission.OrganizationId).LeaderId;
        if (leader is not null) SocialActions.Adjust(w, leader, i.ActorId, 1, 1, 0);
        if (w.SocialRules is { } social)
        {
            if (leader is not null) SocialActions.Adjust(w, i.ActorId, leader, 1, 1, 0);
            int actorIndex = w.Characters.FindIndex(c => c.Id == i.ActorId);
            var actor = w.Characters[actorIndex];
            long headroom = long.MaxValue - actor.Reputation;
            long gain = social.MissionReputationPerItem == 0 ? 0 :
                quantity > headroom / social.MissionReputationPerItem ? headroom : quantity * social.MissionReputationPerItem;
            w.Characters[actorIndex] = actor with { Reputation = actor.Reputation + gain };
            ActionExecutor.Emit(w, i.Id, "MissionReputation", $"{i.ActorId}:{mission.Id}:+{gain}");
        }
        w.Memories.Add(new(i.ActorId, w.Clock.Tick, i.Kind, true, i.ResourceId, payment, -quantity, $"board:{mission.OrganizationId}:{mission.RegionId}"));
        long delivered = mission.Delivered + quantity;
        w.Missions[index] = mission with { Delivered = delivered, Status = delivered == mission.Required ? MissionStatus.Completed : MissionStatus.Active };
        ActionExecutor.Emit(w, i.Id, "MissionDelivered", $"{i.ActorId}:{mission.Id}:{quantity}@{mission.UnitPrice}");
    }

    internal static void Expire(WorldState w)
    {
        foreach (var mission in w.Missions.Where(m => m.Status == MissionStatus.Active && m.DeadlineTick <= w.Clock.Tick).ToArray())
            Close(w, mission, MissionStatus.Expired, "expire:" + mission.Id);
    }

    private static void Close(WorldState w, MissionState mission, MissionStatus status, string actionId)
    {
        long refund = mission.ReservedBudget - checked(mission.Delivered * mission.UnitPrice);
        if (refund > 0)
            ActionExecutor.Transfer(w, new(actionId, mission.OrganizationId, mission.OrganizationId, "spirit-stone", refund, 0, DecisionSource.Npc), Account(mission.Id), mission.OrganizationId, "MissionRefund");
        w.Missions[w.Missions.FindIndex(m => m.Id == mission.Id)] = mission with { Status = status };
        ActionExecutor.Emit(w, actionId, "Mission" + status, mission.Id);
    }
}
