namespace Cultivation.Domain;

public enum SimulationEventKind { CompatibilityStep, MortalTraining, Signal, DecisionRetry, MarketCommand, MarketOrderExpiry, EquipmentRepair, LifeWork, ScheduledWork, DeferredWork, RestRecovery, MedicinePulse, ResourceRegeneration, SectPayroll, MissionExpiry, SocialExpiry, CalendarDay, SettlementSettlement, ProcurementPurchase, ProcurementReview, SectPublication, MemberReview, HuntPublication, PartyExpiry, DecisionDrain, BattleCatchup, BattleRelease }
public enum AdvanceStopReason { TargetReached, ImportantEvent, EventBudget, SameTimeBudget, PlayerBattle, ControlRequired }
public sealed record EventQueueRules(string TimeUnit, int MaximumEventsPerAdvance, int MaximumEventsAtSameTime);
public sealed record SimulationEvent(long EventId, long DueAt, long SequenceId, SimulationEventKind Kind,
    string CorrelationId, long Version, string? ActorId = null, string? Detail = null, bool Blocking = false);
public sealed record AdvanceResult(long WorldTime, int Processed, int Discarded, AdvanceStopReason Reason, string? Detail = null);
public sealed class DiscreteEventState
{
    public int FormatVersion { get; set; } = 1;
    public EventQueueRules Rules { get; set; } = null!;
    public long WorldTime { get; set; }
    public bool IndependentWorldEvents { get; set; }
    public Dictionary<string, long> PeriodicDeadlines { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> ActionSequences { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> BattleReleaseDeadlines { get; set; } = new(StringComparer.Ordinal);
    public long NextSequence { get; set; } = 1;
    public List<SimulationEvent> Pending { get; set; } = [];
    public Dictionary<string, long> Versions { get; set; } = new(StringComparer.Ordinal);
    public HashSet<long> ConsumedEventIds { get; set; } = [];
}
