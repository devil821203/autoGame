using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class PerformanceIntegrity
{
    internal static void Validate(WorldState w)
    {
        if (w.Performance is not { } r)
        {
            if (w.AiUpdates.Count != 0 || w.ScaleNpcCount is not null) throw new InvalidDataException("LOD state without settings");
            return;
        }
        if (r.NearbyIntervalTicks < 1 || r.RegionalIntervalTicks < r.NearbyIntervalTicks || r.RemoteIntervalTicks < r.RegionalIntervalTicks || r.RemoteIntervalTicks > 1000000 ||
            r.RemoteCombatBatchTicks is < 1 or > 1000 || r.DiagnosticRetentionTicks < 1 || w.ScaleNpcCount is < 1 or > 20000) throw new InvalidDataException("Invalid performance settings");
        var ids = w.Characters.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (w.AiUpdates.Select(s => s.ActorId).Distinct().Count() != w.AiUpdates.Count || w.AiUpdates.Any(s => !ids.Contains(s.ActorId) || s.LastTick < 0 || s.LastTick > w.Clock.Tick || !Enum.IsDefined(s.Lod))) throw new InvalidDataException("Invalid persisted LOD state");
    }
    internal static void TrimDiagnostics(WorldState w)
    {
        if (w.Performance is not { } r) return;
        long threshold = w.Clock.Tick - r.DiagnosticRetentionTicks;
        w.Events.RemoveAll(e => e.Tick < threshold && e.Kind is "Decision" or "DecisionBudget" or "PlanInvalidated" or "PartyWaiting");
    }
}
