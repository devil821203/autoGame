using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class TimeActions
{
    internal static bool IsBattleCommand(ActionKind k) => k is ActionKind.BoardTurn or ActionKind.SetBoardAutoCombat or ActionKind.UseCombatMedicine;
    internal static int Cost(WorldState w, ActionIntent i)
    {
        var time = w.Time!;
        if (IsBattleCommand(i.Kind)) return 0;
        if (i.Kind == ActionKind.Wait && time.ActionDriven?.WaitActionPoints is { } waitCost) return waitCost;
        if (i.Kind == ActionKind.Rest && time.ActionDriven?.RestActionPointsPerStep is > 0) return 0;
        if(i.Kind==ActionKind.InvestSettlementPopulation)return w.PopulationInvestmentRules?.ActionPoints??0;
        if(OrganizationLaborActions.Handles(i.Kind))return w.OrganizationLaborRules?.ActionPoints??0;
        if (ExternalMarketActions.Handles(i.Kind)) return w.ExternalMarket?.Rules.ActionPoints ?? 0;
        if (i.Kind == ActionKind.RequestLifeService) return w.PaidServiceRules?.RequestActionPoints ?? 0;
        if (MortalTrainingActions.Handles(i.Kind)) return checked((MethodTraining.IsMethod(i) ? w.Methods?.Catalog.TrainingRules.ActionPointCost ?? 0 : w.MortalActionRules?.ActionPoints ?? 0) * checked((int)i.Quantity));
        if (LifeActions.Handles(i.Kind) && w.LifeSystem is not null) return LifeActions.Cost(w, i);
        if (i.Kind == ActionKind.Engage) return time.Rules.BattleActionPoints;
        if (i.DurationTicks > 0) return checked((int)i.DurationTicks);
        if (i.Kind == ActionKind.Travel) return checked((int)HexNavigation.Duration(w, w.Characters.Single(c => c.Id == i.ActorId), i.TargetId));
        return time.Costs.GetValueOrDefault(i.Kind, 1);
    }
    internal static long Day(WorldState w) => w.Clock.Tick / w.Time!.Rules.TicksPerDay;
    internal static ActionPointBudget Budget(WorldState w, string actorId) => w.Time!.Budgets.SingleOrDefault(b => b.ActorId == actorId)
        ?? new(actorId, Day(w), w.Time.Rules.DailyActionPoints);
    internal static bool NeedsApRest(WorldState w, string id) => w.Time?.ActionDriven?.RestActionPointsPerStep is > 0 && Budget(w, id).Remaining < w.Time.Rules.DailyActionPoints;
    internal static void RecoverRestAp(WorldState w, HashSet<string> resting)
    {
        if (w.Time?.ActionDriven?.RestActionPointsPerStep is not > 0) return;
        foreach (string id in resting) {
            var actor = w.Characters.Single(a => a.Id == id);
            if (actor.Health <= 0 || BoardBattleActions.Participant(w, id) || CombatActions.Active(w, id) is not null) continue;
            var budget = Budget(w, id); int amount = w.Time.ActionDriven.RestActionPointsPerStep.Value;
            int value = (int)Math.Min(w.Time.Rules.DailyActionPoints, (long)budget.Remaining + amount);
            int n = w.Time.Budgets.FindIndex(b => b.ActorId == id); w.Time.Budgets[n] = budget with { Remaining = value };
            if (value != budget.Remaining) ActionExecutor.Emit(w, "rest-ap:" + id + ":" + w.Clock.Tick, "RestActionPointsRecovered", $"{id}:{budget.Remaining}->{value}");
            if (value == w.Time.Rules.DailyActionPoints) w.Time.ApRestingActors.Remove(id);
        }
    }
    internal static void Refresh(WorldState w)
    {
        var t = w.Time!; long day = Day(w);
        var indices = t.Budgets.Select((budget, index) => (budget.ActorId, index)).ToDictionary(p => p.ActorId, p => p.index, StringComparer.Ordinal);
        foreach (var actor in w.Characters)
        {
            if (!indices.TryGetValue(actor.Id, out int n)) t.Budgets.Add(new(actor.Id, day, t.Rules.DailyActionPoints));
            else if (t.Budgets[n].Day != day) t.Budgets[n] = t.Budgets[n] with { Day = day, Remaining = t.Rules.DailyActionPoints };
        }
    }
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.Time is null || IsBattleCommand(i.Kind) || w.Time.CompletingActionId == i.Id) return null;
        var b = Budget(w, i.ActorId);
        if (b.BusyThroughTick > w.Clock.Tick || w.Time.Deferred.Any(d => d.Intent.ActorId == i.ActorId)) return "Actor busy in timed action";
        int cost;
        try { cost = Cost(w, i); } catch (Exception e) when (e is InvalidOperationException or OverflowException or ArgumentException) { return "Invalid timed action cost or route"; }
        int remaining = b.Day == Day(w) ? b.Remaining : w.Time.Rules.DailyActionPoints;
        if (cost < 0 || cost == 0 && !(i.Kind == ActionKind.Wait && w.Time.ActionDriven?.WaitActionPoints == 0 ||
            i.Kind == ActionKind.Rest && w.Time.ActionDriven?.RestActionPointsPerStep is > 0) || cost > remaining) return "Insufficient daily AP";
        if (i.Kind == ActionKind.Engage && (Budget(w, i.TargetId).BusyThroughTick > w.Clock.Tick && !w.MortalTrainingWork.Any(t => t.Intent.ActorId == i.TargetId) &&
            w.LifeSystem?.Work.Any(t => t.Service is { } c && (c.ProviderId == i.TargetId || c.ClientId == i.TargetId)) != true ||
            w.Time.Deferred.Any(d => d.Intent.ActorId == i.TargetId) || BoardBattleActions.Participant(w, i.TargetId))) return "Defender busy";
        if (i.Kind == ActionKind.ExploreDungeon && remaining - cost < w.Time.Rules.BattleActionPoints) return "Reserve battle AP before dungeon exploration";
        return null;
    }
    internal static void Spend(WorldState w, string id, string actor, int cost, int? busyTicks = null)
    {
        var t = w.Time!;
        if (t.ChargeIndex is null || t.ChargeIndex.Count != t.Charges.Count) t.ChargeIndex = t.Charges.Select(c => (c.ActionId, c.ActorId)).ToHashSet();
        if (t.ChargeIndex.Contains((id, actor))) return;
        Refresh(w); var b = Budget(w, actor);
        if (b.Remaining < cost) throw new InvalidOperationException("AP reservation changed");
        t.Budgets[t.Budgets.FindIndex(x => x.ActorId == actor)] = b with {
            Remaining = b.Remaining - cost, BusyThroughTick = Math.Max(b.BusyThroughTick, checked(w.Clock.Tick + (busyTicks ?? cost))) };
        t.Charges.Add(new(id, actor, w.Clock.Tick, Day(w), cost));
        t.ChargeIndex.Add((id, actor));
        ActionExecutor.Emit(w, id, "ActionPointsSpent", $"{actor}:{cost}:{b.Remaining - cost}");
    }
    internal static bool Scheduled(ActionIntent i) => i.DurationTicks > 0 || i.Kind is ActionKind.Travel or ActionKind.Gather or ActionKind.Brew
        or ActionKind.Cultivate or ActionKind.Heal or ActionKind.LearnMethod or ActionKind.Breakthrough or ActionKind.SectGather or ActionKind.SectBrew or ActionKind.ConstructBuilding;
    internal static void ValidateWorld(WorldState w)
    {
        if (w.Time is not { } t) return;
        if (t.ActionDriven is { } mode && (mode.IdleRetryTicks <= 0 || mode.MaximumAdvanceSteps <= 0) ||
            t.IdleDecisions.Any(p => !w.Characters.Any(a => a.Id == p.Key) || p.Value.LastTick > w.Clock.Tick || p.Value.RetryTick <= p.Value.LastTick))
            throw new InvalidDataException("time/action-driven.json: invalid retry/advance limits or idle state");
        if (t.ActionDriven is { } driven && (driven.WaitActionPoints is < 0 or > 30 || driven.WaitDurationTicks is <= 0 ||
            (driven.WaitActionPoints is null) != (driven.WaitDurationTicks is null) || driven.MinutesPerActionPoint is <= 0 || driven.MinutesPerDay is <= 0 ||
            (driven.MinutesPerActionPoint is null) != (driven.MinutesPerDay is null) || driven.RestActionPointsPerStep is <= 0 or > 30 || driven.TrainingBatchLimit is <= 0))
            throw new InvalidDataException("time/action-driven.json: invalid wait AP/duration");
        var r = t.Rules;
        int expectedDay = t.ActionDriven?.MinutesPerActionPoint is > 0 && t.ActionDriven.MinutesPerDay is > 0 ?
            t.ActionDriven.MinutesPerDay.Value / t.ActionDriven.MinutesPerActionPoint.Value : 30;
        if (t.ActionDriven is { MinutesPerActionPoint: > 0 } minuteMode && (minuteMode.MinutesPerDay is not > 0 ||
            minuteMode.MinutesPerDay % minuteMode.MinutesPerActionPoint != 0 || minuteMode.RestActionPointsPerStep is not > 0) ||
            t.ApRestingActors.Any(id => !w.Characters.Any(a => a.Id == id)) || t.ApRestingActors.Count > 0 && t.ActionDriven?.RestActionPointsPerStep is not > 0)
            throw new InvalidDataException("Invalid minute/rest AP rules");
        if (r.TicksPerDay != expectedDay || r.DaysPerSeason != 30 || r.DailyActionPoints != 30 || r.BattleActionPoints is < 1 or > 30 ||
            r.MaximumBattleRounds is < 1 or > 1000 || t.Costs.Any(p => !Enum.IsDefined(p.Key) || p.Value is < 1 or > 30) ||
            t.Budgets.Select(b => b.ActorId).Distinct().Count() != t.Budgets.Count ||
            t.Budgets.Any(b => !w.Characters.Any(c => c.Id == b.ActorId) || b.Day != Day(w) || b.Remaining is < 0 or > 30 || b.BusyThroughTick < -1) ||
            t.Charges.Select(c => (c.ActionId, c.ActorId)).Distinct().Count() != t.Charges.Count ||
            t.Charges.Any(c => !w.Characters.Any(a => a.Id == c.ActorId) || c.Tick < 0 || c.Tick > w.Clock.Tick || c.Day != c.Tick / r.TicksPerDay || c.Cost is < 1 or > 30) ||
            t.Deferred.Select(d => d.Intent.Id).Distinct().Count() != t.Deferred.Count ||
            t.Deferred.Any(d => d.DueTick <= w.Clock.Tick || !w.ProcessedIntentIds.Contains(d.Intent.Id) || !w.Characters.Any(c => c.Id == d.Intent.ActorId)) ||
            t.BattleTime.Select(b => b.BattleId).Distinct().Count() != t.BattleTime.Count || t.BattleTime.Any(b => b.RemainingTicks < 0 || b.RemainingTicks > r.BattleActionPoints))
            throw new InvalidDataException("Invalid calendar or AP state");
    }
}
