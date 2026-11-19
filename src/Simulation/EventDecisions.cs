using Cultivation.AI;
using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class EventDecisions
{
    internal static string RetryKey(string actor)=>"$decision:"+actor;
    internal static void OrganizationChanged(WorldState w,string organizationId)
    {
        if(w.Time?.EventDecisions is null)return;
        var organization=w.Organizations.SingleOrDefault(o=>o.Id==organizationId);
        if(organization?.LeaderId is not {} leader)return;
        if(w.Characters.Any(a=>a.Id==leader&&a.MonsterKind==MonsterKind.None&&a.Health>0))
            Request(w,leader,DecisionTrigger.StateChanged);
    }
    internal static void SettlementPlanningChanged(WorldState w,SettlementState settlement)
    {
        if(w.Time?.EventDecisions is null)return;
        // Public capacity/workforce changes concern only managers with a stake here.
        // Each manager still reads only the existing rules and assets it may manage.
        var owners=w.OrganizationSites.Where(s=>s.SettlementId==settlement.Id).Select(s=>s.OrganizationId)
            .Concat(settlement.Buildings.Select(b=>b.OwnerOrganizationId??settlement.OrganizationId))
            .Append(settlement.OrganizationId).Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal);
        foreach(string owner in owners)OrganizationChanged(w,owner);
    }
    public static void EnableFresh(WorldState w)
    {
        if(w.Time?.ActionRuntime is null||w.Time.DiscreteEvents is not {WorldTime:0}||w.Time.EventDecisions is not null||w.Time.Charges.Count!=0||w.Pending.Count!=0)
            throw new InvalidOperationException("R3 requires a new idle R2 world; no old-save conversion");
        w.Time.EventDecisions=new();
        foreach(var actor in w.Characters.Where(a=>a.MonsterKind==MonsterKind.None&&(a.Id!=w.ControlledCharacterId||w.PlayerAutoSchedule)).OrderBy(a=>a.Id,StringComparer.Ordinal))Request(w,actor.Id,DecisionTrigger.Startup);
    }
    public static void Request(WorldState w,string actorId,DecisionTrigger reason)
    {
        if(w.Time?.EventDecisions is not {} mode)return;
        if(!Enum.IsDefined(reason))throw new ArgumentException("Invalid decision trigger");
        var actor=w.Characters.SingleOrDefault(a=>a.Id==actorId)??throw new ArgumentException("Unknown decision recipient");
        // Capture wakes even while the compatibility adapter temporarily suppresses player AI.
        // Drain alone decides whether the player is currently autonomous.
        if(actor.MonsterKind!=MonsterKind.None)return;
        if(mode.Ready.TryGetValue(actorId,out var existing)){existing.Reasons.Add(reason);return;}
        long sequence=mode.NextSequence;mode.NextSequence=checked(sequence+1);
        mode.Ready.Add(actorId,new(actorId,sequence,w.Time.DiscreteEvents!.WorldTime,[reason]));
        new EventScheduler(w.Time.DiscreteEvents).Cancel(RetryKey(actorId));
    }
    internal static void RecordCandidates(WorldState w,string actorId,IReadOnlyList<ScoredAction> candidates,ActionIntent? selected)
    {
        if(w.Time?.EventDecisions is not {} mode)return;
        var reasons=mode.Traces.TryGetValue(actorId,out var old)?old.Reasons:[];
        mode.Traces[actorId]=new(actorId,w.Time.DiscreteEvents!.WorldTime,reasons,true,selected?.Id,
            candidates.Select(c=>new DecisionCandidate(c.Intent.Kind,c.Intent.TargetId,c.Score,c.Reason,ActionValidator.Validate(w,c.Intent))).ToArray(),"Shared utility/validator; scores unchanged")
            {Knowledge=mode.MarketKnowledge.GetValueOrDefault(actorId)?.Values.Where(f=>f.ExpiresAt>w.Time.DiscreteEvents.WorldTime).ToArray()??[]};
    }
    private sealed class CountedPlanner(IActionPlanner inner,EventDecisionState state):IActionPlanner
    {
        public IReadOnlyList<ScoredAction> Evaluate(LocalObservation observation){state.PlannerCalls++;return inner.Evaluate(observation);}
    }
    // A running action already owns its completion wake; keep the request without polling.
    // AP-only occupancy instead has a known first available decision window.
    internal static long? NextDrainAt(WorldState w,DecisionRequest request,bool autonomous)
    {
        var mode=w.Time!.EventDecisions!;var actor=w.Characters.Single(a=>a.Id==request.ActorId);
        if(PlayerControl.IsControlled(w,actor.Id)?!w.PlayerAutoSchedule:!autonomous)return null;
        bool busy=WorldPeriodEvents.Enabled(w) && (w.Time.BattleTime.Any(b=>b.RemainingTicks>0) || w.Time.DiscreteEvents!.BattleReleaseDeadlines.ContainsKey(actor.Id)) || EquipmentRepairActions.Busy(w,actor.Id)||w.Time.MarketOrders?.Commands.Any(c=>c.Intent.ActorId==actor.Id)==true||w.MortalTrainingWork.Any(t=>t.Intent.ActorId==actor.Id)||w.Scheduled.Any(t=>t.Intent.ActorId==actor.Id)||
            w.LifeSystem?.Work.Any(t=>t.Intent.ActorId==actor.Id||t.Service?.ClientId==actor.Id)==true||
            w.Time.Deferred.Any(t=>t.Intent.ActorId==actor.Id)||w.Pending.Any(t=>t.Intent.ActorId==actor.Id)||
            actor.Stamina?.Resting==true||w.Time.ApRestingActors.Contains(actor.Id)||BoardBattleActions.Participant(w,actor.Id);
        if(busy)return null;
        long now=w.Time.DiscreteEvents!.WorldTime,step=DiscreteTime.SecondsPerStep(w);
        long due=Math.Max(now,checked(TimeActions.Budget(w,actor.Id).BusyThroughTick*step));
        if(mode.Traces.TryGetValue(actor.Id,out var previous)&&previous.AtSeconds==now ||
            mode.BudgetTick==w.Clock.Tick&&mode.BudgetUsed>=w.Rules.DecisionBudget)
            due=Math.Max(due,checked((w.Clock.Tick+1)*step));
        return due;
    }
    internal static void Drain(WorldState w,IActionPlanner planner,EventScheduler queue,bool autonomous)
    {
        if(w.Time?.EventDecisions is not {} mode||mode.Ready.Count==0||!autonomous&&!w.PlayerAutoSchedule)return;
        long now=w.Time.DiscreteEvents!.WorldTime, step=DiscreteTime.SecondsPerStep(w);
        // A retry may precede the compatibility event at the same second; wait for its projection.
        if(now/step!=w.Clock.Tick)return;
        if(mode.BudgetTick!=w.Clock.Tick){mode.BudgetTick=w.Clock.Tick;mode.BudgetUsed=0;}
        int remaining=w.Rules.DecisionBudget-mode.BudgetUsed;if(remaining<=0)return;
        var index=w.Characters.ToDictionary(a=>a.Id,StringComparer.Ordinal);
        var selected=new List<DecisionRequest>();
        foreach(var request in mode.Ready.Values.OrderBy(r=>r.Sequence).ToArray())
        {
            if(NextDrainAt(w,request,autonomous) is not {} due||due>now)continue;
            selected.Add(request);if(selected.Count==remaining)break;
        }
        if(selected.Count==0)return;
        foreach(var request in selected)
        {
            mode.Ready.Remove(request.ActorId);
            mode.Traces[request.ActorId]=new(request.ActorId,now,request.Reasons.Order().ToArray(),true,null,[],"Evaluating targeted request");
        }
        mode.Evaluations=checked(mode.Evaluations+selected.Count);mode.BudgetUsed+=selected.Count;
        AutonomousDecisions.Queue(w,new CountedPlanner(planner,mode),requestedActors:selected.Select(r=>r.ActorId).ToArray());
        foreach(var request in selected)
        {
            var pending=w.Pending.LastOrDefault(p=>p.Intent.ActorId==request.ActorId);
            var trace=mode.Traces[request.ActorId];
            if(pending is not null)mode.Traces[request.ActorId]=trace with {SelectedAction=pending.Intent.Id,Detail=pending.Intent.DecisionDetail??trace.Detail};
            else
            {
                int ticks=w.Time.ActionDriven!.IdleRetryTicks;
                if(w.Performance is {} lod)ticks=Math.Max(ticks,index[request.ActorId].RegionId!=index[w.ControlledCharacterId].RegionId?lod.RemoteIntervalTicks:lod.RegionalIntervalTicks);
                queue.Schedule(checked(now+checked(ticks*step)),SimulationEventKind.DecisionRetry,RetryKey(request.ActorId),request.ActorId);
                mode.Traces[request.ActorId]=trace with {Detail="No action selected; scheduled retry"};
            }
        }
    }
}
