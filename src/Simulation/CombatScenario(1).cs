using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class CombatScenario
{
    public static WorldState Create(ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = AdvancementScenario.Create(seed, config);
        w.CombatRules = config.Read<CombatRules>("combat/combat.json");
        config.ApplyCharacters(w, "combat");
        config.AddStock(w, "combat");
        WorldIntegrity.Validate(w);
        return w;
    }
}