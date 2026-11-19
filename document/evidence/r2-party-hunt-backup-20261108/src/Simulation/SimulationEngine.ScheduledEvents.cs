using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    private static bool PreciseScheduledAction(WorldState w,ActionKind kind)=>WorldPeriodEvents.Enabled(w)||w.Time?.IndependentScheduledEvents==true&&kind is not (ActionKind.Engage or ActionKind.ExploreDungeon)&&(kind!=ActionKind.Rest||w.Time.IndependentRecoveryEvents);
    private void SyncScheduledEvents(EventScheduler queue)
    {
        if(state.Time?.IndependentScheduledEvents!=true)return;
        long now=state.Time.DiscreteEvents!.WorldTime,step=DiscreteTime.SecondsPerStep(state);
        foreach(var item in state.Time.DiscreteEvents.Pending.Where(e=>e.Version==state.Time.DiscreteEvents.Versions[e.CorrelationId]&&
            (e.Kind==SimulationEventKind.ScheduledWork&&!state.Scheduled.Any(t=>t.Intent.Id==e.CorrelationId)||e.Kind==SimulationEventKind.DeferredWork&&!state.Time.Deferred.Any(t=>t.Intent.Id==e.CorrelationId))).ToArray())queue.Cancel(item.CorrelationId);
        for(int n=0;n<state.Scheduled.Count;n++){
            var work=state.Scheduled[n];if(work.DueAtSeconds is null)state.Scheduled[n]=work with{DueAtSeconds=checked(now+(work.DueTick-state.Clock.Tick)*step)};
        }
        for(int n=0;n<state.Time.Deferred.Count;n++){
            var work=state.Time.Deferred[n];if(work.DueAtSeconds is null)state.Time.Deferred[n]=work with{DueAtSeconds=checked(now+(work.DueTick-state.Clock.Tick)*step)};
        }
        var pending=state.Scheduled.Select(t=>(t.Sequence,Due:t.DueAtSeconds!.Value,t.Intent,Kind:SimulationEventKind.ScheduledWork))
            .Concat(state.Time.Deferred.Select(t=>(t.Sequence,Due:t.DueAtSeconds!.Value,t.Intent,Kind:SimulationEventKind.DeferredWork))).OrderBy(t=>t.Sequence);
        foreach(var work in pending)
            if(!state.Time.DiscreteEvents.Pending.Any(e=>e.Kind==work.Kind&&e.CorrelationId==work.Intent.Id&&e.Detail==work.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)&&e.Version==state.Time.DiscreteEvents.Versions[e.CorrelationId]))
                queue.Schedule(work.Due,work.Kind,work.Intent.Id,work.Intent.ActorId,work.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private void CompleteScheduledEvent(SimulationEvent item)
    {
        var w=state;var task=w.Scheduled.SingleOrDefault(t=>t.Intent.Id==item.CorrelationId&&t.DueAtSeconds==item.DueAt&&t.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)==item.Detail);
        if(task is null)return;
        var i=task.Intent;
        ActionExecutor.Emit(w,i.Id,"ScheduledWorkResolved",$"{item.DueAt}秒:{i.Kind}:{task.Sequence}");
        if(i.Kind==ActionKind.Travel&&w.HexMap is not null){
            w.Scheduled.Remove(task);if(HexTravel.CompleteStep(w,i)){
                if(w.Time!.ActionRuntime?.Actions.GetValueOrDefault(i.Id)?.Phase==ActionPhase.Interrupted){int n=w.Time.Budgets.FindIndex(b=>b.ActorId==i.ActorId);if(n>=0)w.Time.Budgets[n]=w.Time.Budgets[n] with{BusyThroughTick=w.Clock.Tick};}
                CompletePlan(w,i);
            }return;
        }
        if(i.Kind==ActionKind.ConstructBuilding)HexBuildingActions.Complete(w,i);
        else if(i.Kind is ActionKind.SectGather or ActionKind.SectBrew)SectFacilityActions.Complete(w,i);
        else if(i.Kind==ActionKind.Travel)TravelActions.Complete(w,i);
        else if(AdvancementActions.Handles(i.Kind))AdvancementActions.Complete(w,i);
        else if(i.Kind!=ActionKind.Transfer)ProductionActions.Complete(w,i);
        else ActionExecutor.CompleteScheduledTransfer(w,i);
        w.Scheduled.Remove(task);CompletePlan(w,i);
    }
    private void CompleteDeferredEvent(SimulationEvent item)
    {
        var w=state;var work=w.Time!.Deferred.SingleOrDefault(t=>t.Intent.Id==item.CorrelationId&&t.DueAtSeconds==item.DueAt&&t.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)==item.Detail);
        if(work is null)return;w.Time.Deferred.Remove(work);w.Time.CompletingActionId=work.Intent.Id;
        ActionExecutor.Emit(w,work.Intent.Id,"DeferredWorkResolved",$"{item.DueAt}秒:{work.Sequence}");
        try{
            string? error=ActionValidator.ValidateAcceptedCompletion(w,work.Intent);
            bool trackedDeferred=(ActionLifecycle.SectCommand(work.Intent.Kind) || ActionLifecycle.EquipmentChange(work.Intent.Kind) || MissionActions.IsMission(work.Intent.Kind) || work.Intent.Kind is ActionKind.Transfer or ActionKind.Buy or ActionKind.Sell or ActionKind.UseMedicine) && w.Time.ActionRuntime?.Actions.ContainsKey(work.Intent.Id)==true;
            if(trackedDeferred)ActionLifecycle.RequireRunning(w,work.Intent.Id);
            if(error is null){ActionExecutor.Execute(w,work.Intent);if(trackedDeferred){ActionLifecycle.Checkpoint(w,work.Intent.Id);ActionLifecycle.Finish(w,work.Intent.Id,ActionPhase.Completed,"延時行動完成");}ConvertTimedCombats(w);CompletePlan(w,work.Intent);}
            else {if(trackedDeferred)ActionLifecycle.Finish(w,work.Intent.Id,ActionPhase.Failed,"行動資格、價格或資源已變更");ActionExecutor.Emit(w,work.Intent.Id,"TimedCompletionRejected",error);}
        }finally{w.Time.CompletingActionId=null;}
    }
    private static long? NextActivityDeadline(WorldState w,string actor)
    {
        var values=w.Scheduled.Where(t=>t.Intent.ActorId==actor).Select(t=>t.DueAtSeconds)
            .Concat(w.Time!.MarketOrders?.Commands.Where(t=>t.Intent.ActorId==actor).Select(t=>(long?)t.DueAt)??[])
            .Concat(w.Time!.DiscreteEvents?.Pending.Where(e=>e.Kind==SimulationEventKind.BattleCatchup&&e.Version==w.Time.DiscreteEvents.Versions[e.CorrelationId]).Select(e=>(long?)e.DueAt)??[])
            .Concat(w.Time!.DiscreteEvents?.BattleReleaseDeadlines.TryGetValue(actor,out long battleDue)==true?new long?[]{battleDue}:[])
            .Concat(w.Time!.RestDueSeconds.TryGetValue(actor,out long restDue)?new long?[]{restDue}:[])
            .Concat(w.Time!.Deferred.Where(t=>t.Intent.ActorId==actor).Select(t=>t.DueAtSeconds))
            .Concat(w.LifeSystem?.Work.Where(t=>t.Intent.ActorId==actor||t.Service?.ClientId==actor).Select(t=>t.DueAtSeconds)??[])
            .Concat(w.MortalTrainingWork.Where(t=>t.Intent.ActorId==actor).Select(t=>t.DueAtSeconds))
            .Concat(w.Time.EquipmentRepair?.Work.Where(t=>t.Intent.ActorId==actor||t.Intent.TargetId==actor).Select(t=>(long?)t.DueAt)??[]).Where(t=>t is not null).ToArray();
        return values.Length==0?null:values.Min();
    }
}
