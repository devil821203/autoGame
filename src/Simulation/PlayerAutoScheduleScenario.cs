using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class PlayerAutoScheduleScenario
{
    public static WorldState Create(string name = "行者", GameConfiguration? configuration = null)
    {
        var world = GrowthScenario.Create(name, configuration: configuration);
        world.PlayerAutoSchedule = true;
        WorldIntegrity.Validate(world);
        return world;
    }
}
