using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class PopulationInvestmentActions
{
    internal static string? Validate(WorldState w,ActionIntent i)
    {
        if(w.PopulationInvestmentRules is not { } r||i.Quantity!=1||i.ResourceId!="population"||i.DurationTicks!=0)return "Population investment unavailable or invalid batch";
        var s=w.Settlements.SingleOrDefault(s=>s.Id==i.TargetId);
        if(s is null||s.PopulationCapacity-s.Population<r.PopulationPerSuccess)return "Population housing capacity reached";
        string owner=i.OrganizationId??s.OrganizationId;
        if(!w.Organizations.Any(o=>o.Id==owner&&o.LeaderId==i.ActorId))return "Population investment owner permission required";
        var actor=w.Characters.Single(c=>c.Id==i.ActorId);
        if(actor.RegionId!="valley"||HexNavigation.Position(w.HexMap!,actor)!=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor)return "Must arrive at settlement";
        foreach(var(item,units)in r.ResourcesPerGroup)
            if(SettlementDailyEconomy.Available(w,s,owner,item)<checked(units*r.GroupsPerAttempt))return "Insufficient population investment resources";
        long points=owner==s.OrganizationId?0:checked(r.ResourcesPerGroup.Values.Sum()*r.GroupsPerAttempt*w.SettlementRules!.MaterialInfluence);
        return s.Influence.GetValueOrDefault(owner)>long.MaxValue-points?"Population influence overflow":null;
    }
    internal static void Execute(WorldState w,ActionIntent i)
    {
        var r=w.PopulationInvestmentRules!;var s=w.Settlements.Single(s=>s.Id==i.TargetId);string owner=i.OrganizationId??s.OrganizationId;
        long points=owner==s.OrganizationId?0:checked(r.ResourcesPerGroup.Values.Sum()*r.GroupsPerAttempt*w.SettlementRules!.MaterialInfluence);
        foreach(var(item,units)in r.ResourcesPerGroup)
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,owner,"consumed",item,checked(units*r.GroupsPerAttempt),"PopulationInvestment"));
        ulong before=w.RandomState;var random=new SeededRandom(before);
        bool success=random.NextUInt32()/((decimal)uint.MaxValue+1)<r.SuccessProbability;w.RandomState=random.State;
        int added=success?r.PopulationPerSuccess:0;
        w.PopulationInvestments.Add(new(i.Id,s.Id,owner,w.Clock.Tick,w.NextSequence++,s.Population,s.PopulationCapacity,success,added,before,random.State,points));
        s.Population=checked(s.Population+added);s.WorkingPopulation=checked(s.WorkingPopulation+added);
        if(owner!=s.OrganizationId)
        {
            s.Influence[owner]=checked(s.Influence.GetValueOrDefault(owner)+points);
            w.SettlementInfluence.Add(new(i.Id,s.Id,owner,"population-investment",1,points));
        }
        ActionExecutor.Emit(w,i.Id,"PopulationInvestmentCompleted",$"{s.Id}:success={success}:population+={added}:influence={points}");
    }
    internal static void ValidateWorld(WorldState w)
    {
        if(w.PopulationInvestmentRules is not { } r)
        {if(w.PopulationInvestments.Count>0)throw new InvalidDataException("Population investment receipts without rules");return;}
        if(w.SettlementRules is null||r.GroupsPerAttempt<=0||r.ResourcesPerGroup is null||r.ResourcesPerGroup.Count!=2||
            !r.ResourcesPerGroup.ContainsKey("food")||!r.ResourcesPerGroup.ContainsKey("wood")||r.ResourcesPerGroup.Any(p=>p.Value<=0||p.Value>long.MaxValue/r.GroupsPerAttempt)||
            r.SuccessProbability<0||r.SuccessProbability>1||r.PopulationPerSuccess<=0||r.ActionPoints is <1 or >30)
            throw new InvalidDataException("config/settlements/population-investment.json: invalid groups, resources, probability, population increment or AP");
        if(w.PopulationInvestments.Select(p=>p.Id).Distinct().Count()!=w.PopulationInvestments.Count||w.PopulationInvestments.Select(p=>p.Sequence).Distinct().Count()!=w.PopulationInvestments.Count)
            throw new InvalidDataException("Duplicate population investment receipt");
        var ledger=w.Ledger.ToLookup(e=>e.ActionId);
        foreach(var p in w.PopulationInvestments)
        {
            var s=w.Settlements.SingleOrDefault(s=>s.Id==p.SettlementId);var random=new SeededRandom(p.RandomBefore);
            bool win=random.NextUInt32()/((decimal)uint.MaxValue+1)<r.SuccessProbability;
            long points=s?.OrganizationId==p.OwnerId?0:checked(r.ResourcesPerGroup.Values.Sum()*r.GroupsPerAttempt*w.SettlementRules.MaterialInfluence);
            if(s is null||p.Tick<=0||p.Tick>w.Clock.Tick||p.Sequence<=0||p.Sequence>=w.NextSequence||!w.ProcessedIntentIds.Contains(p.Id)||
                p.RandomBefore==0||p.RandomAfter!=random.State||p.Success!=win||p.AddedPopulation!=(win?r.PopulationPerSuccess:0)||
                p.PopulationBefore<0||p.PopulationBefore>p.Capacity-r.PopulationPerSuccess||p.Capacity>s.PopulationCapacity||p.Influence!=points||
                !w.Organizations.Any(o=>o.Id==p.OwnerId)||ledger[p.Id].Count()!=r.ResourcesPerGroup.Count||
                r.ResourcesPerGroup.Any(cost=>ledger[p.Id].Count(e=>e.From==p.OwnerId&&e.To=="consumed"&&e.Tick==p.Tick&&e.ResourceId==cost.Key&&
                    e.Quantity==cost.Value*r.GroupsPerAttempt&&e.Reason=="PopulationInvestment")!=1))
                throw new InvalidDataException("Invalid population investment result or resource backing");
        }
    }
}
