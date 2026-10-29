using Cultivation.Domain;

namespace Cultivation.AI;

public sealed record HuntNotice(string Id, MonsterKind Target, long Reward, bool Accepted);
public sealed record ExpeditionObservation(string OrganizationId, string Source, long Tick, string? PartyId, string? LeaderId,
    int PartySize, int MaximumMembers, bool PartyReady, IReadOnlyList<string> Contacts, string? InvitationId, IReadOnlyList<HuntNotice> Hunts);

internal static class ExpeditionUtility
{
    internal static IEnumerable<ScoredAction> Evaluate(LocalObservation o)
    {
        if (o.Expedition is not { } e || e.Tick != o.Tick || e.OrganizationId != o.Actor.OrganizationId || e.Source != "expedition:" + e.OrganizationId || o.Battle is not null) yield break;
        var a = o.Actor;
        ScoredAction Choice(ActionKind k, string target, string item, double value, string reason) => new(new($"ai:{a.Id}:{o.Tick}", a.Id, target, item, 1, 0, DecisionSource.Npc, k), value, reason);
        if (a.Health >= 50)
        {
            if (e.InvitationId is { } invitation && e.PartyId is null)
                yield return Choice(ActionKind.AcceptParty, invitation, "party", (a.Aspiration == Aspiration.Combat ? 45 : 2) + a.CombatDrive * 5 + a.Caution * 5, "自願接受同門邀請，以多人火力與分攤受傷降低副本風險");
            if ((e.PartyId is null || e.LeaderId == a.Id && e.PartySize < e.MaximumMembers) && e.Contacts.Count > 0 && (a.Aspiration == Aspiration.Combat || a.CombatDrive >= 1))
                yield return Choice(ActionKind.InviteParty, e.Contacts[0], "party", 35 + a.CombatDrive * 5, "根據自身戰鬥意願邀請同地點同門，不強制他人加入");
            if (e.PartyId is not null && a.DemoLocationId != "dungeon")
                yield return Choice(ActionKind.Travel, "dungeon", "travel", 45 + a.CombatDrive * 3, "加入小隊後自主前往副本集合，仍可因治療或資源需求改變行動");
            if (e.PartyId is not null && e.LeaderId == a.Id && e.PartyReady)
                yield return Choice(ActionKind.ExploreDungeon, "dungeon", "combat", 35 + a.CombatDrive * 5, "同門已集合且健康，評估隊伍清剿、掉落與較低受傷成本");
            if (e.PartyId is not null && a.DemoLocationId == "dungeon" && (!e.PartyReady || e.LeaderId != a.Id))
                yield return Choice(ActionKind.WaitParty, e.PartyId, "party", 45 + a.CombatDrive * 3, "自主等待隊員集合／隊長探索，避免來回旅行使小隊無法出發");
            foreach (var h in e.Hunts.Where(_ => a.Aspiration == Aspiration.Combat || a.CombatDrive >= 1))
            {
                if (!h.Accepted) yield return Choice(ActionKind.AcceptHunt, h.Id, "combat", 70 + a.CombatDrive * 5, $"接取{h.Target}清剿，每隻共享{h.Reward}貢獻點，成功計考核");
                else if (e.PartyId is null) yield return Choice(ActionKind.ExploreDungeon, "dungeon", "combat", 5 + a.CombatDrive * h.Reward / 2, "已接受清剿，評估副本收益及個人風險");
            }
        }
    }
}
