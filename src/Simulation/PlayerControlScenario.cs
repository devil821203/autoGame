using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class PlayerControlScenario
{
    public static WorldState Create(string name="行者",ulong? seed=null,GameConfiguration? configuration=null)
    {
        var config=configuration??GameConfiguration.Find();var w=RegionalWorldScenario.Create(name,seed,config);PlayerControl.EnableFresh(w,config);WorldIntegrity.Validate(w);return w;
    }
}
