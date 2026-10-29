using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SocialActions
{
    internal static bool Handles(ActionKind kind) => kind is ActionKind.InviteCooperation or ActionKind.InviteApprentice or ActionKind.InviteRecruit or ActionKind.InviteSuccessor or ActionKind.AcceptSocialOffer or ActionKind.DeclineSocialOffer or ActionKind.AssistPartner or ActionKind.StudyWithMentor;
    internal static RelationshipState Relation(WorldState w, string from, string to) => w.Relationships.SingleOrDefault(r => r.FromId == from && r.ToId == to) ?? new(from, to);
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.SocialRules is not { } rules) return "Social interactions not enabled";
        if (i.Quantity != 1 || i.DurationTicks != 0 || i.ResourceId != "social" || i.MissionId is not null) return "Invalid social action";
        var a = w.Characters.Single(c => c.Id == i.ActorId); var b = w.Characters.SingleOrDefault(c => c.Id == i.TargetId);
        if (b is null || a.Id == b.Id || a.RegionId != b.RegionId || a.Hostile || b.Hostile || b.Health == 0) return "No available local social target";
        if (w.Scheduled.Any(t => t.Intent.ActorId == a.Id) || a.SocialCooldownUntil > w.Clock.Tick) return "Social participant busy or cooling down";
        if (i.Kind is ActionKind.AssistPartner or ActionKind.StudyWithMentor && (w.Scheduled.Any(t => t.Intent.ActorId == b.Id) || CombatActions.Active(w, b.Id) is not null)) return "Social target busy";
        if (rules.CooldownTicks > long.MaxValue - w.Clock.Tick || rules.OfferLifetimeTicks > long.MaxValue - w.Clock.Tick) return "Social time overflow";
        if (i.Kind is ActionKind.AcceptSocialOffer or ActionKind.DeclineSocialOffer)
        {
            var offer = w.SocialOffers.SingleOrDefault(o => o.Id == i.SocialOfferId);
            if (offer is null || offer.ToId != a.Id || offer.FromId != b.Id || offer.Status != SocialOfferStatus.Pending || offer.ExpiresTick <= w.Clock.Tick) return "Social offer unavailable";
            return i.Kind == ActionKind.DeclineSocialOffer ? null : Qualifications(w, b, a, offer.Kind, offer.OrganizationId);
        }
        if (i.Kind == ActionKind.AssistPartner)
            return Relation(w, a.Id, b.Id).Cooperating && Inventory.Balance(w, a.Id, "herb") > 0 && InventoryCapacity.CanReceive(w, b.Id, "herb", 1) ? null : "No cooperation or aid inventory";
        if (i.Kind == ActionKind.StudyWithMentor)
        {
            if (a.MentorId != b.Id || b.MethodId is null || w.Advancement is null || (a.MethodId is not null && (a.MethodId != b.MethodId || a.MethodExperience >= b.MethodExperience))) return "No teachable mentor knowledge";
            long price = SocialPricing.Lesson(rules, Relation(w, b.Id, a.Id).Trust);
            return Inventory.Balance(w, a.Id, "spirit-stone") >= price && InventoryCapacity.CanReceive(w, b.Id, "spirit-stone", price) ? null : "Insufficient lesson funds or capacity";
        }
        var kind = OfferKind(i.Kind);
        if (w.SocialOffers.Any(o => o.FromId == a.Id && o.ToId == b.Id && o.Kind == kind && o.Status == SocialOfferStatus.Pending)) return "Duplicate pending social offer";
        string? organization = kind is SocialOfferKind.Recruitment or SocialOfferKind.Succession ? w.Organizations.FirstOrDefault(o => o.LeaderId == a.Id)?.Id : null;
        return Qualifications(w, a, b, kind, organization);
    }

    private static SocialOfferKind OfferKind(ActionKind kind) => kind switch { ActionKind.InviteApprentice => SocialOfferKind.Apprenticeship, ActionKind.InviteRecruit => SocialOfferKind.Recruitment, ActionKind.InviteSuccessor => SocialOfferKind.Succession, _ => SocialOfferKind.Cooperation };
    private static string? Qualifications(WorldState w, CharacterState from, CharacterState to, SocialOfferKind kind, string? organization)
    {
        if (kind == SocialOfferKind.Cooperation) return Relation(w, from.Id, to.Id).Cooperating ? "Already cooperating" : null;
        if (kind == SocialOfferKind.Succession)
            return organization is not null && w.Organizations.Any(o => o.Id == organization && o.LeaderId == from.Id && o.RegionId == to.RegionId) &&
                from.OrganizationId == organization && from.Position == OrganizationPosition.Leader &&
                to.OrganizationId == organization && to.Position is OrganizationPosition.Member or OrganizationPosition.Elder &&
                from.Health > 0 && !from.Hostile && !to.Hostile && to.Health > 0 && CombatActions.Active(w, to.Id) is null &&
                !w.Scheduled.Any(s => s.Intent.ActorId == to.Id) ? null : "Succession authority or member unavailable";
        if (kind == SocialOfferKind.Recruitment)
        {
            if(organization is not null&&w.Characters.Count(c=>c.OrganizationId==organization)>=OrganizationCapacity.MemberLimit(w,organization))return "Organization member capacity reached";
            var policy = w.OrganizationPolicies.SingleOrDefault(p => p.OrganizationId == organization);
            return organization is not null && w.Organizations.Any(o => o.Id == organization && o.LeaderId == from.Id && o.RegionId == to.RegionId) && to.OrganizationId is null && to.Reputation >= (policy?.MinimumReputation ?? 0) && w.Characters.Count(c => c.OrganizationId == organization) < (policy?.MaximumMembers ?? w.SocialRules!.MaximumMembers) ? null : "Recruitment authority membership or capacity invalid";
        }
        if (from.MethodId is null || to.MentorId is not null || w.Advancement is null || (from.Realm <= to.Realm && from.MethodExperience <= to.MethodExperience)) return "Mentorship prerequisites not met";
        for (var current = from; current.MentorId is not null; current = w.Characters.Single(c => c.Id == current.MentorId))
            if (current.MentorId == to.Id) return "Mentorship cycle";
        return null;
    }

    internal static void Execute(WorldState w, ActionIntent i)
    {
        var rules = w.SocialRules!;
        int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
        w.Characters[index] = w.Characters[index] with { SocialCooldownUntil = checked(w.Clock.Tick + rules.CooldownTicks) };
        if (i.Kind is ActionKind.InviteCooperation or ActionKind.InviteApprentice or ActionKind.InviteRecruit or ActionKind.InviteSuccessor)
        {
            var kind = OfferKind(i.Kind);
            string? org = kind is SocialOfferKind.Recruitment or SocialOfferKind.Succession ? w.Organizations.First(o => o.LeaderId == i.ActorId).Id : null;
            w.SocialOffers.Add(new(i.Id, i.ActorId, i.TargetId, kind, checked(w.Clock.Tick + rules.OfferLifetimeTicks), org));
        }
        else if (i.Kind is ActionKind.AcceptSocialOffer or ActionKind.DeclineSocialOffer)
        {
            int offerIndex = w.SocialOffers.FindIndex(o => o.Id == i.SocialOfferId); var offer = w.SocialOffers[offerIndex];
            bool accept = i.Kind == ActionKind.AcceptSocialOffer;
            w.SocialOffers[offerIndex] = offer with { Status = accept ? SocialOfferStatus.Accepted : SocialOfferStatus.Declined };
            if (accept)
            {
                if (offer.Kind == SocialOfferKind.Recruitment) w.Characters[index] = w.Characters[index] with { OrganizationId = offer.OrganizationId, Position = OrganizationPosition.Member };
                if (offer.Kind == SocialOfferKind.Apprenticeship) w.Characters[index] = w.Characters[index] with { MentorId = offer.FromId };
                if (offer.Kind == SocialOfferKind.Succession)
                {
                    int organizationIndex = w.Organizations.FindIndex(o => o.Id == offer.OrganizationId);
                    w.Organizations[organizationIndex] = w.Organizations[organizationIndex] with { LeaderId = i.ActorId };
                    int formerIndex = w.Characters.FindIndex(c => c.Id == offer.FromId);
                    w.Characters[formerIndex] = w.Characters[formerIndex] with { Position = OrganizationPosition.Member };
                    w.Characters[index] = w.Characters[index] with { Position = OrganizationPosition.Leader };
                }
                Adjust(w, i.ActorId, i.TargetId, rules.AffinityGain, rules.TrustGain, 0, offer.Kind == SocialOfferKind.Cooperation);
                Adjust(w, i.TargetId, i.ActorId, rules.AffinityGain, rules.TrustGain, 0, offer.Kind == SocialOfferKind.Cooperation);
            }
        }
        else
        {
            bool lesson = i.Kind == ActionKind.StudyWithMentor;
            long price = lesson ? SocialPricing.Lesson(rules, Relation(w, i.TargetId, i.ActorId).Trust) : 1;
            ActionExecutor.Transfer(w, i with { ResourceId = lesson ? "spirit-stone" : "herb", Quantity = price }, i.ActorId, i.TargetId, lesson ? "LessonPayment" : "CooperationAid");
            if (lesson)
            {
                var teacher = w.Characters.Single(c => c.Id == i.TargetId); var student = w.Characters[index];
                w.Characters[index] = student with { MethodId = teacher.MethodId, MethodExperience = Math.Min(teacher.MethodExperience, CharacterDevelopment.GainExperience(student.MethodExperience, StaminaCalculator.Growth(w, student, rules.LessonExperience))) };
            }
            Adjust(w, i.TargetId, i.ActorId, rules.AffinityGain, rules.TrustGain, 0);
        }
        ActionExecutor.Emit(w, i.Id, "Social", $"{i.ActorId}:{i.Kind}:{i.TargetId}");
    }

    internal static void Adjust(WorldState w, string from, string to, int affinity, int trust, int grudge, bool cooperating = false)
    {
        if (w.SocialRules is null || from == to) return;
        var old = Relation(w, from, to);
        w.Relationships.RemoveAll(r => r.FromId == from && r.ToId == to);
        w.Relationships.Add(old with { Affinity = Math.Clamp(old.Affinity + affinity, -100, 100), Trust = Math.Clamp(old.Trust + trust, -100, 100), Grudge = Math.Clamp(old.Grudge + grudge, 0, 100), Cooperating = old.Cooperating || cooperating });
    }
    internal static void Expire(WorldState w)
    {
        for (int n = 0; n < w.SocialOffers.Count; n++)
            if (w.SocialOffers[n].Status == SocialOfferStatus.Pending && w.SocialOffers[n].ExpiresTick <= w.Clock.Tick)
            {
                w.SocialOffers[n] = w.SocialOffers[n] with { Status = SocialOfferStatus.Expired };
                ActionExecutor.Emit(w, "$social-expire:" + w.SocialOffers[n].Id, "SocialExpired", w.SocialOffers[n].Id);
            }
    }
}

