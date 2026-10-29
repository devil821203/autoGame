using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SocialIntegrity
{
    internal static void Validate(WorldState w)
    {
        var actors = w.Characters.ToDictionary(c => c.Id, StringComparer.Ordinal);
        if (w.SocialRules is { } pricing && (pricing.LessonDiscountTrustThreshold is < 1 or > 100 || pricing.LessonTrustDiscountPercent is < 0 or > 100)) throw new InvalidDataException("Invalid lesson discount rules.");
        if (w.SocialRules is { MissionReputationPerItem: < 0 or > 100 }) throw new InvalidDataException("Invalid mission reputation reward.");
        if (w.SocialRules is { SuccessionHealthThreshold: < 1 or > 100 }) throw new InvalidDataException("Invalid succession health threshold.");
        if (w.OrganizationPolicies.Select(p => p.OrganizationId).Distinct().Count() != w.OrganizationPolicies.Count ||
            w.OrganizationPolicies.Any(p => w.SocialRules is null || !GovernanceActions.ValidPolicy(p) || !w.Organizations.Any(o => o.Id == p.OrganizationId && o.LeaderId is not null))) throw new InvalidDataException("Invalid organization policies.");
        if (w.SocialRules is not { } r)
        {
            if (w.Relationships.Count > 0 || w.SocialOffers.Count > 0 || w.Characters.Any(c => c.MentorId is not null || c.SocialCooldownUntil != 0)) throw new InvalidDataException("Social state without rules.");
            return;
        }
        if (r.OfferLifetimeTicks <= 0 || r.CooldownTicks <= 0 || r.LessonPrice <= 0 || r.LessonExperience <= 0 || r.AffinityGain is < 1 or > 100 || r.TrustGain is < 1 or > 100 || r.MaximumMembers <= 0) throw new InvalidDataException("Invalid social rules.");
        if (w.Relationships.Select(s => (s.FromId, s.ToId)).Distinct().Count() != w.Relationships.Count || w.Relationships.Any(s => !actors.ContainsKey(s.FromId) || !actors.ContainsKey(s.ToId) || s.FromId == s.ToId || s.Affinity is < -100 or > 100 || s.Trust is < -100 or > 100 || s.Grudge is < 0 or > 100)) throw new InvalidDataException("Invalid sparse relationships.");
        if (w.SocialOffers.Select(s => s.Id).Distinct().Count() != w.SocialOffers.Count || w.SocialOffers.Any(s => !actors.ContainsKey(s.FromId) || !actors.ContainsKey(s.ToId) || s.FromId == s.ToId || !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Status) || s.ExpiresTick <= 0 ||
            s.Status == SocialOfferStatus.Pending && s.ExpiresTick <= w.Clock.Tick || s.Kind is SocialOfferKind.Recruitment or SocialOfferKind.Succession && !w.Organizations.Any(o => o.Id == s.OrganizationId))) throw new InvalidDataException("Invalid social offer.");
        foreach (var actor in w.Characters)
        {
            if (actor.SocialCooldownUntil < 0) throw new InvalidDataException("Invalid social cooldown.");
            var seen = new HashSet<string>(StringComparer.Ordinal) { actor.Id };
            var current = actor;
            while (current.MentorId is not null)
            {
                if (!actors.TryGetValue(current.MentorId, out var mentor) || !seen.Add(mentor.Id)) throw new InvalidDataException("Invalid mentor or mentorship cycle.");
                current = mentor;
            }
        }
    }
}

