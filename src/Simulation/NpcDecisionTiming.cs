using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class NpcDecisionTiming
{
    public static bool Ready(WorldState w, CharacterState actor, long latestVisibleNotificationTick = -1)
    {
        if (w.Time?.ActionDriven is null || !w.Time.IdleDecisions.TryGetValue(actor.Id, out var idle)) return true;
        return w.Clock.Tick >= idle.RetryTick || idle.Health != actor.Health || idle.LocationId != actor.DemoLocationId || latestVisibleNotificationTick > idle.LastTick;
    }
}
