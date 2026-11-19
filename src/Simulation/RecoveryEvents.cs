using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class RecoveryEvents
{
    private static string RestKey(string actor)=>"$rest-recovery:"+actor;
    private static string MedicineKey(string effect)=>"$medicine-pulse:"+effect;
    private static bool Resting(WorldState w,string id)=>w.Characters.Any(a=>a.Id==id&&(a.Stamina?.Resting==true||w.Time!.ApRestingActors.Contains(id)));
    internal static void Sync(WorldState w,EventScheduler queue)
    {
        if(w.Time?.IndependentRecoveryEvents!=true)return;
        var time=w.Time;long now=time.DiscreteEvents!.WorldTime,interval=DiscreteTime.SecondsPerStep(w);
        if(time.ActionRuntime is { } runtime)
            foreach(var action in runtime.Actions.Values.Where(a=>a.Kind==ActionKind.Rest && a.Phase==ActionPhase.Running && (!Resting(w,a.ActorId)||w.Characters.Single(c=>c.Id==a.ActorId).Health<=0)).ToArray())
                ActionLifecycle.Finish(w,action.Id,ActionPhase.Interrupted,"休息中斷");
        foreach(string id in time.RestDueSeconds.Keys.Where(id=>!Resting(w,id)||w.Characters.Single(a=>a.Id==id).Health<=0).ToArray()){
            time.RestDueSeconds.Remove(id);queue.Cancel(RestKey(id));
        }
        foreach(var a in w.Characters.Where(a=>a.Health>0&&(a.Stamina?.Resting==true||time.ApRestingActors.Contains(a.Id)))){
            if(!time.RestDueSeconds.ContainsKey(a.Id))time.RestDueSeconds.Add(a.Id,checked(now+interval));
            string key=RestKey(a.Id);long due=time.RestDueSeconds[a.Id];
            if(!time.DiscreteEvents.Pending.Any(e=>e.Kind==SimulationEventKind.RestRecovery&&e.CorrelationId==key&&e.DueAt==due&&e.Version==time.DiscreteEvents.Versions[e.CorrelationId]))queue.Schedule(due,SimulationEventKind.RestRecovery,key,a.Id);
        }
        if(w.LifeSystem is not {} life)return;
        for(int n=0;n<life.Effects.Count;n++)if(life.Effects[n] is {BattleId:null,NextAtSeconds:null} effect)life.Effects[n]=effect with{NextAtSeconds=checked(now+interval)};
        foreach(var item in time.DiscreteEvents.Pending.Where(e=>e.Kind==SimulationEventKind.MedicinePulse&&e.Version==time.DiscreteEvents.Versions[e.CorrelationId]&&!life.Effects.Any(f=>f.Id==e.Detail&&f.BattleId is null)).ToArray())queue.Cancel(item.CorrelationId);
        foreach(var effect in life.Effects.Where(e=>e.BattleId is null)){
            string key=MedicineKey(effect.Id);
            if(!time.DiscreteEvents.Pending.Any(e=>e.Kind==SimulationEventKind.MedicinePulse&&e.CorrelationId==key&&e.DueAt==effect.NextAtSeconds&&e.Version==time.DiscreteEvents.Versions[e.CorrelationId]))queue.Schedule(effect.NextAtSeconds!.Value,SimulationEventKind.MedicinePulse,key,effect.ActorId,effect.Id);
        }
    }
    internal static void Rest(WorldState w,SimulationEvent item)
    {
        if(item.ActorId is not {} id||w.Time?.RestDueSeconds.GetValueOrDefault(id)!=item.DueAt)return;
        w.Time.RestDueSeconds.Remove(id);
        if(!Resting(w,id))return;
        TimeActions.RecoverRestAp(w,[id]);StaminaActions.RecoverActor(w,id);
        ActionExecutor.Emit(w,item.CorrelationId,"RestRecoveryResolved",$"{item.DueAt}秒:{id}");
        if(w.Time.ActionRuntime is { } runtime)
        {
            var action=runtime.Actions.Values.SingleOrDefault(a=>a.Kind==ActionKind.Rest && a.ActorId==id && a.Phase==ActionPhase.Running);
            if(action is not null)
            {
                if(!Resting(w,id)) { ActionLifecycle.Checkpoint(w,action.Id); ActionLifecycle.Finish(w,action.Id,ActionPhase.Completed,"休息完成"); }
                else runtime.Actions[action.Id]=action with { DueAt=checked(item.DueAt+DiscreteTime.SecondsPerStep(w)) };
            }
        }
    }
    internal static void Medicine(WorldState w,SimulationEvent item)
    {
        if(w.LifeSystem?.Effects.Any(e=>e.Id==item.Detail&&e.BattleId is null&&e.NextAtSeconds==item.DueAt)!=true)return;
        LifeActions.Effects(w,effectId:item.Detail);ActionExecutor.Emit(w,item.CorrelationId,"MedicinePulseResolved",$"{item.DueAt}秒:{item.Detail}");
    }
    internal static void Validate(WorldState w)
    {
        if(w.Time is {} present&&present.RestDueSeconds is null)throw new InvalidDataException("Missing recovery deadline collection");
        if(w.Time?.IndependentRecoveryEvents!=true){if(w.Time?.RestDueSeconds.Count>0||w.LifeSystem?.Effects.Any(e=>e.NextAtSeconds is not null)==true)throw new InvalidDataException("Recovery seconds require latest mode");return;}
        var time=w.Time;if(!time.IndependentScheduledEvents||time.DiscreteEvents is not {} events)throw new InvalidDataException("Invalid independent recovery mode");
        if(w.Characters.Any(a=>a.Health>0&&(a.Stamina?.Resting==true||time.ApRestingActors.Contains(a.Id))&&!time.RestDueSeconds.ContainsKey(a.Id)))throw new InvalidDataException("Resting actor is missing recovery deadline");
        foreach(var (id,due) in time.RestDueSeconds){
            if(!Resting(w,id)||due<events.WorldTime||events.Pending.Count(e=>e.Kind==SimulationEventKind.RestRecovery&&e.CorrelationId==RestKey(id)&&e.ActorId==id&&e.DueAt==due&&e.Version==events.Versions[e.CorrelationId])!=1)throw new InvalidDataException("Rest recovery deadline/event mismatch");
        }
        foreach(var effect in w.LifeSystem?.Effects??[]){
            if(effect.BattleId is not null){if(effect.NextAtSeconds is not null)throw new InvalidDataException("Combat effect cannot also have world deadline");continue;}
            if(effect.NextAtSeconds is not {} due||due<events.WorldTime||events.Pending.Count(e=>e.Kind==SimulationEventKind.MedicinePulse&&e.CorrelationId==MedicineKey(effect.Id)&&e.Detail==effect.Id&&e.DueAt==due&&e.Version==events.Versions[e.CorrelationId])!=1)throw new InvalidDataException("Medicine recovery deadline/event mismatch");
        }
    }
}
