using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ResourceRegeneration
{
    internal static void Advance(WorldState w)
    {
        if (w.HerbRegeneration is not { } r || w.Clock.Tick % r.IntervalTicks != 0) return;
        long space = Math.Max(0, r.Capacity - Inventory.Balance(w, "grove", "herb"));
        long amount = Math.Min(r.Amount, space);
        if (amount == 0) return;
        string id = "regeneration:" + w.Clock.Tick;
        w.Ledger.Add(new(w.NextSequence++, w.Clock.Tick, id, "$regeneration", "grove", "herb", amount, "Regenerated"));
        ActionExecutor.Emit(w, id, "ResourceRegenerated", $"grove:herb:+{amount}");
        long eventSequence = w.Events[^1].Sequence;
        string region = w.Organizations.Single(o => o.Id == "grove").RegionId;
        var travelers = w.Scheduled.Where(t => t.Intent.Kind == ActionKind.Travel).Select(t => t.Intent.ActorId).ToHashSet(StringComparer.Ordinal);
        foreach (var actor in w.Characters.Where(c => c.RegionId == region && c.Health > 0 &&
            (!w.DemoMapEnabled || c.DemoLocationId == "grove") && !travelers.Contains(c.Id)).OrderBy(c => c.Id, StringComparer.Ordinal))
            w.Notifications.Add(new(eventSequence, actor.Id));
    }
}
