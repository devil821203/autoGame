using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class TimeScenario
{
    public static WorldState Create(string name = "行者", GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = PlayerAutoScheduleScenario.Create(name, config);
        Enable(w, config);
        return w;
    }
    internal static void Enable(WorldState w, GameConfiguration config)
    {
        var rules = config.Read<TimeRules>("time/calendar.json");
        w.Time = new() { Rules = rules, Costs = config.Read<Dictionary<ActionKind, int>>("time/action-costs.json") };
        ScalePeriods(w, value => checked(value * rules.TicksPerDay));
        if (w.CombatRules is { } combat) w.CombatRules = combat with { MaximumRounds = rules.MaximumBattleRounds };
        TimeActions.Refresh(w); WorldIntegrity.Validate(w);
    }
    internal static void ScalePeriods(WorldState w, Func<long, long> Days)
    {
        if (w.SectPayroll is { } payroll) w.SectPayroll = payroll with { IntervalTicks = Days(payroll.IntervalTicks) };
        if (w.TestProcurement is { } procurement) w.TestProcurement = procurement with { IntervalTicks = Days(procurement.IntervalTicks) };
        if (w.SectDuty is { } duty) w.SectDuty = duty with { MissionIntervalTicks = Days(duty.MissionIntervalTicks),
            ReviewIntervalTicks = Days(duty.ReviewIntervalTicks), MissionLifetimeTicks = Days(duty.MissionLifetimeTicks) };
        if (w.HuntRules is { } hunt) w.HuntRules = hunt with { IntervalTicks = Days(hunt.IntervalTicks) };
        if (w.PartyRules is { } party) w.PartyRules = party with { InvitationTicks = Days(party.InvitationTicks) };
        if (w.HerbRegeneration is { } herbs) w.HerbRegeneration = herbs with { IntervalTicks = Days(herbs.IntervalTicks) };
        if (w.SocialRules is { } social) w.SocialRules = social with { OfferLifetimeTicks = Days(social.OfferLifetimeTicks), CooldownTicks = Days(social.CooldownTicks) };
        w.Rules = w.Rules with { MemoryTicks = Days(w.Rules.MemoryTicks), PlanLifetimeTicks = Days(w.Rules.PlanLifetimeTicks) };
    }
}
