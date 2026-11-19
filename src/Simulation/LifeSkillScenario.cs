using Cultivation.Domain;

namespace Cultivation.Simulation;

// Independent time/AP scenario; existing worlds keep their original rules.
public static class LifeSkillScenario
{
    public static WorldState Create(string name = "行者", GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find(); var w = TimeScenario.Create(name, config);
        if (w.TestProcurement is {} buyer) w.TestProcurement = config.Patch("life/procurement.json", buyer);
        w.AlchemyEnabled = config.Read<LifeAvailability>("life/availability.json").AlchemyEnabled;
        w.LifeSkillDemo = true;
        w.LifeSkillRules = config.Read<LifeSkillRules>("life/skills.json");
        w.Characters = w.Characters.Select(c => c with { LifeSkills = new() }).ToList();
        w.LifeSystem = new() {Options=config.Read<LifeOptions>("life/options.json"),Recipes=config.Read<List<LifeRecipe>>("life/recipes.json")};
        foreach(var item in new[]{new ItemDefinition("ointment","藥膏","藥物"),new("bleeding-powder","止血散","藥物"),new("iron-material","測試鐵料","原料"),new("leather-armor","皮甲","防具")})w.Items.Add(item);
        foreach(var pair in config.Read<Dictionary<string,long>>("life/test-stock.json"))
            w.Ledger.Add(new(w.NextSequence++,0,"life-test-stock","$genesis",w.ControlledCharacterId,pair.Key,pair.Value,"Genesis"));
        WorldIntegrity.Validate(w); return w;
    }
}

internal static class LifeSkillIntegrity
{
    internal static void Validate(WorldState w)
    {
        if (w.LifeSkillRules is not { } r)
        {
            if (w.Characters.Any(c => c.LifeSkills is not null)) throw new InvalidDataException("Life skill state requires rules");
            return;
        }
        if (r.ExperiencePerLevel <= 0 || r.TreatmentHerbs <= 0 || r.OintmentHerbs <= 0 ||
            r.TreatmentHealing is < 1 or > 100 || r.OintmentHealing is < 1 or > 100 || r.BleedingPowderHealing is < 1 or > 100 ||
            r.BleedingPowderRounds is < 1 or > 100 || !double.IsFinite(r.HealingBonusPerLevel) || r.HealingBonusPerLevel is < 0 or > 1 ||
            w.Characters.Any(c => c.LifeSkills is { } s && (s.MedicineExperience < 0 || s.PharmacyExperience < 0 || s.SmithingExperience < 0)))
            throw new InvalidDataException("Invalid life skill rules or experience");
    }
}

public sealed record LifeAvailability(bool AlchemyEnabled);
