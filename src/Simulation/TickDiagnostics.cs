namespace Cultivation.Simulation;

public sealed record TickDiagnostics(long Tick, double TotalMilliseconds, double CloneMilliseconds,
    double AiMilliseconds, double ValidationMilliseconds, int Decisions, int PendingEvents, int Trades,
    int NpcCount, long AllocatedBytes, long ManagedMemoryBytes);
