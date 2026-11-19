using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SectFacilityActions
{
    internal static bool Handles(ActionKind kind) => kind is ActionKind.AcceptSectWork or ActionKind.SectGather or ActionKind.SectBrew or ActionKind.BuyContribution or ActionKind.RedeemContribution or ActionKind.SetSectStockReservation;
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        var r = w.SectFacilities;
        var a = w.Characters.Single(c => c.Id == i.ActorId);
        if (r is null || a.OrganizationId != r.OrganizationId || i.Quantity <= 0 || i.DurationTicks != 0 || i.MissionId is not null ||
            !w.Organizations.Any(o => o.Id == r.OrganizationId && o.RegionId == a.RegionId) || w.Scheduled.Any(s => s.Intent.ActorId == a.Id)) return "Sect facilities require available membership";
        if (i.Kind == ActionKind.SetSectStockReservation)
            return i.TargetId == r.OrganizationId && i.ResourceId == "sect-stock" && i.Quantity == 1 &&
                w.Organizations.Any(o => o.Id == r.OrganizationId && o.LeaderId == a.Id) && i.StockReservation is { Pills: >= 0 and <= 1000000, Herbs: >= 0 and <= 1000000 } ? null : "Only leader may set valid exchange stock reserves";
        if (i.Kind == ActionKind.AcceptSectWork)
            return i.TargetId == r.OrganizationId && i.Quantity == 1 && i.ResourceId is "pill" or "herb" &&
                !w.SectWorkAssignments.Any(t => t.ActorId == a.Id && t.ProductId == i.ResourceId) && (i.ResourceId != "pill" || a.AlchemyChance > 0) ? null : "Work already accepted or unavailable";
        if (i.Kind is ActionKind.SectGather or ActionKind.SectBrew)
        {
            bool brew = i.Kind == ActionKind.SectBrew;
            long duration = brew ? w.Rules.BrewTicks : w.Rules.GatherTicks;
            if (i.Quantity != 1 || i.ResourceId != "herb" || i.TargetId != (brew ? "alchemy" : "grove") || duration > long.MaxValue - w.Clock.Tick ||
                !w.SectWorkAssignments.Any(t => t.ActorId == a.Id && t.OrganizationId == r.OrganizationId && t.ProductId == (brew ? "pill" : "herb"))) return "Accept corresponding sect work before using facilities";
            if (brew && a.AlchemyChance <= 0 || !brew && a.Health < 30) return "Insufficient ability or safe health";
            return Inventory.Balance(w, brew ? r.OrganizationId : "grove", "herb") - (brew ? r.ReservedHerbs : 0) >= (brew ? w.Rules.HerbsPerPill : 1) &&
                InventoryCapacity.CanReceive(w, r.OrganizationId, brew ? "pill" : "herb", 1) ? null : "Insufficient sect materials, herbs or capacity";
        }
        if (i.TargetId != r.OrganizationId) return "Invalid exchange office";
        if (i.Kind == ActionKind.BuyContribution)
            return i.ResourceId == "spirit-stone" && Inventory.Balance(w, a.Id, "spirit-stone") >= i.Quantity &&
                InventoryCapacity.CanReceive(w, r.OrganizationId, "spirit-stone", i.Quantity) && InventoryCapacity.CanReceive(w, a.Id, "contribution", i.Quantity) ? null : "Insufficient stones or contribution capacity";
        if (i.ResourceId != w.RecoveryResourceId && i.ResourceId != "herb") return "Contribution cannot be exchanged for stones";
        long price = i.ResourceId == w.RecoveryResourceId ? r.PillExchangePrice : r.HerbExchangePrice;
        return i.Quantity <= long.MaxValue / price && Inventory.Balance(w, a.Id, "contribution") >= i.Quantity * price &&
            Inventory.Balance(w, r.OrganizationId, i.ResourceId) >= i.Quantity && InventoryCapacity.CanReceive(w, a.Id, i.ResourceId, i.Quantity) ? null : "Insufficient contribution, stock or capacity";
    }

    internal static void Execute(WorldState w, ActionIntent i)
    {
        var r = w.SectFacilities!;
        if (i.Kind == ActionKind.SetSectStockReservation)
        {
            w.SectFacilities = r with { ReservedPills = i.StockReservation!.Pills, ReservedHerbs = i.StockReservation.Herbs };
            ActionExecutor.Emit(w, i.Id, "SectStockReservationChanged", $"pills={i.StockReservation.Pills},herbs={i.StockReservation.Herbs}"); return;
        }
        if (i.Kind == ActionKind.AcceptSectWork)
        {
            w.SectWorkAssignments.Add(new(i.ActorId, r.OrganizationId, i.ResourceId));
            ActionExecutor.Emit(w, i.Id, "SectWorkAccepted", $"{i.ActorId}:{i.ResourceId}:unlimited"); return;
        }
        if (i.Kind is ActionKind.SectGather or ActionKind.SectBrew)
        {
            bool brew = i.Kind == ActionKind.SectBrew;
            var reserved = i with { Quantity = brew ? w.Rules.HerbsPerPill : 1 };
            ActionExecutor.Transfer(w, reserved, brew ? r.OrganizationId : "grove", ActionExecutor.Escrow(i.Id), "Reserved");
            w.Scheduled.Add(new(w.NextSequence++, w.Clock.Tick + (brew ? w.Rules.BrewTicks : w.Rules.GatherTicks), reserved));
            ActionExecutor.Emit(w, i.Id, "SectWorkScheduled", $"{i.ActorId}:{i.Kind}"); return;
        }
        if (i.Kind == ActionKind.BuyContribution)
        {
            ActionExecutor.Transfer(w, i, i.ActorId, r.OrganizationId, "ContributionPurchasePayment");
            ActionExecutor.Transfer(w, i with { ResourceId = "contribution" }, "$contribution", i.ActorId, "ContributionPurchased");
        }
        else
        {
            long price = i.ResourceId == w.RecoveryResourceId ? r.PillExchangePrice : r.HerbExchangePrice;
            ActionExecutor.Transfer(w, i with { ResourceId = "contribution", Quantity = i.Quantity * price }, i.ActorId, "consumed", "ContributionSpent");
            ActionExecutor.Transfer(w, i, r.OrganizationId, i.ActorId, "ContributionRedemption");
        }
        ActionExecutor.Emit(w, i.Id, "ContributionExchange", $"{i.ActorId}:{i.Kind}:{i.Quantity} {i.ResourceId}");
    }

    internal static void Complete(WorldState w, ActionIntent i)
    {
        var r = w.SectFacilities!; bool brew = i.Kind == ActionKind.SectBrew;
        int index = w.Characters.FindIndex(c => c.Id == i.ActorId); var actor = w.Characters[index];
        bool success = true;
        if (brew)
        {
            ActionExecutor.Transfer(w, i, ActionExecutor.Escrow(i.Id), "consumed", "Consumed");
            var rng = new SeededRandom(w.RandomState);
            success = rng.NextUInt32() / ((double)uint.MaxValue + 1) < CharacterDevelopment.SuccessChance(actor, w.Rules);
            w.RandomState = rng.State;
            success &= InventoryCapacity.CanReceive(w, r.OrganizationId, "pill", 1, i.Id);
            if (success) ActionExecutor.Transfer(w, i with { ResourceId = "pill", Quantity = 1 }, "$alchemy", r.OrganizationId, "Brewed");
            w.Characters[index] = actor with { AlchemyExperience = CharacterDevelopment.GainExperience(actor.AlchemyExperience, StaminaCalculator.Growth(w, actor, w.Rules.ExperiencePerAction)) };
        }
        else
        {
            ActionExecutor.Transfer(w, i, ActionExecutor.Escrow(i.Id), r.OrganizationId, "SectHerbsGathered");
            w.Characters[index] = actor with { GatheringExperience = CharacterDevelopment.GainExperience(actor.GatheringExperience, StaminaCalculator.Growth(w, actor, w.Rules.ExperiencePerAction)) };
        }
        long points = success && actor.OrganizationId == r.OrganizationId ? brew ? r.BrewContribution : r.GatherContribution : 0;
        points = Math.Min(points, long.MaxValue - Inventory.Balance(w, actor.Id, "contribution"));
        w.SectWorkReceipts.Add(new(i.Id, w.Clock.Tick, actor.Id, r.OrganizationId, i.Kind, success, success ? 1 : 0, points));
        if (points > 0) ActionExecutor.Transfer(w, i with { ResourceId = "contribution", Quantity = points }, "$contribution", actor.Id, "ContributionWorkReward");
        ActionExecutor.Emit(w, i.Id, success ? "SectWorkCompleted" : "SectWorkFailed", $"{actor.Id}:{i.Kind}:contribution={points}");
    }

    internal static void ClearMembership(WorldState w, string actorId, string actionId)
    {
        long points = Inventory.Balance(w, actorId, "contribution");
        if (points > 0)
            ActionExecutor.Transfer(w, new(actionId + ":clear-contribution", actorId, "consumed", "contribution", points, 0, DecisionSource.Npc), actorId, "consumed", "ContributionCleared");
        w.SectWorkAssignments.RemoveAll(t => t.ActorId == actorId);
        w.MemberDuties.RemoveAll(t => t.ActorId == actorId);
    }
}

