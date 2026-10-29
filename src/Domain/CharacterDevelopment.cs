namespace Cultivation.Domain;

public sealed record CharacterNeeds(double Wealth, double Growth, bool NeedsIngredients, bool NeedsPill,
    double Healing = 0, double Safety = 0, double Reputation = 0);

public static class CharacterDevelopment
{
    public static IReadOnlyList<string> Services(CharacterState actor) =>
        actor.AlchemyChance > 0 ? ["採集藥材", "煉製丹藥", "交易物品"] : ["採集藥材", "交易物品"];
    public static double SuccessChance(CharacterState actor, EconomyRules rules) =>
        actor.AlchemyChance <= 0 ? 0 : Math.Min(1, actor.AlchemyChance +
            Math.Min(rules.MaximumSkillBonus, actor.AlchemyExperience * rules.SkillChancePerExperience * actor.AlchemyTalent));

    public static CharacterNeeds Needs(CharacterState actor, long stones, long herbs, long pills, EconomyRules rules) => new(
        actor.DesiredStones == 0 ? 0 : Math.Clamp(1 - stones / (double)actor.DesiredStones, 0, 1),
        actor.DesiredCultivation == 0 ? 0 : Math.Clamp(1 - actor.Cultivation / (double)actor.DesiredCultivation, 0, 1),
        actor.AlchemyChance > 0 && herbs < rules.HerbsPerPill, pills == 0 && (actor.Cultivation < actor.DesiredCultivation || actor.Health < 100),
        (100 - actor.Health) / 100.0, Math.Clamp((50 - actor.Health) / 50.0, 0, 1),
        actor.DesiredReputation == 0 ? 0 : Math.Clamp(1 - actor.Reputation / (double)actor.DesiredReputation, 0, 1));

    public static long GainExperience(long current, long amount) => current > long.MaxValue - amount ? long.MaxValue : current + amount;
}
