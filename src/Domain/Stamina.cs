namespace Cultivation.Domain;

public enum RestEnvironment { Outdoors, Ordinary, HomeBed }
public sealed record StaminaRules(int Maximum = 100, int FatigueThreshold = 30,
    int ActivityCost = 10, double FatigueMultiplier = 0.5,
    int OutdoorRecovery = 15, int OrdinaryRecovery = 30, int HomeBedRecovery = 45,
    int UnconsciousRecovery = 10, int HealthRecoveryPerTick = 0);
public sealed record StaminaState(int Current = 100, bool Resting = false, bool Unconscious = false,
    string? HomeLocationId = null, bool HasBed = false);

public sealed record StaminaStatus(int Current, int Maximum, bool Fatigued, bool Resting, bool Unconscious,
    RestEnvironment Environment, int RecoveryPerTick, double GrowthMultiplier);

public static class StaminaCalculator
{
    public static StaminaStatus? Status(WorldState world, CharacterState actor)
    {
        if (world.StaminaRules is not { } rules || actor.Stamina is not { } state) return null;
        return new(state.Current, rules.Maximum, Fatigued(actor, rules), state.Resting, state.Unconscious,
            Environment(actor), Recovery(actor, rules), CharacterGrowthCalculator.Modifiers(world, actor).CombinedMultiplier);
    }
    public static bool Fatigued(CharacterState actor, StaminaRules rules) => actor.Stamina!.Current < rules.FatigueThreshold;
    public static int Cost(ActionKind kind, StaminaRules rules) => kind is ActionKind.Travel or ActionKind.Gather
        or ActionKind.Brew or ActionKind.Cultivate or ActionKind.LearnMethod or ActionKind.StudyWithMentor
        or ActionKind.SectGather or ActionKind.SectBrew or ActionKind.Breakthrough or ActionKind.CraftLifeItem ? rules.ActivityCost : 0;
    public static RestEnvironment Environment(CharacterState actor) =>
        actor.Stamina is { HasBed: true, HomeLocationId: { } home } && home == actor.DemoLocationId
            ? RestEnvironment.HomeBed : actor.DemoLocationId is "town" or "sect" or "alchemy"
                ? RestEnvironment.Ordinary : RestEnvironment.Outdoors;
    public static int Recovery(CharacterState actor, StaminaRules rules) => actor.Stamina!.Unconscious
        ? rules.UnconsciousRecovery : Environment(actor) switch
        { RestEnvironment.HomeBed => rules.HomeBedRecovery, RestEnvironment.Ordinary => rules.OrdinaryRecovery, _ => rules.OutdoorRecovery };
    public static long Growth(WorldState world, CharacterState actor, long amount) =>
        CharacterGrowthCalculator.Growth(world, actor, amount);
}

