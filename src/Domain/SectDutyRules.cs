namespace Cultivation.Domain;

public sealed record SectDutyRules(string OrganizationId = "sect", long MissionIntervalTicks = 20,
    long ReviewIntervalTicks = 30, int MissLimit = 3, long PillQuantity = 20, long HideQuantity = 20,
    long PillReward = 120, long HideReward = 15, long MissionLifetimeTicks = 0);
public sealed record MemberDuty(string OrganizationId, string ActorId, long JoinedTick, long LastReviewTick, int Misses = 0);
