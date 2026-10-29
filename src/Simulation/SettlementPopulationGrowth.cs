using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SettlementPopulationGrowth
{
    private static (int Added,long Progress) Result(SettlementPopulationGrowthRules r,int population,int capacity,long before,long delta)
    {
        long value=Math.Max(r.MinimumValue,checked(before+delta));
        long groups=Math.Min(value/r.Threshold,(capacity-population)/r.PopulationPerThreshold);
        return (checked((int)groups*r.PopulationPerThreshold),checked(value-groups*r.Threshold));
    }
    internal static void Advance(WorldState w,SettlementState s)
    {
        if(w.SettlementDailyEconomy?.PopulationGrowth is not { } r)return;
        string id=$"settlement-population:{s.Id}:{w.Clock.Tick}";
        if(w.SettlementPopulationGrowth.Any(p=>p.Id==id))return;
        var daily=w.SettlementConsumption.Where(c=>c.SettlementId==s.Id&&c.Tick==w.Clock.Tick).ToArray();
        long groups=daily.Min(c=>c.Quantity);
        long minimum=checked(s.Population*w.SettlementDailyEconomy.Consumption.Single(c=>c.ResourceId=="food").UnitsPerResident);
        long delta=checked((groups-minimum)*r.PointsPerGroup);
        var result=Result(r,s.Population,s.PopulationCapacity,s.GrowthProgress,delta);
        w.SettlementPopulationGrowth.Add(new(id,s.Id,w.Clock.Tick,s.Population,s.PopulationCapacity,s.GrowthProgress,groups,minimum,delta,
            result.Added,checked(s.Population+result.Added),result.Progress));
        s.Population=checked(s.Population+result.Added);s.WorkingPopulation=checked(s.WorkingPopulation+result.Added);s.GrowthProgress=result.Progress;
        ActionExecutor.Emit(w,id,"SettlementPopulationGrowth",$"{s.Id}:groups={groups}:delta={delta}:progress={result.Progress}:added={result.Added}");
    }
    internal static void Validate(WorldState w)
    {
        if(w.SettlementDailyEconomy?.PopulationGrowth is not { } rules)
        {
            if(w.SettlementPopulationGrowth.Count!=0||w.Settlements.Any(s=>s.GrowthProgress!=0))throw new InvalidDataException("Population growth state without rules");
            return;
        }
        if(rules.MinimumValue<0||rules.InitialValue<rules.MinimumValue||rules.Threshold<=rules.MinimumValue||rules.InitialValue>=rules.Threshold||
            rules.PointsPerGroup<=0||rules.PointsPerGroup>long.MaxValue/(int.MaxValue*w.SettlementDailyEconomy.Consumption.Max(c=>c.MaximumUnits))||rules.PopulationPerThreshold<=0||
            w.SettlementDailyEconomy.Consumption.Select(c=>c.UnitsPerResident).Distinct().Count()!=1)
            throw new InvalidDataException("settlements/daily-economy.json: invalid PopulationGrowth initial/minimum/threshold/points/population increment or unequal minimum consumption");
        if(w.SettlementPopulationGrowth.Select(p=>p.Id).Distinct().Count()!=w.SettlementPopulationGrowth.Count||
            w.SettlementPopulationGrowth.Select(p=>(p.SettlementId,p.Tick)).Distinct().Count()!=w.SettlementPopulationGrowth.Count)
            throw new InvalidDataException("Duplicate settlement population settlement");
        var days=w.SettlementConsumption.ToLookup(c=>(c.SettlementId,c.Tick));
        foreach(var p in w.SettlementPopulationGrowth)
        {
            var s=w.Settlements.SingleOrDefault(s=>s.Id==p.SettlementId);var daily=days[(p.SettlementId,p.Tick)].ToArray();
            if(s is null||daily.Length!=w.SettlementDailyEconomy.Consumption.Count||p.Tick<=0||p.Tick>w.Clock.Tick||p.Tick%w.Time!.Rules.TicksPerDay!=0||
                p.PopulationBefore<0||p.Capacity<p.PopulationBefore||p.Capacity>s.PopulationCapacity||p.ProgressBefore<rules.MinimumValue||
                daily.Any(c=>c.Population!=p.PopulationBefore)||p.ConsumedGroups!=daily.Min(c=>c.Quantity)||
                p.MinimumGroups!=checked(p.PopulationBefore*w.SettlementDailyEconomy.Consumption[0].UnitsPerResident)||
                p.Delta!=checked((p.ConsumedGroups-p.MinimumGroups)*rules.PointsPerGroup))
                throw new InvalidDataException("Invalid settlement population consumption backing");
            var result=Result(rules,p.PopulationBefore,p.Capacity,p.ProgressBefore,p.Delta);
            if(p.AddedPopulation!=result.Added||p.PopulationAfter!=p.PopulationBefore+result.Added||p.ProgressAfter!=result.Progress)
                throw new InvalidDataException("Invalid settlement population threshold result");
        }
        foreach(var s in w.Settlements)
        {
            long progress=rules.InitialValue;int? population=null;
            var investments=w.PopulationInvestments.Where(p=>p.SettlementId==s.Id).OrderBy(p=>p.Tick).ThenBy(p=>p.Sequence).ToList();
            foreach(var p in w.SettlementPopulationGrowth.Where(p=>p.SettlementId==s.Id).OrderBy(p=>p.Tick))
            {
                foreach(var investment in investments.Where(i=>i.Tick<=p.Tick).ToArray())
                {
                    if(population is { } prior&&investment.PopulationBefore!=prior)throw new InvalidDataException("Investment population history mismatch");
                    population=checked(investment.PopulationBefore+investment.AddedPopulation);investments.Remove(investment);
                }
                if(p.ProgressBefore!=progress||population is { } previous&&p.PopulationBefore!=previous)throw new InvalidDataException("Population growth history mismatch");
                progress=p.ProgressAfter;population=p.PopulationAfter;
            }
            foreach(var investment in investments)
            {
                if(population is { } prior&&investment.PopulationBefore!=prior)throw new InvalidDataException("Investment population history mismatch");
                population=checked(investment.PopulationBefore+investment.AddedPopulation);
            }
            if(s.GrowthProgress!=progress||s.Population>s.PopulationCapacity||population is { } end&&s.Population!=end)
                throw new InvalidDataException("Population growth state mismatch");
        }
        if(w.SettlementConsumption.GroupBy(c=>(c.SettlementId,c.Tick)).Any(g=>!w.SettlementPopulationGrowth.Any(p=>p.SettlementId==g.Key.SettlementId&&p.Tick==g.Key.Tick)))
            throw new InvalidDataException("Missing daily population growth receipt");
    }
}
