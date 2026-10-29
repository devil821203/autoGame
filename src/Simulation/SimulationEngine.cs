using Cultivation.Domain;
using Cultivation.AI;
using System.Diagnostics;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    private readonly object gate = new();
    private WorldState state;
    private readonly IActionPlanner planner;
    public TickDiagnostics? LastDiagnostics { get; private set; }

    public SimulationEngine(WorldState initial, IActionPlanner? planner = null)
    {
        this.planner = planner ?? new UtilityPlanner();
        state = WorldCodec.Clone(initial);
        Inventory.BeginFrame(state);
        WorldIntegrity.Validate(state);
    }

    public WorldState Snapshot() { lock (gate) return WorldCodec.Clone(state); }

    // Source identifies a trusted local adapter, not a network security credential.
    public void Submit(ActionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        lock (gate)
        {
            state.Pending.Add(new(state.NextSequence++, intent));
        }
    }

    public IReadOnlyList<ActionResult> Step(bool autonomous = false)
    {
        lock (gate)
        {
            if (state.Time is not null) return StepTimed(autonomous);
            // A failed tick never leaves partial live mutations.
            var timer = Stopwatch.StartNew(); long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            var work = WorldCodec.Clone(state);
            Inventory.BeginFrame(work);
            double cloneMs = timer.Elapsed.TotalMilliseconds;
            int previousTrades = work.Trades.Count;
            work.Memories.RemoveAll(m => m.Tick < work.Clock.Tick - work.Rules.MemoryTicks);
            if (autonomous) { LifeActions.QueueCombatMedicine(work); BoardBattleActions.QueueNpc(work); AutonomousDecisions.Queue(work, planner); }
            else if (work.PlayerAutoSchedule) AutonomousDecisions.Queue(work, planner, playerOnly: true);
            double aiMs = timer.Elapsed.TotalMilliseconds - cloneMs;
            int decisions = autonomous || work.PlayerAutoSchedule ? int.Parse(work.Events.Last(e => e.Kind == "DecisionBudget").Detail, System.Globalization.CultureInfo.InvariantCulture) : 0;
            work.Clock = work.Clock with { Tick = checked(work.Clock.Tick + 1) };
            StaminaActions.Advance(work);
            PartyActions.Advance(work);
            HuntActions.Advance(work);
            ResourceRegeneration.Advance(work);
            MissionActions.Expire(work);
            SocialActions.Expire(work);
            CombatActions.Advance(work);
            foreach (var task in work.Scheduled.OrderBy(t => t.DueTick).ThenBy(t => t.Sequence).ToArray())
            {
                if (task.DueTick > work.Clock.Tick) continue;
                if (task.Intent.Kind == ActionKind.Travel && work.HexMap is not null)
                {
                    work.Scheduled.Remove(task);
                    if (HexTravel.CompleteStep(work, task.Intent)) CompletePlan(work, task.Intent);
                    continue;
                }
                if (task.Intent.Kind == ActionKind.ConstructBuilding) HexBuildingActions.Complete(work, task.Intent);
                else if (task.Intent.Kind is ActionKind.SectGather or ActionKind.SectBrew) SectFacilityActions.Complete(work, task.Intent);
                else if (task.Intent.Kind == ActionKind.Travel) TravelActions.Complete(work, task.Intent);
                else if (AdvancementActions.Handles(task.Intent.Kind)) AdvancementActions.Complete(work, task.Intent);
                else if (task.Intent.Kind != ActionKind.Transfer) ProductionActions.Complete(work, task.Intent);
                else
                {
                    ActionExecutor.Transfer(work, task.Intent, ActionExecutor.Escrow(task.Intent.Id), task.Intent.TargetId, "Completed");
                    ActionExecutor.Emit(work, task.Intent.Id, "Completed", "Scheduled transfer completed");
                }
                work.Scheduled.Remove(task);
                CompletePlan(work, task.Intent);
            }
            foreach (var finished in LifeActions.Complete(work)) CompletePlan(work, finished);
            LifeActions.Effects(work);
            var results = new List<ActionResult>();
            var processed = work.ProcessedIntentIds.ToHashSet(StringComparer.Ordinal);
            foreach (var queued in work.Pending.OrderBy(p => p.Sequence))
            {
                var intent = queued.Intent;
                if (!processed.Add(intent.Id))
                {
                    results.Add(new(intent.Id, false, "Duplicate intent"));
                    continue;
                }
                work.ProcessedIntentIds.Add(intent.Id);
                if (intent.DecisionDetail is not null) ActionExecutor.Emit(work, intent.Id, "Decision", intent.DecisionDetail);
                var error = ActionValidator.Validate(work, intent);
                if (error is null)
                {
                    if (intent.Plan is not null)
                    {
                        work.Plans.RemoveAll(p => p.ActorId == intent.ActorId);
                        work.Plans.Add(intent.Plan);
                    }
                    ActionExecutor.Execute(work, intent);
                    if (!work.Scheduled.Any(t => t.Intent.Id == intent.Id)) CompletePlan(work, intent);
                }
                else
                {
                    work.Plans.RemoveAll(p => p.ActorId == intent.ActorId);
                    ActionExecutor.Emit(work, intent.Id, "Rejected", error);
                }
                results.Add(new(intent.Id, error is null, error ?? "Accepted"));
            }
            work.Pending.Clear();
            BoardBattleActions.QueuePlayerAuto(work);
            BoardBattleActions.Advance(work);
            if (work.BoardBattle is { Phase: BoardBattlePhase.Finished } endedBoard) LifeActions.EndBattle(work, endedBoard);
            TestProcurement.Advance(work);
            SectDuties.Advance(work);
            SectPayroll.Advance(work);
            PartyActions.Advance(work);
            work.Memories.RemoveAll(m => m.Tick < work.Clock.Tick - work.Rules.MemoryTicks);
            PerformanceIntegrity.TrimDiagnostics(work);
            double beforeValidation = timer.Elapsed.TotalMilliseconds;
            WorldIntegrity.Validate(work);
            state = work;
            LastDiagnostics = new(work.Clock.Tick, timer.Elapsed.TotalMilliseconds, cloneMs, aiMs,
                timer.Elapsed.TotalMilliseconds - beforeValidation, decisions,
                work.Scheduled.Count + work.Combats.Count(c => c.Status == CombatStatus.Active), work.Trades.Count - previousTrades,
                work.Characters.Count(c => c.Id != work.ControlledCharacterId), GC.GetAllocatedBytesForCurrentThread() - allocationStart, GC.GetTotalMemory(false));
            return results;
        }
    }
    private static void CompletePlan(WorldState w, ActionIntent intent)
    {
        StaminaActions.Complete(w, intent);
        var plan = w.Plans.SingleOrDefault(p => p.ActorId == intent.ActorId);
        if (plan is null) return;
        w.Plans.Remove(plan);
        if (plan.Steps.Count > 1) w.Plans.Add(plan with { Steps = plan.Steps.Skip(1).ToList() });
    }
}









