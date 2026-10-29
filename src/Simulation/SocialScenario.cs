using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SocialScenario
{
    public static WorldState Create(ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = CombatScenario.Create(seed, config);
        w.SocialRules = config.Read<SocialRules>("organizations/social.json");
        config.ApplyCharacters(w, "social");
        WorldIntegrity.Validate(w);
        return w;
    }
}