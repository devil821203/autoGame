using Cultivation.Domain;

namespace Cultivation.AI;

public sealed record SectWorkObservation(SectFacilityRules Rules, bool GatherAccepted, bool BrewAccepted,
    long AvailableHerbs, long AvailablePills, long Contribution, string Source, long ObservedTick);

internal static class SectWorkUtility
{
    internal static IEnumerable<ScoredAction> Evaluate(LocalObservation o)
    {
        if (o.SectWork is not { } work || o.Actor.OrganizationId != work.Rules.OrganizationId || work.ObservedTick != o.Tick ||
            work.Source != $"sect-work:{work.Rules.OrganizationId}:{o.Actor.RegionId}" || o.Actor.Hostile) yield break;
        var a = o.Actor; var r = work.Rules;
        if (o.Expedition?.PartyId is not null && a.Health >= 80) yield break;
        ScoredAction Choice(ActionKind kind, string target, string item, double score, string reason) =>
            new(new($"ai:{a.Id}:{o.Tick}", a.Id, target, item, 1, 0, DecisionSource.Npc, kind), score, reason);
        if (a.Health >= 30 && o.Gatherable > 0)
            yield return Choice(work.GatherAccepted ? ActionKind.SectGather : ActionKind.AcceptSectWork,
                work.GatherAccepted ? "grove" : r.OrganizationId, "herb",
                (r.GatherContribution * a.WealthWeight + a.GrowthWeight * o.Rules.ExperiencePerAction * o.GrowthMultiplier) / o.Rules.GatherTicks,
                "宗門採藥工作：產出歸宗門，以貢獻點、熟練與考核效益比較");
        if (a.AlchemyChance > 0 && work.AvailableHerbs >= o.Rules.HerbsPerPill)
        {
            double chance = CharacterDevelopment.SuccessChance(a, o.Rules);
            yield return Choice(work.BrewAccepted ? ActionKind.SectBrew : ActionKind.AcceptSectWork,
                work.BrewAccepted ? "alchemy" : r.OrganizationId, work.BrewAccepted ? "herb" : "pill",
                (chance * r.BrewContribution * a.WealthWeight + a.GrowthWeight * o.Rules.ExperiencePerAction * o.GrowthMultiplier - (1 - chance) * a.Caution * 10) / o.Rules.BrewTicks,
                "宗門煉丹工作：宗門供料與持有丹藥，按真實成功領貢獻點，失敗不領點");
        }
        if (work.AvailablePills > 0 && work.Contribution >= r.PillExchangePrice && (o.Pills == 0 && (a.Health < 100 || a.GrowthWeight > 0 && a.Cultivation < a.DesiredCultivation)))
            yield return Choice(ActionKind.RedeemContribution, r.OrganizationId, "pill", Math.Max((100 - a.Health) * (1 + a.Caution), a.GrowthWeight * o.Rules.GrowthPerPill * o.GrowthMultiplier) + (o.Rules.KeepGrowthPill ? r.PillExchangePrice * a.WealthWeight : 0),
                "使用已有貢獻點兌換宗門庫存丹藥供治療／修煉，不兌換錢財");
        if (o.Rules.KeepGrowthPill && work.AvailablePills > 0 && work.Contribution < r.PillExchangePrice && o.Stones >= r.PillExchangePrice - work.Contribution && (o.Pills == 0 && (a.Health < 100 || a.GrowthWeight > 0 && a.Cultivation < a.DesiredCultivation)))
            yield return new(new($"ai:{a.Id}:{o.Tick}", a.Id, r.OrganizationId, "spirit-stone", r.PillExchangePrice - work.Contribution, 0, DecisionSource.Npc, ActionKind.BuyContribution),
                r.PillExchangePrice * a.WealthWeight + (100 - a.Health) * (1 + a.Caution), "用自有錢財補足兌換缺額，取得治療／修煉丹藥；資金回流宗門");
    }
}

