using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class AdvancementScenario
{
    public static WorldState Create(ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = MissionScenario.Create(seed, config);
        w.Advancement = config.Read<AdvancementRules>("cultivation/advancement.json");
        config.ApplyCharacters(w, "advancement");
        config.AddStock(w, "advancement");
        WorldIntegrity.Validate(w);
        return w;
    }
}
