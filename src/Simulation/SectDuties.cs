using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SectDuties
{
    internal static void Advance(WorldState w)
    {
        if (w.SectDuty is not { } r) return;
        var org = w.Organizations.Single(o => o.Id == r.OrganizationId);
        var members = w.Characters.Where(c => c.OrganizationId == org.Id && c.Id != org.LeaderId && c.Position is OrganizationPosition.Member or OrganizationPosition.Elder).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var ids = members.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        w.MemberDuties.RemoveAll(d => d.OrganizationId == org.Id && !ids.Contains(d.ActorId));
        foreach (var member in members)
        {
            var duty = w.MemberDuties.SingleOrDefault(d => d.ActorId == member.Id && d.OrganizationId == org.Id);
            if (duty is null) { w.MemberDuties.Add(new(org.Id, member.Id, w.Clock.Tick, w.Clock.Tick)); continue; }
            if (w.Clock.Tick - duty.LastReviewTick < r.ReviewIntervalTicks) continue;
            bool fulfilled = w.Deliveries.Any(d => d.ActorId == member.Id && d.Tick > duty.LastReviewTick && d.Tick <= w.Clock.Tick && d.Quantity > 0 && w.Missions.Any(m => m.Id == d.MissionId && m.OrganizationId == org.Id));
            fulfilled |= w.SectWorkReceipts.Any(d => d.ActorId == member.Id && d.OrganizationId == org.Id && d.Success && d.Quantity > 0 && d.Tick > duty.LastReviewTick && d.Tick <= w.Clock.Tick);
            fulfilled |= w.HuntReceipts.Any(d => d.ActorId == member.Id && d.Contribution > 0 && d.Tick > duty.LastReviewTick && d.Tick <= w.Clock.Tick && w.Hunts.Any(h => h.Id == d.MissionId && h.OrganizationId == org.Id));
            int misses = fulfilled ? 0 : duty.Misses + 1;
            w.MemberDuties.Remove(duty);
            if (misses >= r.MissLimit)
            {
                int index = w.Characters.FindIndex(c => c.Id == member.Id);
                w.Characters[index] = member with { OrganizationId = null, Position = OrganizationPosition.None };
                SectFacilityActions.ClearMembership(w, member.Id, "duty-expel:" + w.Clock.Tick);
                w.Plans.RemoveAll(p => p.ActorId == member.Id);
                ActionExecutor.Emit(w, member.Id, "DutyExpelled", $"{org.Id}:{misses} consecutive missed reviews");
            }
            else w.MemberDuties.Add(duty with { LastReviewTick = w.Clock.Tick, Misses = misses });
            ActionExecutor.Emit(w, member.Id, "DutyReviewed", $"{org.Id}:fulfilled={fulfilled},misses={misses}");
        }
        if (w.Clock.Tick % r.MissionIntervalTicks != 0 || org.LeaderId is null) return;
        Publish(w.MedicineOnly ? "herb" : "pill", r.PillQuantity, r.PillReward); Publish("beast-hide", r.HideQuantity, r.HideReward);
        void Publish(string item, long quantity, long reward)
        {
            if (w.Missions.Any(m => m.OrganizationId == org.Id && m.ResourceId == item && m.Status == MissionStatus.Active)) return;
            string id = $"sect-auto:{org.Id}:{item}:{w.Clock.Tick}";
            long available = Inventory.Balance(w, org.Id, "spirit-stone");
            quantity = Math.Min(quantity, available / reward);
            long lifetime = r.MissionLifetimeTicks > 0 ? r.MissionLifetimeTicks : r.MissionIntervalTicks;
            if (quantity == 0 || lifetime > long.MaxValue - w.Clock.Tick) { ActionExecutor.Emit(w, id, "AutoMissionSkipped", "Insufficient funds or time"); return; }
            var intent = new ActionIntent(id, org.LeaderId!, org.Id, item, quantity, 0, DecisionSource.Npc, ActionKind.PublishMission,
                Offer: new(id, quantity, reward, w.Clock.Tick + lifetime, quantity * reward));
            string? error = MissionActions.Validate(w, intent);
            if (error is null) { MissionActions.Execute(w, intent); ActionExecutor.Emit(w, id, "AutoMissionPublished", item); }
            else ActionExecutor.Emit(w, id, "AutoMissionSkipped", error);
        }
    }
}
