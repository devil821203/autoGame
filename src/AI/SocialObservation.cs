using Cultivation.Domain;

namespace Cultivation.AI;

public sealed record ReceivedSocialOffer(string Id, string FromId, SocialOfferKind Kind, long ExpiresTick, int Trust, int Grudge, string Source, long ObservedTick);
public sealed record KnownContact(string Id, bool Cooperating, bool IsMentor, bool NeedsHerbs, bool CanTeach, int Trust, int Grudge, string Source, long ObservedTick, long? LessonQuote = null);

internal static class SocialUtility
{
    internal static IEnumerable<ScoredAction> Evaluate(LocalObservation o)
    {
        if (o.SocialRules is not { } rules || o.Actor.SocialCooldownUntil > o.Tick || o.Actor.Hostile) yield break;
        var a = o.Actor;
        ScoredAction Choice(ActionKind kind, string target, double value, string reason, string? offer = null) =>
            new(new($"ai:{a.Id}:{o.Tick}", a.Id, target, "social", 1, 0, DecisionSource.Npc, kind, SocialOfferId: offer), value, reason);
        foreach (var offer in o.SocialOffers ?? [])
        {
            if (offer.Source != "invitation:" + a.Id || offer.ObservedTick != o.Tick || offer.ExpiresTick <= o.Tick + 1) continue;
            double value = a.SocialWeight * 10 + (offer.Kind == SocialOfferKind.Apprenticeship ? a.GrowthWeight * 5 : offer.Kind is SocialOfferKind.Recruitment or SocialOfferKind.Succession ? a.SectAffinity * 10 : 0) + offer.Trust / 10.0 - offer.Grudge / 2.0 - a.Caution;
            yield return Choice(value > 0 ? ActionKind.AcceptSocialOffer : ActionKind.DeclineSocialOffer, offer.FromId, Math.Max(1, Math.Abs(value)), "根據自身成長、社交、宗門偏好、信任與仇恨，自主接受或拒絕邀請", offer.Id);
        }
        foreach (var contact in o.Contacts ?? [])
        {
            if (contact.Source != "contact:" + a.Id || contact.ObservedTick != o.Tick || contact.Grudge > 30) continue;
            long price = contact.LessonQuote ?? rules.LessonPrice;
            if (contact.IsMentor && contact.CanTeach && price > 0 && o.Stones >= price)
                yield return Choice(ActionKind.StudyWithMentor, contact.Id, a.GrowthWeight * (rules.LessonExperience + (a.MethodId is null ? 10 : 0)) - price * a.WealthWeight / 10.0, $"付費向已同意的師父學習，依本地報價 {price} 評估成長與費用");
            if (contact.Cooperating && contact.NeedsHerbs && o.Herbs > 0)
                yield return Choice(ActionKind.AssistPartner, contact.Id, a.SocialWeight * 10 + contact.Trust / 10.0 - o.Rules.HerbPrice * a.WealthWeight / 4, "合作夥伴需要原料，評估物資機會成本與信任");
        }
    }
}

