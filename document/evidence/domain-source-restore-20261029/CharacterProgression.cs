namespace Cultivation.Domain;

public enum CharacterIdentity { Wanderer, Player, Herbalist, Alchemist, Fighter }
public enum OrganizationPosition { None, Member, Leader, Elder }
public enum CharacterCondition { Healthy, Injured, Incapacitated }
public sealed record AdvancementRules(string MethodId = "qingmu", long LearningCost = 20,
    long LearningTicks = 3, long BreakthroughTicks = 4, long BreakthroughPills = 2,
    long CultivationPerRealm = 50, int MaximumRealm = 3,
    long MethodExperiencePerLevel = 5, int MaximumMethodLevel = 5,
    long SkillExperiencePerLevel = 10, long GrowthBonusPerMethodLevel = 2,
    int AttackPerRealm = 4, int DefensePerRealm = 2, int AttackPerMethodLevel = 1,
    string WeaponId = "iron-sword", int WeaponAttack = 6, int WeaponDefense = 1);
public sealed record CharacterStats(int Attack, int Defense, int MethodLevel, long AlchemyLevel,
    long GatheringLevel, CharacterCondition Condition);

public static class CharacterProgression
{
    public static long Threshold(CharacterState actor, AdvancementRules rules) => checked((actor.Realm + 1L) * rules.CultivationPerRealm);
    public static int MethodLevel(CharacterState actor, AdvancementRules rules) => actor.MethodId is null ? 0 :
        1 + (int)Math.Min(rules.MaximumMethodLevel - 1, actor.MethodExperience / rules.MethodExperiencePerLevel);
    public static long GrowthPerPill(CharacterState actor, EconomyRules economy, AdvancementRules? rules) =>
        rules is null ? economy.GrowthPerPill : checked(economy.GrowthPerPill + MethodLevel(actor, rules) * rules.GrowthBonusPerMethodLevel);

    public static CharacterStats Stats(CharacterState actor, AdvancementRules? rules)
    {
        var condition = actor.Health == 0 ? CharacterCondition.Incapacitated : actor.Health < 100 ? CharacterCondition.Injured : CharacterCondition.Healthy;
        if (rules is null) return new(actor.BaseAttack, actor.BaseDefense, 0, 0, 0, condition);
        int level = MethodLevel(actor, rules);
        bool equipped = actor.EquippedWeaponId == rules.WeaponId;
        return new(checked(actor.BaseAttack + actor.Realm * rules.AttackPerRealm + level * rules.AttackPerMethodLevel + (equipped ? rules.WeaponAttack : 0)),
            checked(actor.BaseDefense + actor.Realm * rules.DefensePerRealm + (equipped ? rules.WeaponDefense : 0)),
            level, actor.AlchemyExperience / rules.SkillExperiencePerLevel, actor.GatheringExperience / rules.SkillExperiencePerLevel, condition);
    }
}
