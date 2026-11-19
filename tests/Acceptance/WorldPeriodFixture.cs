using Cultivation.Domain;
using Cultivation.Simulation;

internal static class WorldPeriodFixture
{
    // Tests that change policy after scenario creation must rebuild the matching deadlines.
    internal static void Reset(WorldState w)
    {
        var events = w.Time!.DiscreteEvents!;
        var queue = new EventScheduler(events);
        foreach (string key in events.PeriodicDeadlines.Keys.Where(k => k == "$sect-publication" ||
            k.StartsWith("$member-review:", StringComparison.Ordinal) || k.StartsWith("$procurement-", StringComparison.Ordinal)).ToArray())
        {
            queue.Cancel(key);
            events.PeriodicDeadlines.Remove(key);
        }
        if (w.SectDuty is not { } duty) w.MemberDuties.Clear();
        else
        {
            Add("$sect-publication", SimulationEventKind.SectPublication, duty.MissionIntervalTicks);
            foreach (var member in w.MemberDuties)
                Add("$member-review:" + member.ActorId, SimulationEventKind.MemberReview, duty.ReviewIntervalTicks, member.ActorId);
        }
        if (w.TestProcurement is { } procurement)
        {
            Add("$procurement-purchase", SimulationEventKind.ProcurementPurchase, 1);
            Add("$procurement-review", SimulationEventKind.ProcurementReview, procurement.IntervalTicks);
        }
        void Add(string key, SimulationEventKind kind, long ticks, string? actor = null)
        {
            long due = checked(events.WorldTime + ticks * DiscreteTime.SecondsPerStep(w));
            events.PeriodicDeadlines.Add(key, due);
            queue.Schedule(due, kind, key, actor);
        }
    }
}