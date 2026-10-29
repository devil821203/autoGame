using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class IntentDeduplication
{
    internal static bool Contains(WorldState w, string id)
    {
        if (w.ProcessedIntentIndex is null || w.ProcessedIntentIndex.Count != w.ProcessedIntentIds.Count)
            w.ProcessedIntentIndex = w.ProcessedIntentIds.ToHashSet(StringComparer.Ordinal);
        return w.ProcessedIntentIndex.Contains(id);
    }
    internal static void Record(WorldState w, string id)
    {
        if (Contains(w, id)) throw new InvalidOperationException("Duplicate processed intent");
        w.ProcessedIntentIds.Add(id); w.ProcessedIntentIndex!.Add(id);
    }
}
