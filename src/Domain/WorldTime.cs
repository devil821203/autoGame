namespace Cultivation.Domain;

public sealed record CalendarDate(long Year, string Season, int Day, int TickOfDay);
public sealed record TimeRules(int TicksPerDay = 30, int DaysPerSeason = 30, int DailyActionPoints = 30,
    int BattleActionPoints = 5, int MaximumBattleRounds = 100);
public sealed record ActionPointBudget(string ActorId, long Day, int Remaining, long BusyThroughTick = -1);
public sealed record ActionPointCharge(string ActionId, string ActorId, long Tick, long Day, int Cost);
public sealed record DeferredWorldAction(ActionIntent Intent, long DueTick);
public sealed record BattleTimeReceipt(string BattleId, int RemainingTicks);
public sealed record ActionDrivenRules(int IdleRetryTicks, int MaximumAdvanceSteps)
{
    public int? WaitActionPoints { get; init; }
    public int? WaitDurationTicks { get; init; }
    public int? MinutesPerActionPoint { get; init; }
    public int? MinutesPerDay { get; init; }
    public int? RestActionPointsPerStep { get; init; }
    public int? TrainingBatchLimit { get; init; }
}
public sealed record IdleDecisionWait(long LastTick, long RetryTick, int Health, string LocationId);
public sealed class WorldTimeState
{
    public ActionDrivenRules? ActionDriven { get; set; }
    public Dictionary<string, IdleDecisionWait> IdleDecisions { get; set; } = [];
    public HashSet<string> ApRestingActors { get; set; } = [];
    public TimeRules Rules { get; set; } = new();
    public Dictionary<ActionKind, int> Costs { get; set; } = [];
    public List<ActionPointBudget> Budgets { get; set; } = [];
    public List<ActionPointCharge> Charges { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public HashSet<(string ActionId, string ActorId)>? ChargeIndex { get; set; }
    public List<DeferredWorldAction> Deferred { get; set; } = [];
    public List<BattleTimeReceipt> BattleTime { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public string? CompletingActionId { get; set; }
}
public static class CalendarCalculator
{
    public static CalendarDate Date(long tick, TimeRules rules)
    {
        long days = tick / rules.TicksPerDay;
        long season = days / rules.DaysPerSeason;
        return new(season / 4 + 1, new[] { "春", "夏", "秋", "冬" }[season % 4],
            (int)(days % rules.DaysPerSeason) + 1, (int)(tick % rules.TicksPerDay));
    }
}
