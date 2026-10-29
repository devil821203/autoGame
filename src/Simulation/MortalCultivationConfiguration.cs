using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class MortalCultivationConfiguration
{
    public static (MortalTrainingRules Training, QiAwakeningRules Awakening) Read(GameConfiguration configuration)
    {
        var training = configuration.Read<MortalTrainingRules>("cultivation/mortal-training.json");
        ValidateTraining(training);
        var awakening = configuration.Read<QiAwakeningRules>("cultivation/qi-awakening.json");
        ValidateAwakening(awakening);
        return (training, awakening);
    }

    public static void ValidateTraining(MortalTrainingRules r)
    {
        void Require(bool condition, string field)
        { if (!condition) throw new InvalidDataException($"cultivation/mortal-training.json: invalid {field}"); }
        Require(r.StaminaCostPerAction > 0, nameof(r.StaminaCostPerAction));
        Require(r.ExperiencePerAction > 0, nameof(r.ExperiencePerAction));
        Require(r.BaseExperiencePerLevel > 0, nameof(r.BaseExperiencePerLevel));
        Require(r.LevelsPerLayer > 0, nameof(r.LevelsPerLayer));
        Require(r.ExperienceMultiplierPerLayer >= 1, nameof(r.ExperienceMultiplierPerLayer));
        Require(r.AttributePointsPerLevel > 0, nameof(r.AttributePointsPerLevel));
        Require(r.FreePointsPerLevel == 0, nameof(r.FreePointsPerLevel));
        try { _ = MortalCultivationCalculator.Threshold(r.LevelsPerLayer, r); }
        catch (OverflowException e) { throw new InvalidDataException("cultivation/mortal-training.json: ExperienceMultiplierPerLayer/BaseExperiencePerLevel overflow", e); }
    }

    public static void ValidateAwakening(QiAwakeningRules r)
    {
        void Require(bool condition, string field)
        { if (!condition) throw new InvalidDataException($"cultivation/qi-awakening.json: invalid {field}"); }
        Require(r.SuccessProbabilityPerCompletedMeditation is >= 0 and <= 1, nameof(r.SuccessProbabilityPerCompletedMeditation));
        Require(!r.RollOnThresholdCompletion, nameof(r.RollOnThresholdCompletion));
        Require(!r.PityEnabled, nameof(r.PityEnabled));
        Require(r.AttributePointsOnAwakening == 0, nameof(r.AttributePointsOnAwakening));
        Require(r.LevelGainOnAwakening == 0, nameof(r.LevelGainOnAwakening));
        Require(r.ExperienceAfterAwakening == 0, nameof(r.ExperienceAfterAwakening));
    }
}

