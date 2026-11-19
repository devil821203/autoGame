using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ScheduledEventIntegrity
{
    internal static void Validate(WorldState w)
    {
        var work=w.Scheduled.Select(t=>(t.Sequence,t.DueAtSeconds,t.Intent,Kind:SimulationEventKind.ScheduledWork))
            .Concat(w.Time?.Deferred.Select(t=>(t.Sequence,t.DueAtSeconds,t.Intent,Kind:SimulationEventKind.DeferredWork))??[]).ToArray();
        if(w.Time?.IndependentScheduledEvents!=true){if(work.Any(t=>t.DueAtSeconds is not null))throw new InvalidDataException("Seconds scheduled work requires current mode");return;}
        if(!w.Time.IndependentLifeEvents||w.Time.DiscreteEvents is not {} events)throw new InvalidDataException("Invalid independent work mode");
        foreach(var task in work){
            if(task.Sequence<=0||task.Sequence>=w.NextSequence||task.DueAtSeconds is not {} due||due<events.WorldTime||
                events.Pending.Count(e=>e.Kind==task.Kind&&e.CorrelationId==task.Intent.Id&&e.Detail==task.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)&&e.DueAt==due&&e.Version==events.Versions[e.CorrelationId])!=1)
                throw new InvalidDataException("Scheduled work deadline/event mismatch");
        }
    }
}
