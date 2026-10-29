using Cultivation.Domain;

namespace Cultivation.Simulation;

public static partial class CultivationMethodConfiguration
{
    public const string Path = "cultivation/methods-proposal.json";
    public static void Validate(CultivationMethodCatalog catalog)
    {
        void Require(bool condition, string field)
        { if (!condition) throw new InvalidDataException($"{Path}: invalid {field}"); }
        Require(catalog is not null, "catalog");
        var rules = catalog!.TrainingRules;
        Require(rules is not null, "TrainingRules");
        Require(rules!.DurationTicks > 0 && rules.ActionPointCost == rules.DurationTicks && rules.StaminaCost > 0,
            "TrainingRules.DurationTicks/ActionPointCost/StaminaCost");
        Require(rules.BaseSkillProficiency > 0 && rules.BaseCultivationExperience > 0 &&
            rules.IndependentSkillProficiency && rules.CumulativeThresholds, "TrainingRules.proficiency");
        Require(catalog.Methods is { Length: > 0 }, "Methods");
        var methodIds = new HashSet<string>(StringComparer.Ordinal);
        var skillIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in catalog.Methods!)
        {
            Require(method is not null, "Methods.entry");
            string prefix = $"Methods.{method!.Id}";
            Require(!string.IsNullOrWhiteSpace(method.Id) && methodIds.Add(method.Id) &&
                !string.IsNullOrWhiteSpace(method.Name), prefix + ".Id/Name");
            Require(method.LayerCount > 0 && method.CumulativeProficiencyThresholds is { } thresholds &&
                thresholds.Length == method.LayerCount && thresholds.All(t => t > 0) &&
                thresholds.Zip(thresholds.Skip(1)).All(p => p.First < p.Second), prefix + ".CumulativeProficiencyThresholds");
            Require(method.EntryRequirements is null || method.EntryRequirements is { Strength: >= 0, Constitution: >= 0 }, prefix + ".EntryRequirements");
            Require(method.AdditionalLayerRequirements is not null && method.AdditionalLayerRequirements.All(r =>
                r.Layer > 1 && r.Layer <= method.LayerCount && r.Strength >= 0 && r.Constitution >= 0) &&
                method.AdditionalLayerRequirements.Select(r => r.Layer).Distinct().Count() == method.AdditionalLayerRequirements.Length,
                prefix + ".AdditionalLayerRequirements");
            Require(method.Skills is { Length: > 0 }, prefix + ".Skills");
            foreach (var skill in method.Skills!)
            {
                Require(skill is not null, prefix + ".Skills.entry");
                string field = prefix + ".Skills." + skill!.Id;
                Require(!string.IsNullOrWhiteSpace(skill.Id) && skillIds.Add(skill.Id) &&
                    !string.IsNullOrWhiteSpace(skill.Name) && !string.IsNullOrWhiteSpace(skill.Training), field + ".Id/Name/Training");
                Require(skill.Tags is { Length: > 0 } && skill.Tags.All(t => !string.IsNullOrWhiteSpace(t)) &&
                    skill.Tags.Distinct().Count() == skill.Tags.Length && skill.Tags.Count(t => t is "心法" or "招式" or "技巧") == 1,
                    field + ".Tags");
                Require(skill.UseStaminaCost >= 0 && skill.CooldownRounds is null or >= 0 && skill.PhysicalAttackMultiplier is null or > 0 &&
                    skill.AgilityCoefficient is null or >= 0, field + ".cost/damage");
                Require(skill.AttributeBonuses is not null && skill.AttributeBonuses.All(p =>
                    p.Key is "Strength" or "Constitution" or "Agility" && p.Value >= 0), field + ".AttributeBonuses");
                Require(skill.PreferredTrainingAttribute is null or "Strength" or "Constitution" or "Agility", field + ".PreferredTrainingAttribute");
                Require(skill.CultivationTarget is null or "QiOrAwakeningEntry", field + ".CultivationTarget");
                CheckSeries(skill.PhysicalMultiplierByLayer, true, field + ".PhysicalMultiplierByLayer");
                CheckSeries(skill.ConstitutionCoefficientByLayer, false, field + ".ConstitutionCoefficientByLayer");
                CheckSeries(skill.QingyunFistFlatDamageByLayer?.Select(v => (decimal)v).ToArray(), false, field + ".QingyunFistFlatDamageByLayer");
                CheckSeries(skill.StrengthBonusByLayer?.Select(v => (decimal)v).ToArray(), false, field + ".StrengthBonusByLayer");
                void CheckSeries(decimal[]? values, bool positive, string name) => Require(values is not null &&
                    (values.Length == 0 || values.Length == method.LayerCount) && values.All(v => positive ? v > 0 : v >= 0), name);
            }
        }
    }
}
