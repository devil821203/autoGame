using Cultivation.Domain;
using Cultivation.AI;

namespace Cultivation.Simulation;

internal static class MortalTrainingActions
{
    internal static bool Handles(ActionKind kind) => kind is ActionKind.TrainMortalAttribute or ActionKind.MeditateQi;
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.MortalActionRules is null || w.MortalTrainingRules is null || w.Time is null) return "Mortal cultivation not enabled";
        var a = w.Characters.Single(c => c.Id == i.ActorId);
        if (a.MortalCultivation is null || a.Health <= 0 || CombatActions.Active(w, a.Id) is not null || w.MortalActionRules.BlockSevereInjury && a.SeverelyInjured ||
            a.Stamina is null || a.Stamina.Current < (MethodTraining.IsMethod(i) && w.Methods is { } methods ? methods.Catalog.TrainingRules.StaminaCost : w.MortalTrainingRules.StaminaCostPerAction)) return "Mortal cultivation unavailable or insufficient stamina";
        if (i.TargetId != a.Id || i.Quantity < 1 || i.Quantity > (w.Time.ActionDriven?.TrainingBatchLimit ?? 1) || i.DurationTicks != 0 || i.MissionId is not null ||
            i.BoardTurn is not null || w.Scheduled.Any(t => t.Intent.ActorId == a.Id) || w.LifeSystem?.Work.Any(t => t.Intent.ActorId == a.Id) == true)
            return "Invalid mortal training payload or busy actor";
        if (MethodTraining.IsMethod(i)) return MethodTraining.Validate(w, i);
        if (i.Kind == ActionKind.MeditateQi) return i.ResourceId == "qi" ? null : "Invalid meditation target";
        return !Enum.TryParse<MortalAttribute>(i.ResourceId, out var target) || !Enum.IsDefined(target) ||
            MortalCultivationCalculator.Aspect(target) == CultivationAspect.Qi ? "Invalid mortal training attribute" : null;
    }

    internal static void Start(WorldState w, ActionIntent i)
    {
        int duration = MethodTraining.IsMethod(i) ? w.Methods!.Catalog.TrainingRules.DurationTicks : w.MortalActionRules!.DurationTicks;
        w.MortalTrainingWork.Add(new(i, checked(w.Clock.Tick + duration), checked((int)i.Quantity)));
        ActionExecutor.Emit(w, i.Id, "MortalTrainingStarted", $"{i.ActorId}:{i.ResourceId}:{i.Quantity}次:{GameTimeDisplay.Duration(w, checked(duration * i.Quantity))}");
    }

    internal static void Interrupt(WorldState w, string actorId)
    {
        foreach (var work in w.MortalTrainingWork.Where(t => t.Intent.ActorId == actorId).ToArray())
        {
            w.MortalTrainingWork.Remove(work);
            ReleaseBusyTime(w, actorId);
            ActionExecutor.Emit(w, work.Intent.Id, "MortalTrainingInterrupted", $"{actorId}:受襲中斷，無經驗／未扣體力，AP不退");
        }
    }
    private static void ReleaseBusyTime(WorldState w, string actorId)
    {
        if (w.Time is null) return;
        int n = w.Time.Budgets.FindIndex(b => b.ActorId == actorId);
        if (n >= 0) w.Time.Budgets[n] = w.Time.Budgets[n] with { BusyThroughTick = w.Clock.Tick };
    }

    internal static void Complete(WorldState w)
    {
        foreach (var work in w.MortalTrainingWork.Where(t => t.DueTick <= w.Clock.Tick).OrderBy(t => t.DueTick).ToArray())
        {
            var i = work.Intent; int n = w.Characters.FindIndex(c => c.Id == i.ActorId); var a = w.Characters[n];
            w.MortalTrainingWork.Remove(work);
            var methodRules = MethodTraining.IsMethod(i) ? w.Methods!.Catalog.TrainingRules : null;
            int staminaCost = methodRules?.StaminaCost ?? w.MortalTrainingRules!.StaminaCostPerAction;
            if (a.Health <= 0 || a.SeverelyInjured || BoardBattleActions.Participant(w, a.Id) || CombatActions.Active(w, a.Id) is not null ||
                a.Stamina!.Current < staminaCost)
            { ReleaseBusyTime(w, a.Id); ActionExecutor.Emit(w, i.Id, "MortalTrainingInterrupted", a.Id); continue; }
            var s = a.MortalCultivation!; var r = w.MortalTrainingRules!; var options = w.MortalActionRules!;
            decimal multiplier = options.ApplyGrowthModifiers ? (decimal)CharacterGrowthCalculator.Modifiers(w, a).CombinedMultiplier : 1;
            decimal gain = checked((methodRules?.BaseCultivationExperience ?? r.ExperiencePerAction) * multiplier);
            if (i.Kind == ActionKind.TrainMortalAttribute)
                s = MortalCultivationCalculator.GainAttributeExperience(s, Enum.Parse<MortalAttribute>(MethodTraining.Target(w, i)), gain, r);
            else if (s.Awakening == QiAwakeningStage.Unaware)
            {
                decimal progress = Math.Min(options.InitiationExperienceRequired, checked(s.QiInitiationExperience + gain));
                s = s with { QiInitiationExperience = progress, Awakening = progress >= options.InitiationExperienceRequired ? QiAwakeningStage.Eligible : QiAwakeningStage.Unaware };
            }
            else if (s.Awakening == QiAwakeningStage.Eligible)
            {
                var rng = new SeededRandom(w.RandomState);
                bool success = rng.NextUInt32() / ((decimal)uint.MaxValue + 1) < w.QiAwakeningRules!.SuccessProbabilityPerCompletedMeditation;
                w.RandomState = rng.State;
                s = s with { AwakeningAttempts = checked(s.AwakeningAttempts + 1), Awakening = success ? QiAwakeningStage.Awakened : s.Awakening,
                    QiExperience = success ? w.QiAwakeningRules.ExperienceAfterAwakening : s.QiExperience };
                ActionExecutor.Emit(w, i.Id, "QiAwakeningAttempt", $"{a.Id}:{(success ? "得炁成功" : "未得炁")}:{s.AwakeningAttempts}");
            }
            else
            {
                decimal remaining = checked(s.QiExperience + gain); long level = s.QiLevel;
                var growth = new Dictionary<MortalAttribute, long>(s.AttributeGrowth);
                var rng = new SeededRandom(w.RandomState);
                while (remaining >= MortalCultivationCalculator.Threshold(level, r))
                {
                    remaining -= MortalCultivationCalculator.Threshold(level, r); level = checked(level + 1);
                    decimal roll = rng.NextUInt32() / ((decimal)uint.MaxValue + 1) * options.QiAttributeWeights.Values.Sum();
                    var target = options.QiAttributeWeights.OrderBy(p => p.Key).Last().Key;
                    foreach (var weight in options.QiAttributeWeights.OrderBy(p => p.Key))
                    { if (roll < weight.Value) { target = weight.Key; break; } roll -= weight.Value; }
                    growth[target] = checked(growth.GetValueOrDefault(target) + r.AttributePointsPerLevel);
                }
                w.RandomState = rng.State; s = s with { QiLevel = level, QiExperience = remaining, AttributeGrowth = growth };
            }
            int stamina = a.Stamina!.Current - staminaCost;
            foreach (var growth in s.AttributeGrowth.Where(p => p.Value > a.MortalCultivation!.AttributeGrowth.GetValueOrDefault(p.Key)))
                ActionExecutor.Emit(w, i.Id, "MortalAttributeImproved", $"{a.Id}:{growth.Key}:+{growth.Value - a.MortalCultivation!.AttributeGrowth.GetValueOrDefault(growth.Key)}");
            w.Characters[n] = a with { MortalCultivation = s, Stamina = a.Stamina with { Current = stamina, Resting = stamina == 0, Unconscious = stamina == 0 } };
            if (methodRules is not null)
            {
                var skill = MethodTraining.Skill(w, i)!;
                var learned = w.Methods!.Learners[a.Id];
                decimal skillGain = checked(methodRules.BaseSkillProficiency * multiplier);
                learned[skill.Id] = checked(learned[skill.Id] + skillGain);
                ActionExecutor.Emit(w, i.Id, "MethodTrainingCompleted", $"{a.Id}:{skill.Id}:熟練+{skillGain}:累計{learned[skill.Id]}:層{MethodTraining.Layer(w, a.Id, skill.Id)}");
            }
            ActionExecutor.Emit(w, i.Id, "MortalTrainingCompleted", $"{a.Id}:{i.ResourceId}:經驗{gain}:體力-{staminaCost}:性{s.SpiritLevel}/命{s.BodyLevel}/炁{s.QiLevel}");
            if (work.RemainingIterations > 1) {
                if (stamina < staminaCost || stamina == 0) {
                    ReleaseBusyTime(w, a.Id);
                    ActionExecutor.Emit(w, i.Id, "MortalTrainingInterrupted", $"{a.Id}:體力不足，中止剩餘{work.RemainingIterations - 1}次，已付AP不退");
                } else {
                    int duration = methodRules?.DurationTicks ?? options.DurationTicks;
                    w.MortalTrainingWork.Add(work with { DueTick = checked(w.Clock.Tick + duration), RemainingIterations = work.RemainingIterations - 1 });
                }
            }
        }
    }

    internal static IEnumerable<ScoredAction> Evaluate(WorldState w, CharacterState a, DecisionSource source)
    {
        if (w.MortalActionRules is not { } options || a.MortalCultivation is not { } s) yield break;
        foreach (var candidate in MethodTraining.Evaluate(w, a, source)) yield return candidate;
        // All candidates are later filtered through the common validator. No direct state edits.
        foreach (var target in Enum.GetValues<MortalAttribute>().Where(t => MortalCultivationCalculator.Aspect(t) != CultivationAspect.Qi))
        {
            double score = options.AiTrainingUtility * a.GrowthWeight / (1 + (double)s.AttributeGrowth.GetValueOrDefault(target));
            yield return new(new ActionIntent($"mortal:{a.Id}:{w.Clock.Tick}:{target}", a.Id, a.Id, target.ToString(), 1, 0, source, ActionKind.TrainMortalAttribute), score, "依成長志向與既有屬性成長評估修練");
        }
        yield return new(new ActionIntent($"meditate:{a.Id}:{w.Clock.Tick}", a.Id, a.Id, "qi", 1, 0, source, ActionKind.MeditateQi),
            options.AiTrainingUtility * a.GrowthWeight / (1 + (double)s.QiLevel), "打坐以累積入門／正式炁功，得炁依實際機率");
    }
}

public static class MortalCultivationScenario
{
    public static WorldState Create(string name = "行者", GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = MedicineOnlyProfile.ApplyFresh(TimeScenario.Create(name, config), config);
        var r = MortalCultivationConfiguration.Read(config);
        w.MortalTrainingRules = r.Training; w.QiAwakeningRules = r.Awakening;
        w.MortalActionRules = config.Read<MortalActionRules>("cultivation/mortal-actions.json");
        w.Characters = w.Characters.Select(c => c.MonsterKind != MonsterKind.None ? c : c with { MortalCultivation = new()
        { BaseAttributes = Enum.GetValues<MortalAttribute>().ToDictionary(a => a, _ => w.MortalActionRules.InitialAttributeValue) } }).ToList();
        WorldIntegrity.Validate(w); return w;
    }
}
