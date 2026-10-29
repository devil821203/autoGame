namespace Cultivation.Domain;

public enum CultivationStage { Mortal, Cultivator, Divinity, Immortal }
public enum CultivationAspect { Spirit, Body, Qi }
public enum MortalAttribute { Intelligence, MentalStrength, DivineSense, Willpower, Strength, Agility, Constitution, Sensitivity, QiSense, Comprehension, Luck }
public enum QiAwakeningStage { Unaware, Eligible, Awakened }

// Required rule values come from config. No runtime fallback balance values.
public sealed record MortalTrainingRules(int StaminaCostPerAction, decimal ExperiencePerAction,
    long BaseExperiencePerLevel, int LevelsPerLayer, decimal ExperienceMultiplierPerLayer,
    int AttributePointsPerLevel, int FreePointsPerLevel);
public sealed record QiAwakeningRules(
    [property: System.Text.Json.Serialization.JsonRequired] decimal SuccessProbabilityPerCompletedMeditation,
    [property: System.Text.Json.Serialization.JsonRequired] bool RollOnThresholdCompletion,
    [property: System.Text.Json.Serialization.JsonRequired] bool PityEnabled,
    [property: System.Text.Json.Serialization.JsonRequired] int AttributePointsOnAwakening,
    [property: System.Text.Json.Serialization.JsonRequired] int LevelGainOnAwakening,
    [property: System.Text.Json.Serialization.JsonRequired] decimal ExperienceAfterAwakening);
public sealed record MortalActionRules(int DurationTicks, int ActionPoints, int InitialAttributeValue,
    int InitialAspectLevel, decimal InitiationExperienceRequired, Dictionary<MortalAttribute, decimal> QiAttributeWeights,
    bool ApplyGrowthModifiers, bool BlockSevereInjury, bool AllowCancellation,
    bool RefundActionPointsOnInterruption, double AiTrainingUtility);
public sealed record MortalTrainingWork(ActionIntent Intent, long DueTick, int RemainingIterations = 1);

// These are earned growth only; starting attributes and temporary equipment are separate.
public sealed record MortalCultivationState
{
    public CultivationStage Stage { get; init; } = CultivationStage.Mortal;
    public long SpiritLevel { get; init; }
    public long BodyLevel { get; init; }
    public long QiLevel { get; init; }
    public Dictionary<MortalAttribute, decimal> Experience { get; init; } = [];
    public Dictionary<MortalAttribute, long> AttributeGrowth { get; init; } = [];
    public Dictionary<MortalAttribute, int> BaseAttributes { get; init; } = [];
    public decimal QiInitiationExperience { get; init; }
    public decimal QiExperience { get; init; }
    public QiAwakeningStage Awakening { get; init; }
    public long AwakeningAttempts { get; init; }
}

public static class MortalCultivationCalculator
{
    public static CultivationAspect Aspect(MortalAttribute target) => target switch
    {
        MortalAttribute.Intelligence or MortalAttribute.MentalStrength or MortalAttribute.DivineSense or MortalAttribute.Willpower => CultivationAspect.Spirit,
        MortalAttribute.Strength or MortalAttribute.Agility or MortalAttribute.Constitution => CultivationAspect.Body,
        MortalAttribute.Sensitivity or MortalAttribute.QiSense or MortalAttribute.Comprehension or MortalAttribute.Luck => CultivationAspect.Qi,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    public static long Level(MortalCultivationState state, CultivationAspect aspect) => aspect switch
    {
        CultivationAspect.Spirit => state.SpiritLevel,
        CultivationAspect.Body => state.BodyLevel,
        CultivationAspect.Qi => state.QiLevel,
        _ => throw new ArgumentOutOfRangeException(nameof(aspect))
    };

    public static long CompletedLayers(long level, MortalTrainingRules rules)
    {
        if (level < 0 || rules.LevelsPerLayer <= 0) throw new ArgumentOutOfRangeException(nameof(level));
        return level / rules.LevelsPerLayer;
    }

    public static long Threshold(long level, MortalTrainingRules rules)
    {
        if (rules.BaseExperiencePerLevel <= 0 || rules.ExperienceMultiplierPerLayer < 1)
            throw new ArgumentOutOfRangeException(nameof(rules));
        long exponent = CompletedLayers(level, rules);
        decimal factor = rules.ExperienceMultiplierPerLayer;
        decimal value = rules.BaseExperiencePerLevel;
        // Decimal exponentiation keeps configured 1.2 exact and bounds the work even on hostile saves.
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) value = checked(value * factor);
            exponent >>= 1;
            if (exponent > 0) factor = checked(factor * factor);
        }
        return checked((long)decimal.Ceiling(value));
    }

    // Pure calculation only. Action admission, resource payment and commit belong to Simulation.
    public static MortalCultivationState GainAttributeExperience(MortalCultivationState state,
        MortalAttribute target, decimal amount, MortalTrainingRules rules)
    {
        var aspect = Aspect(target);
        if (aspect == CultivationAspect.Qi || state.Stage != CultivationStage.Mortal || amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (rules.AttributePointsPerLevel <= 0 || rules.FreePointsPerLevel != 0)
            throw new ArgumentOutOfRangeException(nameof(rules));
        var experience = new Dictionary<MortalAttribute, decimal>(state.Experience);
        var growth = new Dictionary<MortalAttribute, long>(state.AttributeGrowth);
        decimal remaining = checked(experience.GetValueOrDefault(target) + amount);
        long level = Level(state, aspect);
        long earned = growth.GetValueOrDefault(target);
        long threshold;
        while (remaining >= (threshold = Threshold(level, rules)))
        {
            remaining -= threshold;
            level = checked(level + 1);
            earned = checked(earned + rules.AttributePointsPerLevel);
        }
        experience[target] = remaining;
        growth[target] = earned;
        return state with { Experience = experience, AttributeGrowth = growth,
            SpiritLevel = aspect == CultivationAspect.Spirit ? level : state.SpiritLevel,
            BodyLevel = aspect == CultivationAspect.Body ? level : state.BodyLevel };
    }
}
