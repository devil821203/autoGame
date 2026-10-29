namespace Cultivation.Domain;

public sealed record SocialRules(long OfferLifetimeTicks = 30, long CooldownTicks = 5, long LessonPrice = 10,
    long LessonExperience = 2, int AffinityGain = 5, int TrustGain = 5, int MaximumMembers = 30, int SuccessionHealthThreshold = 50,
    int MissionReputationPerItem = 1, int LessonDiscountTrustThreshold = 50, int LessonTrustDiscountPercent = 20);

public static class SocialPricing
{
    public static long Lesson(SocialRules rules, int teacherTrust)
    {
        if (teacherTrust < rules.LessonDiscountTrustThreshold) return rules.LessonPrice;
        long discount = rules.LessonPrice / 100 * rules.LessonTrustDiscountPercent +
            rules.LessonPrice % 100 * rules.LessonTrustDiscountPercent / 100;
        return Math.Max(1, rules.LessonPrice - discount);
    }
}
public sealed record RelationshipState(string FromId, string ToId, int Affinity = 0, int Trust = 0, int Grudge = 0, bool Cooperating = false);
public enum SocialOfferKind { Cooperation, Apprenticeship, Recruitment, Succession }
public enum SocialOfferStatus { Pending, Accepted, Declined, Expired }
public sealed record SocialOffer(string Id, string FromId, string ToId, SocialOfferKind Kind,
    long ExpiresTick, string? OrganizationId = null, SocialOfferStatus Status = SocialOfferStatus.Pending);

