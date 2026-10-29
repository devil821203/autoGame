using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class MinuteTimeProfile
{
    internal static void EnableFresh(WorldState w, GameConfiguration config)
    {
        var time = w.Time!; var mode = config.Read<ActionDrivenRules>("time/action-driven.json");
        if (mode.MinutesPerActionPoint is not > 0 || mode.MinutesPerDay is not > 0 || mode.MinutesPerDay % mode.MinutesPerActionPoint != 0)
            throw new InvalidDataException("time/action-driven.json: invalid minute units");
        int oldDay = time.Rules.TicksPerDay, newDay = mode.MinutesPerDay.Value / mode.MinutesPerActionPoint.Value;
        if (w.Clock.Tick != 0) throw new InvalidOperationException("Minute profile applies to a new world only");
        if (oldDay != newDay) TimeScenario.ScalePeriods(w, value => value % oldDay == 0 ? checked(value / oldDay * newDay) :
            throw new InvalidDataException("Cannot scale a non-day-based periodic setting"));
        time.Rules = time.Rules with { TicksPerDay = newDay };
        time.ActionDriven = mode; w.PlayerAutoSchedule = false;
    }
}
