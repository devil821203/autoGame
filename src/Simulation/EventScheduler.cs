using Cultivation.Domain;

namespace Cultivation.Simulation;

// Persist only data; rebuild the heap after loading. No delegates enter a world snapshot.
// The caller owns the transaction for resolver effects (SimulationEngine clones/rolls back the world).
public sealed class EventScheduler
{
    private readonly DiscreteEventState state;
    private readonly PriorityQueue<SimulationEvent, (long DueAt, int Phase, long Accepted, long Sequence)> heap = new();
    public EventScheduler(DiscreteEventState state)
    {
        Validate(state);
        this.state = state;
        foreach (var item in state.Pending) heap.Enqueue(item, Priority(item));
    }
    public long WorldTime => state.WorldTime;
    public SimulationEvent Schedule(long dueAt, SimulationEventKind kind, string correlationId,
        string? actorId = null, string? detail = null, bool blocking = false)
    {
        if (dueAt < WorldTime || !Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Event must have a valid kind/id and cannot precede WorldTime");
        long sequence = state.NextSequence;
        long next = checked(sequence + 1);
        long version = state.Versions.GetValueOrDefault(correlationId);
        var item = new SimulationEvent(sequence, dueAt, sequence, kind, correlationId, version, actorId, detail, blocking);
        state.NextSequence = next;
        state.Versions.TryAdd(correlationId, version);
        state.Pending.Add(item); heap.Enqueue(item, Priority(item));
        return item;
    }
    public void Cancel(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("Missing correlation id");
        state.Versions[correlationId] = checked(state.Versions.GetValueOrDefault(correlationId) + 1);
    }
    private (long DueAt, int Phase, long Accepted, long Sequence) Priority(SimulationEvent item)
    {
        int phase = !state.IndependentWorldEvents ? 0 : item.Kind switch
        {
            SimulationEventKind.CalendarDay => 1,
            SimulationEventKind.MarketOrderExpiry or SimulationEventKind.MissionExpiry or SimulationEventKind.SocialExpiry or SimulationEventKind.PartyExpiry => 2,
            SimulationEventKind.Signal or SimulationEventKind.ResourceRegeneration => 3,
            SimulationEventKind.SettlementSettlement => 5,
            SimulationEventKind.ProcurementPurchase => 6,
            SimulationEventKind.ProcurementReview => 7,
            SimulationEventKind.MemberReview => 8,
            SimulationEventKind.SectPublication or SimulationEventKind.HuntPublication => 9,
            SimulationEventKind.SectPayroll => 10,
            SimulationEventKind.DecisionRetry => 11,
            SimulationEventKind.DecisionDrain => 12,
            _ => 4
        };
        long accepted = state.IndependentWorldEvents
            ? state.ActionSequences.GetValueOrDefault(item.CorrelationId, item.Detail is null ? item.SequenceId : state.ActionSequences.GetValueOrDefault(item.Detail, item.SequenceId))
            : item.SequenceId;
        return (item.DueAt, phase, accepted, item.SequenceId);
    }

    public AdvanceResult AdvanceTo(long targetTime, Action<SimulationEvent> resolve, Func<bool>? playerBattle = null)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        if (targetTime < WorldTime) throw new ArgumentOutOfRangeException(nameof(targetTime));
        int processed = 0, discarded = 0, sameTime = 0;
        long lastTime = -1;
        while (heap.TryPeek(out var item, out _) && item.DueAt <= targetTime)
        {
            if (processed + discarded >= state.Rules.MaximumEventsPerAdvance)
                return new(WorldTime, processed, discarded, AdvanceStopReason.EventBudget);
            if (item.DueAt != lastTime) { sameTime = 0; lastTime = item.DueAt; }
            if (sameTime >= state.Rules.MaximumEventsAtSameTime)
                return new(WorldTime, processed, discarded, AdvanceStopReason.SameTimeBudget);
            sameTime++;
            heap.Dequeue(); state.Pending.Remove(item);
            state.WorldTime = item.DueAt;
            state.ConsumedEventIds.Add(item.EventId);
            if (state.Versions[item.CorrelationId] != item.Version) { discarded++; continue; }
            resolve(item); processed++;
            if (playerBattle?.Invoke() == true) return new(WorldTime, processed, discarded, AdvanceStopReason.PlayerBattle);
            if (item.Blocking) return new(WorldTime, processed, discarded, AdvanceStopReason.ImportantEvent, item.Detail);
        }
        state.WorldTime = targetTime;
        return new(WorldTime, processed, discarded, AdvanceStopReason.TargetReached);
    }
    public static void Validate(DiscreteEventState state)
    {
        if (state.FormatVersion != 1 || state.Rules is not { TimeUnit: "Seconds", MaximumEventsPerAdvance: > 0, MaximumEventsAtSameTime: > 0 } ||
            state.WorldTime < 0 || state.NextSequence < 1 || state.Pending is null || state.Versions is null ||
            state.ConsumedEventIds is null || state.ConsumedEventIds.Any(id => id <= 0 || id >= state.NextSequence) ||
            state.Versions.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0) ||
            state.Pending.Select(e => e.EventId).Distinct().Count() != state.Pending.Count ||
            state.Pending.Select(e => e.SequenceId).Distinct().Count() != state.Pending.Count ||
            state.Pending.Any(e => e.EventId <= 0 || e.EventId != e.SequenceId || e.SequenceId >= state.NextSequence ||
                state.ConsumedEventIds.Contains(e.EventId) || e.DueAt < state.WorldTime || !Enum.IsDefined(e.Kind) || string.IsNullOrWhiteSpace(e.CorrelationId) ||
                !state.Versions.TryGetValue(e.CorrelationId, out long version) || e.Version < 0 || e.Version > version))
            throw new InvalidDataException("time/discrete-events.json: invalid rules or persisted event queue");
    }
}
