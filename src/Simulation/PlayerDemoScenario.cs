using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class PlayerDemoScenario
{
    public static WorldState Create(string playerName, ulong seed = 12345, GameConfiguration? configuration = null)
    {
        string name = playerName?.Trim() ?? "";
        if (name.Length is < 1 or > 24 || name.Any(char.IsControl)) throw new ArgumentException("角色名稱須為1–24字，且不可包含控制字元。", nameof(playerName));
        var config = configuration ?? GameConfiguration.Find();
        var world = SocialScenario.Create(seed, config);
        int index = world.Characters.FindIndex(c => c.Id == world.ControlledCharacterId);
        world.Characters[index] = world.Characters[index] with { Name = name };
        world.DemoMapEnabled = true;
        world.HerbRegeneration = config.Read<RegenerationRules>("world/regeneration.json");
        WorldIntegrity.Validate(world);
        return world;
    }
}
