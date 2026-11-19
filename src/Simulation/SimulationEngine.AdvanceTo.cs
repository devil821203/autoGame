using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    public AdvanceResult? LastAdvance { get; private set; }
    public long? WorldTimeSeconds { get { lock (gate) return state.Time?.DiscreteEvents?.WorldTime; } }

    // Signals are an engine/debug integration API, never a player command to alter NPC assets.
    public long ScheduleWorldSignal(long dueAt, string correlationId, string detail, bool blocking = false)
    {
        lock (gate) return new EventScheduler(state.Time?.DiscreteEvents ?? throw new InvalidOperationException("Discrete time not enabled"))
            .Schedule(dueAt, SimulationEventKind.Signal, SignalKey(correlationId), detail: detail, blocking: blocking).EventId;
    }
    public void CancelWorldSignal(string correlationId)
    {
        lock (gate) new EventScheduler(state.Time?.DiscreteEvents ?? throw new InvalidOperationException("Discrete time not enabled"))
            .Cancel(SignalKey(correlationId));
    }
    private static string SignalKey(string value) => !string.IsNullOrWhiteSpace(value) ? "$signal:" + value : throw new ArgumentException("Signal id required");
    public AdvanceResult AdvanceTo(long targetSeconds, bool autonomous = false)
    {
        lock (gate) return AdvanceDiscrete(targetSeconds, autonomous, []);
    }
    private AdvanceResult AdvanceDiscrete(long targetSeconds, bool autonomous, List<ActionResult> actions)
    {
        var initial = state.Time?.DiscreteEvents ?? throw new InvalidOperationException("Discrete time not enabled");
        if (targetSeconds < initial.WorldTime) throw new ArgumentOutOfRangeException(nameof(targetSeconds));
        if(StopForControlSelection())return LastAdvance!;
        var original = state; var diagnostics = LastDiagnostics;
        var watch=System.Diagnostics.Stopwatch.StartNew();long allocation=GC.GetAllocatedBytesForCurrentThread();
        long evaluationsBefore=state.Time!.EventDecisions?.Evaluations??0;
        int tradesBefore=state.Trades.Count;
        state = WorldCodec.Clone(state); Inventory.BeginFrame(state);
        try
        {
            var events = state.Time!.DiscreteEvents!;
            var queue = new EventScheduler(events);
            bool PlayerBattle() => state.BoardBattle is { Phase: BoardBattlePhase.Active } b && b.Fighters.Any(f => f.ActorId == state.ControlledCharacterId);
            if (PlayerBattle()) return LastAdvance = new(events.WorldTime, 0, 0, AdvanceStopReason.PlayerBattle);
            TimedPending(state, actions, combatOnly: false);
            SyncTrainingEvents(queue);
            DrainOrScheduleDecisions(queue,autonomous);
            TimedPending(state,actions,combatOnly:false);
            SyncTrainingEvents(queue);
            if (PlayerBattle())
            {
                WorldIntegrity.Validate(state);
                return LastAdvance = new(events.WorldTime, 0, 0, AdvanceStopReason.PlayerBattle);
            }
            var progress = queue.AdvanceTo(targetSeconds, item =>
            {
                WorldPeriodEvents.ProjectClock(state);
                if (WorldPeriodEvents.Handles(item.Kind)) { WorldPeriodEvents.Resolve(state,item,queue); }
                else if (item.Kind == SimulationEventKind.DecisionDrain) { EventDecisions.Drain(state,planner,queue,autonomous); }
                else
                if (item.Kind == SimulationEventKind.CompatibilityStep)
                {
                    // Mission refunds must precede same-time automatic publication and payroll.
                    if(MissionExpiryEvents.HasDue(state,item.DueAt)){
                        queue.Schedule(item.DueAt,SimulationEventKind.CompatibilityStep,"$compatibility");return;
                    }
                    // Complete the preceding legacy interval first. New work starts at this event's time,
                    // not at the previous projected Tick (which would shorten its duration).
                    bool playerAuto = state.PlayerAutoSchedule;
                    state.PlayerAutoSchedule = false;
                    actions.AddRange(StepTimed(false, drainCatchup: false, cloneState: false, validate: false));
                    state.PlayerAutoSchedule = playerAuto;
                    if(state.Time!.EventDecisions is null){
                        if (autonomous) AutonomousDecisions.Queue(state, planner);
                        else if (playerAuto) AutonomousDecisions.Queue(state, planner, playerOnly: true);
                    }
                    TimedPending(state, actions, combatOnly: false);
                    queue.Schedule(checked(item.DueAt + DiscreteTime.SecondsPerStep(state)), SimulationEventKind.CompatibilityStep, "$compatibility");
                }
                else if (item.Kind == SimulationEventKind.MortalTraining)
                {
                    var task = state.MortalTrainingWork.SingleOrDefault(t => t.Intent.Id == item.CorrelationId && t.DueAtSeconds == item.DueAt);
                    if (task is not null) MortalTrainingActions.Complete(state, task.Intent.Id);
                }
                else if(item.Kind==SimulationEventKind.SocialExpiry){SocialActions.Expire(state,item.Detail!);ActionExecutor.Emit(state,item.CorrelationId,"SocialExpiryResolved",$"{item.DueAt}秒:{item.Detail}");}
                else if(item.Kind==SimulationEventKind.MissionExpiry){MissionActions.Expire(state,item.Detail!);ActionExecutor.Emit(state,item.CorrelationId,"MissionExpiryResolved",$"{item.DueAt}秒:{item.Detail}");}
                else if(item.Kind==SimulationEventKind.SectPayroll)PayrollEvents.Resolve(state,item,queue);
                else if(item.Kind==SimulationEventKind.ResourceRegeneration)ResourceEvents.Resolve(state,item,queue);
                else if(item.Kind==SimulationEventKind.RestRecovery)RecoveryEvents.Rest(state,item);
                else if(item.Kind==SimulationEventKind.MedicinePulse)RecoveryEvents.Medicine(state,item);
                else if(item.Kind==SimulationEventKind.ScheduledWork)CompleteScheduledEvent(item);
                else if(item.Kind==SimulationEventKind.DeferredWork)CompleteDeferredEvent(item);
                else if(item.Kind==SimulationEventKind.LifeWork){foreach(var finished in LifeActions.Complete(state,item.CorrelationId))CompletePlan(state,finished);}
                else if(item.Kind==SimulationEventKind.EquipmentRepair)EquipmentRepairActions.Complete(state,item.CorrelationId);
                else if(item.Kind==SimulationEventKind.MarketCommand)MarketOrders.Complete(state,item.Detail!);
                else if(item.Kind==SimulationEventKind.MarketOrderExpiry)MarketOrders.Expire(state,item.Detail!);
                else if(item.Kind==SimulationEventKind.DecisionRetry)EventDecisions.Request(state,item.ActorId!,DecisionTrigger.IdleRetry);
                else ActionExecutor.Emit(state, item.CorrelationId, "WorldSignal", $"{item.DueAt}秒:{item.Detail}");
                DrainOrScheduleDecisions(queue,autonomous);
                TimedPending(state,actions,combatOnly:false);
                SyncTrainingEvents(queue);
            },()=>PlayerBattle()||PlayerControl.NeedsSelection(state));
            WorldPeriodEvents.ProjectClock(state);
            if(PlayerControl.NeedsSelection(state)){state.PlayerAutoSchedule=false;progress=progress with {Reason=AdvanceStopReason.ControlRequired,Detail="目前角色無法控制，請選擇合格角色"};}
            WorldIntegrity.Validate(state);
            if(state.Time!.EventDecisions is {} decisions)LastDiagnostics=new(state.Clock.Tick,watch.Elapsed.TotalMilliseconds,0,0,0,
                checked((int)(decisions.Evaluations-evaluationsBefore)),events.Pending.Count,state.Trades.Count-tradesBefore,
                state.Characters.Count-1,GC.GetAllocatedBytesForCurrentThread()-allocation,GC.GetTotalMemory(false));
            return LastAdvance = progress;
        }
        catch { state = original; LastDiagnostics = diagnostics; throw; }
    }
    private void DrainOrScheduleDecisions(EventScheduler queue, bool autonomous)
    {
        if (!WorldPeriodEvents.Enabled(state)) { EventDecisions.Drain(state, planner, queue, autonomous); return; }
        var events = state.Time!.DiscreteEvents!;
        var decisions = state.Time.EventDecisions;
        if(decisions is null)return;
        var deadlines=decisions.Ready.Values.Select(r=>EventDecisions.NextDrainAt(state,r,autonomous)).OfType<long>().ToArray();
        if(deadlines.Length==0)return;
        long due=deadlines.Min();
        var pending=events.Pending.SingleOrDefault(e=>e.CorrelationId=="$decision-drain"&&e.Version==events.Versions[e.CorrelationId]);
        // A completion can make an older request ready before a previously queued budget wake.
        if(pending is not null&&pending.DueAt<=due)return;
        queue.Schedule(due, SimulationEventKind.DecisionDrain, "$decision-drain");
    }

    private void SyncTrainingEvents(EventScheduler queue)
    {
        WorldPeriodEvents.Sync(state,queue);
        MissionExpiryEvents.Sync(state,queue);
        SocialExpiryEvents.Sync(state,queue);
        SyncMarketEvents(queue);
        SyncScheduledEvents(queue);
        RecoveryEvents.Sync(state,queue);
        foreach(var task in state.LifeSystem?.Work.Where(t=>t.DueAtSeconds is not null)??[])
            if(!state.Time!.DiscreteEvents!.Pending.Any(e=>e.Kind==SimulationEventKind.LifeWork&&e.CorrelationId==task.Intent.Id&&e.Version==state.Time.DiscreteEvents.Versions[e.CorrelationId]))
                queue.Schedule(task.DueAtSeconds!.Value,SimulationEventKind.LifeWork,task.Intent.Id,task.Intent.ActorId);
        foreach(var task in state.Time!.EquipmentRepair?.Work??[])
            if(!state.Time.DiscreteEvents!.Pending.Any(e=>e.Kind==SimulationEventKind.EquipmentRepair&&e.CorrelationId==task.Intent.Id&&e.Version==state.Time.DiscreteEvents.Versions[e.CorrelationId]))
                queue.Schedule(task.DueAt,SimulationEventKind.EquipmentRepair,task.Intent.Id,task.Intent.ActorId);
        var events = state.Time!.DiscreteEvents!;
        foreach (var item in events.Pending.Where(e => e.Kind == SimulationEventKind.MortalTraining &&
            e.Version == events.Versions[e.CorrelationId] && !state.MortalTrainingWork.Any(t => t.Intent.Id == e.CorrelationId && t.DueAtSeconds == e.DueAt)).ToArray())
            queue.Cancel(item.CorrelationId);
        foreach (var task in state.MortalTrainingWork)
            if (!events.Pending.Any(e => e.Kind == SimulationEventKind.MortalTraining && e.CorrelationId == task.Intent.Id &&
                e.DueAt == task.DueAtSeconds && e.Version == events.Versions[e.CorrelationId]))
                queue.Schedule(task.DueAtSeconds!.Value, SimulationEventKind.MortalTraining, task.Intent.Id, task.Intent.ActorId);
    }
    private void SyncMarketEvents(EventScheduler queue)
    {
        if(state.Time?.MarketOrders is not {} market)return;
        var events=state.Time.DiscreteEvents!;
        foreach(var c in market.Commands)
            if(!events.Pending.Any(e=>e.CorrelationId==MarketOrders.CommandKey(c.Intent.Id)&&e.Version==events.Versions[e.CorrelationId]))
                queue.Schedule(c.DueAt,SimulationEventKind.MarketCommand,MarketOrders.CommandKey(c.Intent.Id),c.Intent.ActorId,c.Intent.Id);
        foreach(var o in market.Orders.Values.Where(o=>o.Phase==MarketOrderPhase.Open))
            if(!events.Pending.Any(e=>e.CorrelationId==MarketOrders.ExpiryKey(o.Id)&&e.Version==events.Versions[e.CorrelationId]))
                queue.Schedule(o.ExpiresAt,SimulationEventKind.MarketOrderExpiry,MarketOrders.ExpiryKey(o.Id),o.ActorId,o.Id);
    }
}
