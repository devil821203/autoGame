namespace Cultivation.Domain;

public sealed record MindsetRules(int Minimum = 0, int Maximum = 100, int Initial = 50,
    double BaseMultiplier = 0.5, double MultiplierPerPoint = 0.01);
public sealed record GrowthModifiers(double MindsetMultiplier, double FatigueMultiplier, double CombinedMultiplier);

public static class CharacterGrowthCalculator
{
    public static GrowthModifiers Modifiers(WorldState world, CharacterState actor)
    {
        double mindset = world.MindsetRules is { } rules && actor.Mindset is { } value
            ? rules.BaseMultiplier + value * rules.MultiplierPerPoint : 1;
        double fatigue = world.StaminaRules is { } stamina && StaminaCalculator.Fatigued(actor, stamina)
            ? stamina.FatigueMultiplier : 1;
        return new(mindset, fatigue, mindset * fatigue);
    }
    // Integer experience is rounded down once, after combining all applicable modifiers.
    public static long Growth(WorldState world, CharacterState actor, long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        decimal scaled = decimal.Floor(amount * (decimal)Modifiers(world, actor).CombinedMultiplier);
        return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
    }
}
