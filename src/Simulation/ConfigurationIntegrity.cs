using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ConfigurationIntegrity
{
    internal static void Validate(WorldState w)
    {
        CurrencyIntegrity.Validate(w);
        DungeonLoot.Validate(w);
        if (!double.IsFinite(w.Rules.GrowthUtilityMultiplier) || w.Rules.GrowthUtilityMultiplier is <= 0 or > 1000) throw new InvalidDataException("Invalid growth utility multiplier");
        if (w.MissionDefaults is { } mission && (mission.Quantity <= 0 || mission.UnitPrice <= 0 || mission.DeadlineDays <= 0 ||
            mission.DeadlineDays > (long.MaxValue - int.MaxValue) / 86400 || mission.MaximumBudget <= 0 ||
            mission.Quantity > long.MaxValue / mission.UnitPrice || mission.MaximumBudget < mission.Quantity * mission.UnitPrice))
            throw new InvalidDataException("Invalid configured mission template");
        if (w.MapRules is { } map && (map.SecondsPerTick <= 0 || map.SecondsPerTick != w.Clock.SimulatedSecondsPerTick || map.TownTravelTicks <= 0 || map.OtherTravelTicks <= 0))
            throw new InvalidDataException("Invalid configured world time or travel duration");
        if (w.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != w.Items.Count ||
            w.Items.Any(i => string.IsNullOrWhiteSpace(i.Id) || string.IsNullOrWhiteSpace(i.Name) || string.IsNullOrWhiteSpace(i.Category)))
            throw new InvalidDataException("Invalid item catalog");
        if (w.MedicineOnly && (w.AlchemyEnabled || w.Items.Any(i=>i.Id=="pill") || w.Ledger.Any(l=>l.ResourceId=="pill") || w.Missions.Any(m=>m.ResourceId=="pill"))) throw new InvalidDataException("Medicine-only world contains pills or enabled alchemy");
        if (w.Items.Count > 0 && new[] { "spirit-stone", "herb", w.RecoveryResourceId, "beast-hide", "iron-sword", "contribution" }.Any(id => !w.Items.Any(i => i.Id == id)))
            throw new InvalidDataException("Item catalog is missing a supported item ID");
        if (w.Aspirations.Count > 0 && (w.Aspirations.Count > Enum.GetValues<Aspiration>().Length ||
            w.Aspirations.Select(a => a.Id).Distinct().Count() != w.Aspirations.Count || w.Aspirations.Any(a => !Enum.IsDefined(a.Id) || string.IsNullOrWhiteSpace(a.Name) ||
                new[] { a.WealthMultiplier, a.GrowthMultiplier, a.AlchemyExperienceMultiplier, a.CombatMultiplier }.Any(v => !double.IsFinite(v) || v < 0))))
            throw new InvalidDataException("Invalid aspiration catalog");
    }
}
