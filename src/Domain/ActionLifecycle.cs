namespace Cultivation.Domain;

public enum ActionPhase { Proposed, Validated, Reserved, Running, Completed, Interrupted, Failed, Cancelled }
public enum ConsumptionPolicy { PaidUpfrontNoRefund, PerCompletedSegment, EscrowRefundUnused }
public sealed record ActionTransition(ActionPhase Phase, long AtSeconds, string Reason);
public sealed record ActionInstance(string Id, string ActorId, string WorkerId, ActionKind Kind, long StartedAt,
    long DueAt, int TotalSegments)
{
    public string ResourceId { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public ActionPhase Phase { get; set; }
    public long Version { get; set; }
    public int CompletedSegments { get; set; }
    public long StaminaConsumed { get; set; }
    public int StaminaAtStart { get; set; }
    public int ActionPointsPaid { get; set; }
    public ConsumptionPolicy ApPolicy { get; init; } = ConsumptionPolicy.PaidUpfrontNoRefund;
    public ConsumptionPolicy StaminaPolicy { get; init; } = ConsumptionPolicy.PerCompletedSegment;
    public List<ActionTransition> Transitions { get; set; } = [];
}
public sealed record ResourceReservation(string Id, string ActionId, string OwnerId, string ResourceId, string Account, long Amount)
{
    public long Consumed { get; set; }
    public long Released { get; set; }
    public long Remaining => checked(Amount - Consumed - Released);
    public ConsumptionPolicy Policy { get; init; } = ConsumptionPolicy.EscrowRefundUnused;
}
public sealed class CapacityPool
{
    public int Capacity { get; set; }
    public Dictionary<string, int> Leases { get; set; } = new(StringComparer.Ordinal);
}
public sealed class ActionRuntimeState
{
    public int FormatVersion { get; set; } = 1;
    public Dictionary<string, ActionInstance> Actions { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ResourceReservation> Reservations { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CapacityPool> Capacities { get; set; } = new(StringComparer.Ordinal);
}
