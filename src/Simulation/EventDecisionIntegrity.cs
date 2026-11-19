using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class EventDecisionIntegrity
{
    public static void Validate(WorldState w)
    {
        if(w.Time?.EventDecisions is not {} mode)
        {
            if(w.Time?.DiscreteEvents?.Pending.Any(e=>e.Kind==SimulationEventKind.DecisionRetry)==true)throw new InvalidDataException("R3 retry requires decision state");
            return;
        }
        var events=w.Time.DiscreteEvents;
        if(w.Time.ActionRuntime is null||events is null||mode.FormatVersion!=1||mode.NextSequence<1||mode.Ready is null||mode.Traces is null||mode.MarketKnowledge is null||
            mode.BudgetTick < -1||mode.BudgetTick>w.Clock.Tick||mode.BudgetUsed<0||mode.BudgetUsed>w.Rules.DecisionBudget||mode.Evaluations<0||mode.PlannerCalls<0)
            throw new InvalidDataException("Invalid R3 decision state");
        var ids=w.Characters.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
        if(mode.Ready.Values.Select(r=>r.Sequence).Distinct().Count()!=mode.Ready.Count||mode.Ready.Any(p=>p.Value is not {} r||p.Key!=r.ActorId||!ids.Contains(p.Key)||r.Sequence<1||r.Sequence>=mode.NextSequence||r.RequestedAt<0||r.RequestedAt>events.WorldTime||r.Reasons is null||r.Reasons.Count==0||r.Reasons.Any(x=>!Enum.IsDefined(x))))
            throw new InvalidDataException("Invalid R3 ready requests");
        foreach(var (id,trace) in mode.Traces)
            if(trace is null||!ids.Contains(id)||trace.ActorId!=id||trace.AtSeconds<0||trace.AtSeconds>events.WorldTime||trace.Reasons is null||trace.Reasons.Any(x=>!Enum.IsDefined(x))||trace.Knowledge is null||trace.Knowledge.Any(f=>f is null||f.ActorId!=id||f.ObservedAt>trace.AtSeconds||f.ExpiresAt<=trace.AtSeconds||f.Source!="observed:market:"+f.RegionId)||trace.Candidates is null||trace.Candidates.Any(c=>c is null||!double.IsFinite(c.Score)))
                throw new InvalidDataException("Invalid R3 decision trace");
        foreach(var (id,facts) in mode.MarketKnowledge)
        {
            if(!ids.Contains(id)||facts is null)throw new InvalidDataException("Invalid R3 knowledge owner");
            foreach(var (item,fact) in facts)
                if(fact is null||fact.ActorId!=id||fact.LocationId!="town"||fact.Source!="observed:market:"+fact.RegionId||fact.Quote is null||fact.Quote.ResourceId!=item||!w.Items.Any(i=>i.Id==item)||fact.ObservedAt<0||fact.ObservedAt>events.WorldTime||fact.ExpiresAt<=fact.ObservedAt||fact.Confidence is <0 or >1)
                    throw new InvalidDataException("Invalid R3 market provenance");
        }
        var retries=events.Pending.Where(e=>e.Kind==SimulationEventKind.DecisionRetry&&events.Versions[e.CorrelationId]==e.Version).ToArray();
        if(retries.Any(e=>e.ActorId is null||!ids.Contains(e.ActorId)||e.CorrelationId!=EventDecisions.RetryKey(e.ActorId))||retries.GroupBy(e=>e.ActorId).Any(g=>g.Count()>1))
            throw new InvalidDataException("Invalid R3 retry events");
    }
}
