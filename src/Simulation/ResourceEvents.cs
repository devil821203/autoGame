using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ResourceEvents
{
    private const string Key = "$resource-regeneration";
    private static long Interval(WorldState w) => checked(w.HerbRegeneration!.IntervalTicks * DiscreteTime.SecondsPerStep(w));

    internal static void EnableFresh(WorldState w)
    {
        if(w.Time?.DiscreteEvents is not {WorldTime:0} events)
            throw new InvalidOperationException("Resource events require a fresh seconds world");
        w.Time.IndependentResourceEvents = true;
        if(w.HerbRegeneration is null)return;
        w.Time.ResourceRegenerationDueSeconds = Interval(w);
        new EventScheduler(events).Schedule(Interval(w),SimulationEventKind.ResourceRegeneration,Key);
    }

    internal static void Resolve(WorldState w,SimulationEvent item,EventScheduler queue)
    {
        if(w.Time?.IndependentResourceEvents!=true || item.CorrelationId!=Key || item.DueAt!=w.Time.ResourceRegenerationDueSeconds)
            throw new InvalidDataException("Unexpected resource regeneration event");
        ResourceRegeneration.Advance(w,item.DueAt / DiscreteTime.SecondsPerStep(w));
        ActionExecutor.Emit(w,Key,"ResourceRegenerationResolved",$"{item.DueAt}秒");
        long next=checked(item.DueAt+Interval(w));
        w.Time.ResourceRegenerationDueSeconds=next;
        queue.Schedule(next,SimulationEventKind.ResourceRegeneration,Key);
    }

    internal static void Validate(WorldState w)
    {
        var time=w.Time;
        var live=time?.DiscreteEvents?.Pending.Where(e=>e.Kind==SimulationEventKind.ResourceRegeneration&&e.Version==time.DiscreteEvents.Versions[e.CorrelationId]).ToArray()??[];
        if(time?.IndependentResourceEvents!=true){
            if(time?.ResourceRegenerationDueSeconds is not null||live.Length!=0)throw new InvalidDataException("Resource event requires seconds mode");
            return;
        }
        if(time.DiscreteEvents is not {} events)throw new InvalidDataException("Missing resource event clock");
        if(w.HerbRegeneration is null){
            if(time.ResourceRegenerationDueSeconds is not null||live.Length!=0)throw new InvalidDataException("Resource event without regeneration rules");
            return;
        }
        if(w.HerbRegeneration.IntervalTicks<=0||time.ResourceRegenerationDueSeconds is not {} due||due<events.WorldTime||due<=0||due%Interval(w)!=0||
            due-events.WorldTime>Interval(w)||live.Length!=1||live[0].DueAt!=due||live[0].CorrelationId!=Key)
            throw new InvalidDataException("Resource regeneration deadline/event mismatch");
    }
}
