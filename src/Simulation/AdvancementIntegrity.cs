using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class AdvancementIntegrity
{
    internal static void Validate(WorldState w)
    {
        var r = w.Advancement;
        if (r is not null && (string.IsNullOrWhiteSpace(r.MethodId) || string.IsNullOrWhiteSpace(r.WeaponId) || r.WeaponId is "pill" or "herb" or "spirit-stone" || r.LearningCost <= 0 || r.LearningTicks <= 0 || r.BreakthroughTicks <= 0 || r.BreakthroughPills <= 0 ||
            r.MaximumRealm is < 1 or > 100 || r.MaximumMethodLevel is < 1 or > 100 || r.CultivationPerRealm <= 0 || r.CultivationPerRealm > long.MaxValue / (r.MaximumRealm + 1L) ||
            r.MethodExperiencePerLevel <= 0 || r.SkillExperiencePerLevel <= 0 || r.GrowthBonusPerMethodLevel < 0 || r.GrowthBonusPerMethodLevel > (long.MaxValue - w.Rules.GrowthPerPill) / r.MaximumMethodLevel ||
            new[] { r.AttackPerRealm, r.DefensePerRealm, r.AttackPerMethodLevel, r.WeaponAttack, r.WeaponDefense }.Any(n => n is < 0 or > 1000000) || !w.Organizations.Any(o => o.Id == "consumed")))
            throw new InvalidDataException("Invalid advancement rules.");
        foreach (var actor in w.Characters)
        {
            if (!Enum.IsDefined(actor.Identity) || !Enum.IsDefined(actor.Position) || actor.BaseAttack is < 0 or > 1000000 || actor.BaseDefense is < 0 or > 1000000 || actor.MethodExperience < 0 ||
                (actor.OrganizationId is null ? actor.Position != OrganizationPosition.None : !w.Organizations.Any(o => o.Id == actor.OrganizationId) || actor.Position == OrganizationPosition.None) ||
                (actor.Position == OrganizationPosition.Leader && !w.Organizations.Any(o => o.Id == actor.OrganizationId && o.LeaderId == actor.Id)))
                throw new InvalidDataException("Invalid character identity or affiliation.");
            if (r is null)
            {
                if (actor.Realm != 0 || actor.MethodId is not null || actor.EquippedWeaponId is not null || actor.MethodExperience != 0) throw new InvalidDataException("Advancement state without rules.");
                continue;
            }
            if (actor.Realm < 0 || actor.Realm > r.MaximumRealm || actor.MethodId is not null && actor.MethodId != r.MethodId || actor.MethodId is null && (actor.Realm > 0 || actor.MethodExperience > 0) ||
                actor.EquippedWeaponId is not null && (Inventory.Balance(w,actor.Id,actor.EquippedWeaponId)<1||
                    actor.EquippedWeaponId!=r.WeaponId&&w.EquipmentDurability?.LegacyWeapons.Any(g=>g.Id==actor.EquippedWeaponId&&g.DefinitionId==r.WeaponId)!=true))
                throw new InvalidDataException("Invalid realm, method or equipped inventory.");
        }
        foreach (var task in w.Scheduled.Where(t => AdvancementActions.Handles(t.Intent.Kind)))
        {
            if (r is null || task.Intent.Kind is not (ActionKind.LearnMethod or ActionKind.Breakthrough) || task.Intent.TargetId != task.Intent.ActorId ||
                task.Intent.ResourceId != (task.Intent.Kind == ActionKind.LearnMethod ? "spirit-stone" : "pill") || task.Intent.Quantity != (task.Intent.Kind == ActionKind.LearnMethod ? r.LearningCost : r.BreakthroughPills))
                throw new InvalidDataException("Invalid advancement schedule.");
            var actor = w.Characters.SingleOrDefault(c => c.Id == task.Intent.ActorId);
            if (actor is null || (task.Intent.Kind == ActionKind.LearnMethod ? actor.MethodId is not null :
                actor.MethodId != r.MethodId || actor.Realm >= r.MaximumRealm || actor.Cultivation < CharacterProgression.Threshold(actor, r)))
                throw new InvalidDataException("Invalid scheduled advancement prerequisites.");
        }
    }
}
