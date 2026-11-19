using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class DiscreteTime
{
    // Unit conversion, not a balance setting. Old Tick remains a compatibility projection.
    public static long SecondsPerStep(WorldState w) => w.Time?.ActionDriven?.MinutesPerActionPoint is > 0
        ? checked(w.Time.ActionDriven.MinutesPerActionPoint.Value * 60L)
        : throw new InvalidOperationException("Discrete time requires a positive minute-mode snapshot");
    public static void EnableFresh(WorldState w, GameConfiguration? configuration = null)
    {
        if (w.Clock.Tick != 0 || w.Time is null || w.Time.DiscreteEvents is not null || w.Pending.Count != 0 ||
            w.Scheduled.Count != 0 || w.MortalTrainingWork.Count != 0 || w.Time.Deferred.Count != 0 ||
            w.LifeSystem?.Work.Count > 0 || w.Time.Charges.Count != 0 || w.Time.ApRestingActors.Count != 0 ||
            w.Characters.Any(a => a.Stamina?.Resting == true) || w.BoardBattle is { Phase: BoardBattlePhase.Active })
            throw new InvalidOperationException("Discrete time is opt-in for a new idle world; no old-save migration");
        var events = new DiscreteEventState { Rules = (configuration ?? GameConfiguration.Find()).Read<EventQueueRules>("time/discrete-events.json") };
        var scheduler = new EventScheduler(events);
        scheduler.Schedule(SecondsPerStep(w), SimulationEventKind.CompatibilityStep, "$compatibility");
        w.Time.DiscreteEvents = events;
        Validate(w);
    }
    public static void Validate(WorldState w)
    {
        if (w.Time?.DiscreteEvents is not { } events) return;
        EventScheduler.Validate(events);
        long step = SecondsPerStep(w);
        bool pendingBoundary=events.WorldTime%step==0&&events.WorldTime/step==w.Clock.Tick+1&&events.Pending.Any(e=>e.Kind==SimulationEventKind.CompatibilityStep&&e.DueAt==events.WorldTime&&e.Version==events.Versions[e.CorrelationId]);
        if (events.WorldTime / step != w.Clock.Tick&&!pendingBoundary ||
            (!events.IndependentWorldEvents && events.Pending.Count(e => e.Kind == SimulationEventKind.CompatibilityStep && e.Version == events.Versions[e.CorrelationId]) != 1) ||
            (events.IndependentWorldEvents && events.Pending.Any(e => e.Kind == SimulationEventKind.CompatibilityStep && e.Version == events.Versions[e.CorrelationId])) ||
            events.Pending.Any(e => e.Kind == SimulationEventKind.CompatibilityStep && e.Version == events.Versions[e.CorrelationId] &&
                (e.CorrelationId != "$compatibility" || e.DueAt / step != w.Clock.Tick + 1 || e.DueAt % step != 0)) ||
            events.Pending.Any(e => e.ActorId is not null && !w.Characters.Any(a => a.Id == e.ActorId)) ||
            w.MortalTrainingWork.Any(t => t.DueAtSeconds is not { } due || due < events.WorldTime ||
                t.DueTick != due / step + (due % step == 0 ? 0 : 1)))
            throw new InvalidDataException("Invalid discrete clock, compatibility bridge or training deadline");
        foreach (var task in w.MortalTrainingWork)
            if (events.Pending.Count(e => e.Kind == SimulationEventKind.MortalTraining && e.CorrelationId == task.Intent.Id &&
                e.ActorId == task.Intent.ActorId && e.DueAt == task.DueAtSeconds && e.Version == events.Versions[e.CorrelationId]) != 1)
                throw new InvalidDataException("Training requires exactly one live completion event");
    }
}
