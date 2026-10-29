namespace Cultivation.AI;

public sealed record MissionObservation(string Id, string OrganizationId, string ResourceId,
    long Remaining, long UnitPrice, long DeadlineTick, bool Accepted, string Source, long ObservedTick, double RelationshipWeight = 0);
