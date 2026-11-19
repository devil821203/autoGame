using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class HuntActions
{
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        var a = w.Characters.Single(c => c.Id == i.ActorId);
        return w.HuntRules is { } r && a.OrganizationId == r.OrganizationId && i.ResourceId == "combat" && i.Quantity == 1 && i.DurationTicks == 0 && i.MissionId is null &&
            PartyActions.Available(w, a.Id) && w.Hunts.Any(h => h.Id == i.TargetId && h.OrganizationId == a.OrganizationId && h.DeadlineTick > w.Clock.Tick && h.Kills < h.Required && !h.Participants.Contains(a.Id)) ? null : "Hunt mission unavailable";
    }
    internal static void Accept(WorldState w, ActionIntent i)
    {
        var h = w.Hunts.Single(h => h.Id == i.TargetId);
        w.Hunts[w.Hunts.IndexOf(h)] = h with { Participants = [.. h.Participants, i.ActorId] };
        ActionExecutor.Emit(w, i.Id, "HuntAccepted", $"{i.ActorId}:{h.Target}");
        EventDecisions.Request(w,i.ActorId,DecisionTrigger.StateChanged);
    }
    internal static void Advance(WorldState w)
    {
        if (w.HuntRules is not { } r || w.Hunts.Count > 0 && w.Clock.Tick % r.IntervalTicks != 0) return;
        foreach (var kind in new[] { MonsterKind.Wolf, MonsterKind.Bandit })
        {
            if (w.Hunts.Any(h => h.Target == kind && h.DeadlineTick > w.Clock.Tick && h.Kills < h.Required)) continue;
            if (r.IntervalTicks > long.MaxValue - w.Clock.Tick) continue;
            long deadline = w.Clock.Tick == 1 ? r.IntervalTicks : w.Clock.Tick + r.IntervalTicks;
            w.Hunts.Add(new($"hunt:{kind}:{w.Clock.Tick}", r.OrganizationId, kind, r.RequiredKills, r.RewardPerKill, deadline, 0, []));
            ActionExecutor.Emit(w, "hunt:" + w.Clock.Tick, "HuntPublished", kind.ToString());
            Notify(w,r.OrganizationId,[],includeObservers:true);
        }
    }
    internal static void Record(WorldState w, CombatEncounter battle)
    {
        if (w.HuntRules is null || battle.WinnerId != battle.InitiatorId || battle.Status != CombatStatus.Victory) return;
        var monster = w.Characters.Single(c => c.Id == battle.DefenderId);
        var fighters = PartyCombat.Participants(battle).Order(StringComparer.Ordinal).ToArray();
        var mission = w.Hunts.FirstOrDefault(h => h.Target == monster.MonsterKind && h.Kills < h.Required && h.DeadlineTick > w.Clock.Tick && h.Participants.Any(fighters.Contains));
        if (mission is null || w.HuntReceipts.Any(r => r.CombatId == battle.Id)) return;
        var recipients = fighters.Where(id => w.Characters.Any(c => c.Id == id && c.OrganizationId == mission.OrganizationId)).ToArray();
        if (recipients.Length == 0) return;
        var updated=mission with { Kills = mission.Kills + 1 };
        w.Hunts[w.Hunts.IndexOf(mission)] = updated;
        for (int n = 0; n < recipients.Length; n++)
        {
            string actor = recipients[n], id = $"hunt-reward:{battle.Id}:{actor}";
            long reward = mission.RewardPerKill / recipients.Length + (n < mission.RewardPerKill % recipients.Length ? 1 : 0);
            reward = Math.Min(reward, long.MaxValue - Inventory.Balance(w, actor, "contribution"));
            w.HuntReceipts.Add(new(id, mission.Id, battle.Id, actor, w.Clock.Tick, reward));
            if (reward > 0) ActionExecutor.Transfer(w, new(id, actor, actor, "contribution", reward, 0, DecisionSource.Npc), "$contribution", actor, "ContributionHuntReward");
        }
        ActionExecutor.Emit(w, battle.Id, "HuntProgress", $"{mission.Target}:{mission.Kills + 1}/{mission.Required}");
        Notify(w,updated.OrganizationId,updated.Participants,includeObservers:updated.Kills>=updated.Required);
    }
    private static void Notify(WorldState w,string organizationId,IEnumerable<string> participants,bool includeObservers)
    {
        if(w.Time?.EventDecisions is null)return;
        var recipients=participants.ToHashSet(StringComparer.Ordinal);
        // ExpeditionObservations currently exposes the board to members of this organization.
        // Partial progress concerns accepted contracts; completion also changes board availability.
        if(includeObservers)recipients.UnionWith(w.Characters.Where(a=>a.OrganizationId==organizationId&&!a.Hostile&&a.Health>0&&a.MonsterKind==MonsterKind.None).Select(a=>a.Id));
        foreach(string id in recipients.OrderBy(id=>id,StringComparer.Ordinal))
            EventDecisions.Request(w,id,DecisionTrigger.KnownNotification);
    }
}
