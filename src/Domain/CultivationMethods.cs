namespace Cultivation.Domain;

// A catalog snapshot; ownership, payment and state commits belong to Simulation.
public sealed record MethodTrainingRules(int DurationTicks, int ActionPointCost, int StaminaCost,
    decimal BaseSkillProficiency, decimal BaseCultivationExperience,
    bool IndependentSkillProficiency, bool CumulativeThresholds);
public sealed record MethodAttributeRequirement(long Strength, long Constitution);
public sealed record MethodLayerRequirement(int Layer, long Strength, long Constitution);
public sealed record MethodSkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string[] Tags { get; init; }
    public required string Training { get; init; }
    public string? CultivationTarget { get; init; }
    public string? PreferredTrainingAttribute { get; init; }
    public Dictionary<string, long> AttributeBonuses { get; init; } = [];
    public int UseStaminaCost { get; init; }
    public int? CooldownRounds { get; init; }
    public decimal[] PhysicalMultiplierByLayer { get; init; } = [];
    public decimal[] ConstitutionCoefficientByLayer { get; init; } = [];
    public long[] QingyunFistFlatDamageByLayer { get; init; } = [];
    public long[] StrengthBonusByLayer { get; init; } = [];
    public decimal? PhysicalAttackMultiplier { get; init; }
    public decimal? AgilityCoefficient { get; init; }
}
public sealed record CultivationMethodDefinition(string Id, string Name, int LayerCount,
    decimal[] CumulativeProficiencyThresholds, MethodAttributeRequirement? EntryRequirements,
    MethodLayerRequirement[] AdditionalLayerRequirements, MethodSkillDefinition[] Skills);
public sealed record CultivationMethodCatalog(string Status, MethodTrainingRules TrainingRules,
    CultivationMethodDefinition[] Methods)
{
    // Missing in historical snapshots means the original permanent-attribute policy.
    public bool UseCurrentAttributesForUpgrade { get; init; }
}
public sealed record MethodSkillProgress(string SkillId, decimal Proficiency);
public sealed record MethodWorldState(CultivationMethodCatalog Catalog,
    Dictionary<string, Dictionary<string, decimal>> Learners,
    HashSet<string>? TrainingFocusActors = null);
public sealed record MethodTestNpcRules(string TemplateActorId, string ActorId, string Name, long InitialMoney);
public sealed record MethodScenarioRules(decimal InitialProficiency, Dictionary<string, string[]> OrganizationMethods, string[] PlayerMethods);

public static class MethodProficiencyCalculator
{
    public static MethodSkillProgress Gain(MethodSkillProgress state, decimal amount)
    {
        if (state.Proficiency < 0 || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return state with { Proficiency = checked(state.Proficiency + amount) };
    }

    // Qualification policy is deliberately supplied by the caller. It must not silently
    // choose between permanent attributes and temporary bonuses before that rule is settled.
    public static int ProficiencyLayer(CultivationMethodDefinition definition, decimal proficiency)
    {
        if (proficiency < 0) throw new ArgumentOutOfRangeException(nameof(proficiency));
        return definition.CumulativeProficiencyThresholds.TakeWhile(t => proficiency >= t).Count();
    }
}
