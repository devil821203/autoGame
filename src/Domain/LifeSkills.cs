namespace Cultivation.Domain;

public enum LifeSkillKind { Medicine, Pharmacy, Smithing }
public sealed record LifeSkills(long MedicineExperience = 0, long PharmacyExperience = 0, long SmithingExperience = 0);
public sealed record LifeSkillRules(long ExperiencePerLevel, int TreatmentHerbs, int TreatmentHealing,
    double HealingBonusPerLevel, int OintmentHerbs, int OintmentHealing, int BleedingPowderHealing, int BleedingPowderRounds);

public static class LifeSkillCalculator
{
    public static long Experience(LifeSkills skills, LifeSkillKind kind) => kind switch
    {
        LifeSkillKind.Medicine => skills.MedicineExperience,
        LifeSkillKind.Pharmacy => skills.PharmacyExperience,
        LifeSkillKind.Smithing => skills.SmithingExperience,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static long Level(LifeSkills skills, LifeSkillKind kind, LifeSkillRules rules) => Experience(skills, kind) / rules.ExperiencePerLevel;
    public static int TreatmentHealing(CharacterState healer, CharacterState patient, LifeSkillRules rules)
    {
        long level = Level(healer.LifeSkills ?? new(), LifeSkillKind.Medicine, rules);
        double healing = Math.Floor(rules.TreatmentHealing * (1 + level * rules.HealingBonusPerLevel));
        // This ordinary treatment never revives an incapacitated patient.
        return patient.Health <= 0 ? 0 : (int)Math.Min(Math.Max(0, 100 - patient.Health), healing);
    }
}
