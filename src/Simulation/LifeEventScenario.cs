using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class LifeEventScenario
{
    public static WorldState Create(string name="行者",GameConfiguration? configuration=null)
    {
        var config=configuration??GameConfiguration.Find();var w=PlayerControlScenario.Create(name,configuration:config);
        w.Time!.IndependentLifeEvents=true;
        w.Time.IndependentScheduledEvents=true;
        w.Time.IndependentRecoveryEvents=true;
        ResourceEvents.EnableFresh(w);
        PayrollEvents.EnableFresh(w);
        w.Time.IndependentMissionExpiryEvents=true;
        w.Time.IndependentSocialExpiryEvents=true;
        MissionExpiryEvents.Sync(w,new EventScheduler(w.Time.DiscreteEvents!));
        foreach(var stock in config.Read<Dictionary<string,long>>("life/test-stock.json"))
            w.Ledger.Add(new(w.NextSequence++,0,"independent-life-test-stock","$genesis",w.ControlledCharacterId,stock.Key,stock.Value,"Genesis"));
        WorldPeriodEvents.EnableFresh(w);
        WorldIntegrity.Validate(w);return w;
    }
}
