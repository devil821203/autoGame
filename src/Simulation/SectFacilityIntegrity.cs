using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SectFacilityIntegrity
{
    internal static void Validate(WorldState w)
    {
        var r = w.SectFacilities;
        if (r is null)
        {
            if (w.SectWorkAssignments.Count > 0 || w.SectWorkReceipts.Count > 0 || w.Ledger.Any(e => e.ResourceId == "contribution")) throw new InvalidDataException("Sect facility state without rules");
            return;
        }
        if (!w.DemoMapEnabled || !w.Organizations.Any(o => o.Id == r.OrganizationId) || !w.Organizations.Any(o => o.Id == "grove") || !w.Organizations.Any(o => o.Id == "consumed") ||
            r.ReservedPills is < 0 or > 1000000 || r.ReservedHerbs is < 0 or > 1000000 ||
            new[] { r.BrewContribution, r.GatherContribution, r.PillExchangePrice, r.HerbExchangePrice }.Any(v => v is < 1 or > 1000000)) throw new InvalidDataException("Invalid sect facility rules");
        if (w.SectWorkAssignments.Select(t => (t.ActorId, t.ProductId)).Distinct().Count() != w.SectWorkAssignments.Count ||
            w.SectWorkAssignments.Any(t => t.OrganizationId != r.OrganizationId || t.ProductId is not ("pill" or "herb") || !w.Characters.Any(c => c.Id == t.ActorId && c.OrganizationId == r.OrganizationId))) throw new InvalidDataException("Invalid sect work assignment");
        if (w.SectWorkReceipts.Select(t => t.Id).Distinct().Count() != w.SectWorkReceipts.Count || w.SectWorkReceipts.Any(t =>
            t.Tick < 0 || t.Tick > w.Clock.Tick || t.OrganizationId != r.OrganizationId || !w.Characters.Any(c => c.Id == t.ActorId) ||
            t.Kind is not (ActionKind.SectGather or ActionKind.SectBrew) || t.Quantity != (t.Success ? 1 : 0) || t.Contribution < 0 ||
            t.Contribution > (t.Kind == ActionKind.SectBrew ? r.BrewContribution : r.GatherContribution) || !t.Success && t.Contribution != 0)) throw new InvalidDataException("Invalid sect work receipt");
        if (w.Characters.Any(c => c.OrganizationId != r.OrganizationId && Inventory.Balance(w, c.Id, "contribution") != 0)) throw new InvalidDataException("Nonmember retained contribution");
        foreach (var task in w.Scheduled.Where(t => t.Intent.Kind is ActionKind.SectGather or ActionKind.SectBrew))
        {
            var i = task.Intent;
            if (i.ResourceId != "herb" || i.MissionId is not null || i.DurationTicks != 0 || i.Quantity != (i.Kind == ActionKind.SectBrew ? w.Rules.HerbsPerPill : 1) ||
                i.TargetId != (i.Kind == ActionKind.SectBrew ? "alchemy" : "grove") || !w.Characters.Any(c => c.Id == i.ActorId)) throw new InvalidDataException("Invalid sect work reservation");
        }
        if (w.Ledger.Any(e => e.ResourceId == "contribution" && !(e.From == "$contribution" && e.Reason is "ContributionPurchased" or "ContributionWorkReward" or "ContributionHuntReward" ||
            e.To == "consumed" && e.Reason is "ContributionSpent" or "ContributionCleared"))) throw new InvalidDataException("Invalid contribution transfer");
    }
}
