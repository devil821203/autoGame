using Cultivation.Domain;

namespace Cultivation.AI;

// Public local governance notice and this actor's own relationship with its leader.
public sealed record OrganizationObservation(OrganizationPolicy Policy, string RegionId, int Members,
    int LeaderTrust, int LeaderGrudge, string Source, long ObservedTick, bool IsLeader = false,
    long MembershipPillReward = 0, long MembershipDeadlineTick = long.MaxValue);
public sealed record RecruitmentContact(string Id, long Reputation, int Trust, int Grudge, string Source, long ObservedTick);

internal static class OrganizationUtility
{
    internal static IEnumerable<ScoredAction> Evaluate(LocalObservation o)
    {
        var a = o.Actor;
        if (o.SocialRules is null || a.Hostile) yield break;
        foreach (var notice in o.Organizations ?? [])
        {
            var p = notice.Policy;
            if (notice.RegionId != a.RegionId || notice.Source != $"admission:{p.OrganizationId}:{a.RegionId}" ||
                notice.ObservedTick != o.Tick) continue;
            if (notice.IsLeader && a.OrganizationId == p.OrganizationId && a.SocialCooldownUntil <= o.Tick &&
                a.Health > 0 && a.Health < o.SocialRules.SuccessionHealthThreshold)
            {
                foreach (var contact in o.SuccessorContacts ?? [])
                {
                    if (contact.Id == a.Id || contact.Trust <= 0 || contact.Source != $"successor-contact:{a.Id}:{a.RegionId}" || contact.ObservedTick != o.Tick) continue;
                    double value = (o.SocialRules.SuccessionHealthThreshold - a.Health) * a.Caution +
                        contact.Trust / 10.0 - contact.Grudge / 2.0 - a.SocialWeight * a.SectAffinity * 10;
                    if (value > 0)
                        yield return new(new($"ai:{a.Id}:{o.Tick}", a.Id, contact.Id, "social", 1, 0,
                            DecisionSource.Npc, ActionKind.InviteSuccessor), value,
                            $"健康低於繼任評估門檻，衡量風險與歸屬後提名已知可信成員 {contact.Id}；須對方同意");
                }
            }
            if (notice.IsLeader && a.OrganizationId == p.OrganizationId && notice.Members < p.MaximumMembers && a.SocialCooldownUntil <= o.Tick)
            {
                foreach (var contact in o.RecruitmentContacts ?? [])
                {
                    if (contact.Id == a.Id || contact.Source != $"recruitment-contact:{a.Id}:{a.RegionId}" ||
                        contact.ObservedTick != o.Tick || contact.Reputation < p.MinimumReputation) continue;
                    double value = a.SocialWeight * a.SectAffinity * 10 + contact.Trust / 10.0 - contact.Grudge / 2.0 - a.Caution;
                    if (value > 0)
                        yield return new(new($"ai:{a.Id}:{o.Tick}", a.Id, contact.Id, "social", 1, 0,
                            DecisionSource.Npc, ActionKind.InviteRecruit), value,
                            $"招募已知同區角色 {contact.Id}：符合聲望與名額，衡量社交、宗門偏好與自身信任；邀請不等於加入");
                }
            }
            double attachment = a.SocialWeight * a.SectAffinity * 10 + notice.LeaderTrust / 10.0 - notice.LeaderGrudge / 2.0;
            if (a.OrganizationId is not null)
            {
                if (a.OrganizationId != p.OrganizationId || notice.IsLeader || a.Position == OrganizationPosition.Leader) continue;
                double exitScore = -attachment - a.Caution;
                if (exitScore > 0)
                    yield return new(new($"ai:{a.Id}:{o.Tick}", a.Id, p.OrganizationId, "organization", 1, 0,
                        DecisionSource.Npc, ActionKind.LeaveOrganization), exitScore,
                        $"本地組織 {p.OrganizationId}：自身對領導者的不滿超過歸屬與信任，衡量退出風險並與其他行動比較");
                continue;
            }
            if (!p.OpenApplications || notice.Members >= p.MaximumMembers || a.Reputation < p.MinimumReputation) continue;
            // Public terms are an opportunity, not reserved admission or guaranteed work.
            double opportunity = 0;
            double remaining = (double)notice.MembershipDeadlineTick - o.Tick;
            void Supply(double cost, double ticks, double risk = 0)
            {
                if (ticks >= remaining) return;
                double value = ((notice.MembershipPillReward - cost) * a.WealthWeight - risk) / ticks;
                opportunity = Math.Max(opportunity, value);
            }
            if (o.Pills > 0) Supply(o.PillSellPrice ?? o.Rules.PillPrice, 3);
            if (o.ShopPills > 0 && o.Stones >= o.Rules.PillPrice) Supply(o.Rules.PillPrice, 4);
            long missing = Math.Max(0, o.Rules.HerbsPerPill - o.Herbs);
            if (a.AlchemyChance > 0 && (missing == 0 || o.ShopHerbs >= missing && missing <= o.Stones / o.Rules.HerbPrice))
            {
                double chance = CharacterDevelopment.SuccessChance(a, o.Rules);
                double materialCost = (double)o.Rules.HerbsPerPill * o.Rules.HerbPrice;
                Supply(materialCost / chance, 3 + (o.Rules.BrewTicks + (double)missing) / chance,
                    (1 - chance) * a.Caution * (1 + o.RecentFailures) * materialCost);
            }
            double score = attachment - a.Caution + opportunity;
            if (score <= 0) continue;
            yield return new(new($"ai:{a.Id}:{o.Tick}", a.Id, p.OrganizationId, "organization", 1, 0,
                DecisionSource.Npc, ActionKind.ApplyToOrganization), score,
                $"本地入門公告 {p.OrganizationId}：符合聲望與名額；比較任務報酬 {notice.MembershipPillReward}、持有／購丹／煉丹原料成本、成功率、失敗風險與期限；任務不保留名額");
        }
    }
}