internal static class MortalCultivationIntegrity
{
    internal static void Validate(WorldState world)
    {
        if (world.MortalActionRules is { } options)
        {
            if (world.MortalTrainingRules is null || world.Time is null || world.StaminaRules is null ||
                options.DurationTicks <= 0 || options.DurationTicks != options.ActionPoints || options.ActionPoints > world.Time.Rules.DailyActionPoints ||
                options.InitialAttributeValue < 0 || options.InitialAspectLevel != 0 || options.InitiationExperienceRequired <= 0 ||
                !options.ApplyGrowthModifiers || !options.BlockSevereInjury || options.AllowCancellation || options.RefundActionPointsOnInterruption ||
                !double.IsFinite(options.AiTrainingUtility) || options.AiTrainingUtility <= 0 || options.QiAttributeWeights is null ||
                options.QiAttributeWeights.Count != Enum.GetValues<MortalAttribute>().Count(a => MortalCultivationCalculator.Aspect(a) == CultivationAspect.Qi) ||
                options.QiAttributeWeights.Any(p => !Enum.IsDefined(p.Key) || MortalCultivationCalculator.Aspect(p.Key) != CultivationAspect.Qi || p.Value <= 0) ||
                options.QiAttributeWeights.Values.Distinct().Count() != 1)
                throw new InvalidDataException("cultivation/mortal-actions.json: invalid duration/AP/initial values/weights or policy");
        }
        else if (world.MortalTrainingWork.Count != 0) throw new InvalidDataException("Mortal work requires action rules");
        if (world.MortalTrainingWork.Select(t => t.Intent.ActorId).Distinct().Count() != world.MortalTrainingWork.Count ||
            world.MortalTrainingWork.Any(t => t.DueTick <= world.Clock.Tick || !MortalTrainingActions.Handles(t.Intent.Kind) ||
                t.Intent.Quantity < 1 || t.Intent.Quantity > (world.Time?.ActionDriven?.TrainingBatchLimit ?? 1) ||
                t.RemainingIterations < 1 || t.RemainingIterations > t.Intent.Quantity ||
                !world.ProcessedIntentIds.Contains(t.Intent.Id) || !world.Characters.Any(c => c.Id == t.Intent.ActorId && c.MortalCultivation is not null)))
            throw new InvalidDataException("Invalid mortal training work");
        if (world.MortalTrainingRules is not { } rules)
        {
            if (world.QiAwakeningRules is not null || world.Characters.Any(c => c.MortalCultivation is not null))
                throw new InvalidDataException("Mortal cultivation state requires rules");
            return;
        }
        MortalCultivationConfiguration.ValidateTraining(rules);
        if (world.QiAwakeningRules is not { } awakening) throw new InvalidDataException("Missing qi awakening rules");
        MortalCultivationConfiguration.ValidateAwakening(awakening);
        if (world.StaminaRules is { } stamina && rules.StaminaCostPerAction > stamina.Maximum)
            throw new InvalidDataException("cultivation/mortal-training.json: StaminaCostPerAction exceeds stamina Maximum");
        foreach (var actor in world.Characters.Where(c => c.MortalCultivation is not null))
        {
            var s = actor.MortalCultivation!;
            if (s.Stage != CultivationStage.Mortal || !Enum.IsDefined(s.Awakening) ||
                s.SpiritLevel < 0 || s.BodyLevel < 0 || s.QiLevel < 0 ||
                s.QiExperience < 0 || s.QiInitiationExperience < 0 || s.AwakeningAttempts < 0 ||
                s.Experience is null || s.AttributeGrowth is null ||
                s.Experience.Any(p => !Enum.IsDefined(p.Key) || p.Value < 0 || MortalCultivationCalculator.Aspect(p.Key) == CultivationAspect.Qi) ||
                s.AttributeGrowth.Any(p => !Enum.IsDefined(p.Key) || p.Value < 0) ||
                s.BaseAttributes is null || s.BaseAttributes.Any(p => !Enum.IsDefined(p.Key) || p.Value < 0) ||
                s.Awakening != QiAwakeningStage.Awakened && (s.QiExperience != 0 || s.QiLevel != 0))
                throw new InvalidDataException($"Invalid mortal cultivation state: {actor.Id}");
            if (world.MortalActionRules is { } ar &&
                (s.BaseAttributes.Count != Enum.GetValues<MortalAttribute>().Length ||
                 s.QiInitiationExperience > ar.InitiationExperienceRequired ||
                 s.Awakening == QiAwakeningStage.Unaware && s.QiInitiationExperience >= ar.InitiationExperienceRequired ||
                 s.Awakening != QiAwakeningStage.Unaware && s.QiInitiationExperience != ar.InitiationExperienceRequired))
                throw new InvalidDataException($"Invalid mortal attributes/initiation state: {actor.Id}");
            try
            {
                foreach (var aspect in Enum.GetValues<CultivationAspect>())
                {
                    long level = MortalCultivationCalculator.Level(s, aspect);
                    _ = MortalCultivationCalculator.Threshold(level, rules);
                    long points = s.AttributeGrowth.Where(p => MortalCultivationCalculator.Aspect(p.Key) == aspect)
                        .Aggregate(0L, (total, p) => checked(total + p.Value));
                    if (points != checked(level * rules.AttributePointsPerLevel))
                        throw new InvalidDataException($"Mortal growth/level mismatch: {actor.Id}/{aspect}");
                }
            }
            catch (OverflowException e) { throw new InvalidDataException($"Mortal cultivation overflow: {actor.Id}", e); }
        }
    }
}
