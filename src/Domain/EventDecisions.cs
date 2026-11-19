namespace Cultivation.Domain;

public enum DecisionTrigger { Startup, ActionCompleted, Interrupted, RestCompleted, KnownNotification, IdleRetry, StateChanged, PlayerAutonomy }
public sealed record DecisionRequest(string ActorId,long Sequence,long RequestedAt,HashSet<DecisionTrigger> Reasons);
public sealed record DecisionCandidate(ActionKind Kind,string TargetId,double Score,string Reason,string? Rejected);
public sealed record DecisionTrace(string ActorId,long AtSeconds,DecisionTrigger[] Reasons,bool Evaluated,string? SelectedAction,
    DecisionCandidate[] Candidates,string Detail)
{
    public KnownMarketFact[] Knowledge { get; init; } = [];
}
public sealed record KnownMarketFact(string ActorId,string RegionId,string LocationId,MarketQuote Quote,long ObservedAt,long ExpiresAt,decimal Confidence,string Source);
public sealed class EventDecisionState
{
    public int FormatVersion { get; set; } = 1;
    public long NextSequence { get; set; } = 1;
    public Dictionary<string,DecisionRequest> Ready { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string,DecisionTrace> Traces { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string,Dictionary<string,KnownMarketFact>> MarketKnowledge { get; set; } = new(StringComparer.Ordinal);
    public long BudgetTick { get; set; } = -1;
    public int BudgetUsed { get; set; }
    public long Evaluations { get; set; }
    public long PlannerCalls { get; set; }
}
