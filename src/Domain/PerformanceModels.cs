namespace Cultivation.Domain;

public enum AiLod { Nearby, Regional, Remote }
public sealed record PerformanceRules(int NearbyIntervalTicks = 1, int RegionalIntervalTicks = 4,
    int RemoteIntervalTicks = 16, int RemoteCombatBatchTicks = 16, long DiagnosticRetentionTicks = 120,
    bool LogCandidateDetails = false);
public sealed record AiUpdateState(string ActorId, long LastTick, AiLod Lod, string RegionId, string LocationId);
