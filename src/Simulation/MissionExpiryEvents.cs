using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class MissionExpiryEvents
{
    private static string Key(string id)=>"$mission-expiry:"+id;
    internal static long Due(WorldState w,MissionState mission)=>checked(mission.DeadlineTick*DiscreteTime.SecondsPerStep(w));
    internal static bool HasDue(WorldState w,long now)=>w.Time?.IndependentMissionExpiryEvents==true&&
        w.Time.DiscreteEvents!.Pending.Any(e=>e.Kind==SimulationEventKind.MissionExpiry&&e.DueAt<=now&&e.Version==w.Time.DiscreteEvents.Versions[e.CorrelationId]);
    internal static void Sync(WorldState w,EventScheduler queue)
    {
        if(w.Time?.IndependentMissionExpiryEvents!=true)return;
        var events=w.Time.DiscreteEvents!;
        foreach(var e in events.Pending.Where(e=>e.Kind==SimulationEventKind.MissionExpiry&&e.Version==events.Versions[e.CorrelationId]&&
            !w.Missions.Any(m=>m.Id==e.Detail&&m.Status==MissionStatus.Active)).ToArray())queue.Cancel(e.CorrelationId);
        foreach(var mission in w.Missions.Where(m=>m.Status==MissionStatus.Active)){
            long due=Due(w,mission);string key=Key(mission.Id);
            if(!events.Pending.Any(e=>e.Kind==SimulationEventKind.MissionExpiry&&e.CorrelationId==key&&e.Version==events.Versions[e.CorrelationId]))
                queue.Schedule(due,SimulationEventKind.MissionExpiry,key,detail:mission.Id);
        }
    }
    internal static void Validate(WorldState w)
    {
        var events=w.Time?.DiscreteEvents;
        var live=events?.Pending.Where(e=>e.Kind==SimulationEventKind.MissionExpiry&&e.Version==events.Versions[e.CorrelationId]).ToArray()??[];
        if(w.Time?.IndependentMissionExpiryEvents!=true){if(live.Length!=0)throw new InvalidDataException("Mission expiry requires seconds mode");return;}
        if(events is null)throw new InvalidDataException("Missing mission expiry clock");
        var active=w.Missions.Where(m=>m.Status==MissionStatus.Active).ToArray();
        if(live.Length!=active.Length||active.Any(m=>Due(w,m)<events.WorldTime||live.Count(e=>e.CorrelationId==Key(m.Id)&&e.Detail==m.Id&&e.DueAt==Due(w,m))!=1))
            throw new InvalidDataException("Mission expiry deadline/event mismatch");
    }
}
