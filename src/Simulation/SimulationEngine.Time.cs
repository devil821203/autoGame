using Cultivation.Domain;
using System.Diagnostics;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    private IReadOnlyList<ActionResult> StepTimed(bool autonomous, bool drainCatchup = true, bool cloneState = true, bool validate = true)
    {
        var watch = Stopwatch.StartNew(); long allocation = GC.GetAllocatedBytesForCurrentThread();
        var w = cloneState ? WorldCodec.Clone(state) : state; Inventory.BeginFrame(w); TimeActions.Refresh(w);
        var results = new List<ActionResult>();
        bool PlayerBattle() => w.BoardBattle is { Phase: BoardBattlePhase.Active } b && b.Fighters.Any(f => f.ActorId == w.ControlledCharacterId);
        if (PlayerBattle())
        {
            LifeActions.QueueCombatMedicine(w);
            if (autonomous) BoardBattleActions.QueueNpc(w);
            TimedPending(w, results, combatOnly: true);
            BoardBattleActions.QueuePlayerAuto(w); BoardBattleActions.Advance(w);
            FinishTimedBattle(w);
        }
        else
        {
            if (autonomous) AutonomousDecisions.Queue(w, planner);
            else if (w.PlayerAutoSchedule) AutonomousDecisions.Queue(w, planner, playerOnly: true);
            TimedPending(w, results, combatOnly: false);
            if (!PlayerBattle())
            {
                var paidRest = new HashSet<string>(StringComparer.Ordinal);
                foreach (var actor in w.Characters.Where(c => (c.Stamina?.Resting == true || w.Time!.ApRestingActors.Contains(c.Id)) && c.Health > 0))
                {
                    if (w.Time!.ActionDriven?.RestActionPointsPerStep is > 0) { paidRest.Add(actor.Id); continue; }
                    var budget = TimeActions.Budget(w, actor.Id);
                    if (budget.BusyThroughTick > w.Clock.Tick) paidRest.Add(actor.Id);
                    else if (budget.Remaining >= 1)
                    {
                        TimeActions.Spend(w, $"rest-tick:{actor.Id}:{w.Clock.Tick}", actor.Id, 1); paidRest.Add(actor.Id);
                    }
                }
                var catchup = w.Time!.BattleTime.FirstOrDefault(b => b.RemainingTicks > 0);
                w.Clock = w.Clock with { Tick = checked(w.Clock.Tick + 1) };
                TimeActions.Refresh(w);
                MissionActions.Expire(w); SocialActions.Expire(w);
                PartyActions.Advance(w); HuntActions.Advance(w); ResourceRegeneration.Advance(w);
                foreach (var task in w.Scheduled.OrderBy(t => t.DueTick).ThenBy(t => t.Sequence).ToArray())
                {
                    if (task.DueTick > w.Clock.Tick) continue;
                    if (task.Intent.Kind == ActionKind.Travel && w.HexMap is not null)
                    {
                        w.Scheduled.Remove(task);
                        if (HexTravel.CompleteStep(w, task.Intent)) CompletePlan(w, task.Intent);
                        continue;
                    }
                    if (task.Intent.Kind == ActionKind.ConstructBuilding) HexBuildingActions.Complete(w, task.Intent);
                    else if (task.Intent.Kind is ActionKind.SectGather or ActionKind.SectBrew) SectFacilityActions.Complete(w, task.Intent);
                    else if (task.Intent.Kind == ActionKind.Travel) TravelActions.Complete(w, task.Intent);
                    else if (AdvancementActions.Handles(task.Intent.Kind)) AdvancementActions.Complete(w, task.Intent);
                    else if (task.Intent.Kind != ActionKind.Transfer) ProductionActions.Complete(w, task.Intent);
                    else ActionExecutor.Transfer(w, task.Intent, ActionExecutor.Escrow(task.Intent.Id), task.Intent.TargetId, "Completed");
                    w.Scheduled.Remove(task); CompletePlan(w, task.Intent);
                }
                foreach (var task in w.Time.Deferred.Where(t => t.DueTick <= w.Clock.Tick).ToArray())
                {
                    w.Time.Deferred.Remove(task); w.Time.CompletingActionId = task.Intent.Id;
                    var error = ActionValidator.Validate(w, task.Intent);
                    if (error is null)
                    {
                        ActionExecutor.Execute(w, task.Intent); ConvertTimedCombats(w); CompletePlan(w, task.Intent);
                    }
                    else ActionExecutor.Emit(w, task.Intent.Id, "TimedCompletionRejected", error);
                    w.Time.CompletingActionId = null;
                }
                MortalTrainingActions.Complete(w);
                SettlementActions.Advance(w);
                foreach (var finished in LifeActions.Complete(w)) CompletePlan(w, finished);
                LifeActions.Effects(w);
                TimeActions.RecoverRestAp(w, paidRest);
                StaminaActions.Advance(w, actor => paidRest.Contains(actor.Id));
                TestProcurement.Advance(w); SectDuties.Advance(w); SectPayroll.Advance(w); PartyActions.Advance(w);
                if (catchup is not null)
                {
                    int n = w.Time.BattleTime.FindIndex(b => b.BattleId == catchup.BattleId);
                    w.Time.BattleTime[n] = catchup with { RemainingTicks = catchup.RemainingTicks - 1 };
                    ActionExecutor.Emit(w, catchup.BattleId, "BattleWorldTickReplayed", $"remaining:{catchup.RemainingTicks - 1}");
                }
                if (w.Clock.Tick % w.Time.Rules.TicksPerDay == 0)
                    ActionExecutor.Emit(w, "calendar:" + w.Clock.Tick, "DayStarted", CalendarCalculator.Date(w.Clock.Tick, w.Time.Rules).ToString());
            }
        }
        if (drainCatchup)
        {
            while (w.Time!.BattleTime.Any(r => r.RemainingTicks > 0) && !PlayerBattle())
            {
                var replay = new SimulationEngine(w);
                results.AddRange(replay.StepTimed(autonomous, drainCatchup: false)); w = replay.Snapshot();
            }
        }
        TimeActions.Refresh(w);
        w.Memories.RemoveAll(m => m.Tick < w.Clock.Tick - w.Rules.MemoryTicks);
        PerformanceIntegrity.TrimDiagnostics(w); if (validate) WorldIntegrity.Validate(w); state = w;
        LastDiagnostics = new(w.Clock.Tick, watch.Elapsed.TotalMilliseconds, 0, 0, 0, results.Count,
            w.Scheduled.Count + w.Time!.Deferred.Count, 0, w.Characters.Count - 1,
            GC.GetAllocatedBytesForCurrentThread() - allocation, GC.GetTotalMemory(false));
        return results;
    }

    private static void TimedPending(WorldState w, List<ActionResult> results, bool combatOnly)
    {
        var pending = w.Pending.OrderBy(p => p.Sequence).ToArray(); w.Pending.Clear();
        foreach (var queued in pending)
        {
            var i = queued.Intent;
            if (IntentDeduplication.Contains(w, i.Id)) { results.Add(new(i.Id, false, "Duplicate intent")); continue; }
            IntentDeduplication.Record(w, i.Id);
            bool frozen = w.BoardBattle is { Phase: BoardBattlePhase.Active } current && current.Fighters.Any(f => f.ActorId == w.ControlledCharacterId);
            string? error = (combatOnly || frozen) && !TimeActions.IsBattleCommand(i.Kind) ? "World paused for player battle" : ActionValidator.Validate(w, i);
            if (error is not null) { ActionExecutor.Emit(w, i.Id, "Rejected", error); results.Add(new(i.Id, false, error)); continue; }
            if (i.DecisionDetail is not null) ActionExecutor.Emit(w, i.Id, "Decision", i.DecisionDetail);
            if (i.Plan is not null) { w.Plans.RemoveAll(p => p.ActorId == i.ActorId); w.Plans.Add(i.Plan); }
            int cost = TimeActions.Cost(w, i);
            if (i.Kind == ActionKind.Wait && w.Time!.ActionDriven?.WaitDurationTicks is { } waitDuration)
            {
                if (cost > 0) TimeActions.Spend(w, i.Id, i.ActorId, cost, waitDuration);
                else {
                    TimeActions.Refresh(w); int n = w.Time.Budgets.FindIndex(b => b.ActorId == i.ActorId);
                    w.Time.Budgets[n] = w.Time.Budgets[n] with { BusyThroughTick = checked(w.Clock.Tick + waitDuration) };
                }
                w.Time.Deferred.Add(new(i, checked(w.Clock.Tick + waitDuration)));
            }
            else if (cost == 0) ActionExecutor.Execute(w, i);
            else
            {
                TimeActions.Spend(w, i.Id, i.ActorId, cost);
                if (i.Kind == ActionKind.Engage)
                {
                    StartTimedBattle(w, i.Id, i.ActorId, i.TargetId, chargeInitiator: false);
                }
                else if (i.Kind == ActionKind.Rest || i.Kind == ActionKind.RequestLifeService || LifeActions.IsWork(i.Kind) || MortalTrainingActions.Handles(i.Kind)) ActionExecutor.Execute(w, i);
                else if (TimeActions.Scheduled(i))
                {
                    ActionExecutor.Execute(w, i);
                    if (i.Kind != ActionKind.Travel || w.HexMap is null)
                    {
                        int n = w.Scheduled.FindIndex(t => t.Intent.Id == i.Id);
                        if (n >= 0) w.Scheduled[n] = w.Scheduled[n] with { DueTick = checked(w.Clock.Tick + cost) };
                        if (i.Kind == ActionKind.ConstructBuilding && w.HexMap is { } map)
                        {
                            int building = map.Buildings.FindIndex(b => b.IntentId == i.Id);
                            if (building >= 0) map.Buildings[building] = map.Buildings[building] with { DueTick = checked(w.Clock.Tick + cost) };
                        }
                    }
                }
                else w.Time!.Deferred.Add(new(i, checked(w.Clock.Tick + cost)));
            }
            results.Add(new(i.Id, true, "Accepted"));
        }
    }
    private static void ConvertTimedCombats(WorldState w)
    {
        foreach (var c in w.Combats.Where(c => c.Status == CombatStatus.Active).ToArray())
        {
            w.Combats.Remove(c); StartTimedBattle(w, c.Id, c.InitiatorId, c.DefenderId, chargeInitiator: true);
        }
    }
    private static void StartTimedBattle(WorldState w, string id, string initiator, string defender, bool chargeInitiator)
    {
        var previousBoard = w.BoardBattle;
        MortalTrainingActions.Interrupt(w, initiator); MortalTrainingActions.Interrupt(w, defender);
        PaidLifeServices.Interrupt(w, initiator); PaidLifeServices.Interrupt(w, defender);
        int battleCost = w.Time!.Rules.BattleActionPoints;
        var members = new List<string> { initiator };
        var location = w.Characters.Single(c => c.Id == initiator).DemoLocationId;
        if (PartyActions.For(w, initiator) is { } party)
            members.AddRange(party.Members.Where(m => m != initiator && w.Characters.Single(c => c.Id == m) is { Health: > 0 } actor &&
                actor.DemoLocationId == location && TimeActions.Budget(w, m).Remaining >= battleCost && TimeActions.Budget(w, m).BusyThroughTick <= w.Clock.Tick &&
                !w.Time!.Deferred.Any(t => t.Intent.ActorId == m) && !w.Scheduled.Any(t => t.Intent.ActorId == m)));
        foreach (var member in members)
            if (member != initiator || chargeInitiator) TimeActions.Spend(w, "battle-cost:" + id, member, battleCost);
        var t = w.Time!;
        foreach (var member in members.Append(defender))
        {
            TimeActions.Refresh(w); int n = t.Budgets.FindIndex(b => b.ActorId == member);
            t.Budgets[n] = t.Budgets[n] with { BusyThroughTick = Math.Max(t.Budgets[n].BusyThroughTick, checked(w.Clock.Tick + battleCost)) };
        }
        w.BoardRules = new(t.Rules.MaximumBattleRounds);
        w.BoardBattle = new() { Id = id, Fighters = members.Select((m, n) => new BoardFighter(m, BattleSide.Left, new(n, 0), 10, 1)).ToList() };
        w.BoardBattle.Fighters.Add(new(defender, BattleSide.Right, new(0, 0), 10, 1));
        w.Plans.RemoveAll(p => members.Contains(p.ActorId) || p.ActorId == defender);
        ActionExecutor.Emit(w, id, "TimedBattleStarted", $"{initiator}->{defender}");
        if (!w.BoardBattle.Fighters.Any(f => f.ActorId == w.ControlledCharacterId))
        {
            while (w.BoardBattle.Phase == BoardBattlePhase.Active)
            {
                LifeActions.QueueCombatMedicine(w); BoardBattleActions.QueueNpc(w); TimedPending(w, [], combatOnly: true); BoardBattleActions.Advance(w);
            }
            FinishTimedBattle(w);
            if (previousBoard is { Phase: BoardBattlePhase.Active }) w.BoardBattle = previousBoard;
        }
    }
    private static void FinishTimedBattle(WorldState w)
    {
        if (w.BoardBattle is not { Phase: BoardBattlePhase.Finished } b || w.Time!.BattleTime.Any(r => r.BattleId == b.Id)) return;
        bool player = b.Fighters.Any(f => f.ActorId == w.ControlledCharacterId);
        w.Time.BattleTime.Add(new(b.Id, player ? w.Time.Rules.BattleActionPoints : 0));
        string initiator = b.Fighters.First(f => f.Side == BattleSide.Left).ActorId;
        string defender = b.Fighters.First(f => f.Side == BattleSide.Right).ActorId;
        var allies = b.Fighters.Where(f => f.Side == BattleSide.Left && f.ActorId != initiator).Select(f => f.ActorId).ToList();
        string? winner = b.Winner is null ? null : b.Winner == BattleSide.Left ? initiator : defender;
        var history = new CombatEncounter(b.Id, initiator, defender, w.Clock.Tick, w.Clock.Tick, b.Round,
            winner is null ? CombatStatus.Draw : CombatStatus.Victory, winner, w.Clock.Tick, Allies: allies.Count == 0 ? null : allies);
        w.Combats.Add(history);
        if (winner is not null)
        {
            if (allies.Count > 0 && b.Winner == BattleSide.Left) PartyCombat.Reward(w, history);
            else CombatActions.Reward(w, history, winner, winner == initiator ? defender : initiator);
            HuntActions.Record(w, history);
        }
        LifeActions.EndBattle(w, b);
        ActionExecutor.Emit(w, b.Id, "TimedBattleSettled", player ? "WorldCatchup:" + w.Time.Rules.BattleActionPoints : "NPCDirectResult");
    }
}

