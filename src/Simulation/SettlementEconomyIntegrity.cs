using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SettlementEconomyIntegrity
{
    public static void ValidateRules(SettlementRuntimeRules r)
    {
        if(r.DailyOutputOverrides is { } rates && rates.Any(p=>r.Outputs is null||!r.Outputs.ContainsKey(p.Key)||p.Value<=0||p.Value>long.MaxValue/int.MaxValue))
            throw new InvalidDataException("config/settlements/runtime.json: invalid DailyOutputOverrides kind or quantity");
        if (r.Outputs is null || r.Levels is null || r.Prices is null || r.InitialLevel <= 0 || r.InitialPopulation < 0 || r.MinimumHappiness >= r.MaximumHappiness ||
            r.InitialHappiness < r.MinimumHappiness || r.InitialHappiness > r.MaximumHappiness ||
            r.InitialStones < 0 || r.HallCapacity < r.InitialPopulation || r.HousingCapacity <= 0 ||
            r.HallCapacityPerLevel <= 0 || r.EmployeeLimit <= 0 || r.DailyWage <= 0 ||
            r.DailyOutputPerEmployee <= 0 || r.DailyWage > long.MaxValue / r.EmployeeLimit ||
            r.DailyOutputPerEmployee > long.MaxValue / r.EmployeeLimit || r.MaterialInfluence < 0 || r.BuildingInfluence < 0 ||
            r.Outputs.Count != 3 || !r.Outputs.TryGetValue(SettlementBuildingKind.Farm, out var farm) || farm != "food" ||
            !r.Outputs.TryGetValue(SettlementBuildingKind.Forestry, out var forest) || forest != "wood" ||
            !r.Outputs.TryGetValue(SettlementBuildingKind.Mine, out var mine) || mine != "mineral" ||
            r.Levels.Select(l => l.Level).Distinct().Count() != r.Levels.Count ||
            r.Levels.Any(l => l.Level <= r.InitialLevel || l.RequiredPopulation < 0 || l.Stones < 0 || l.Wood < 0) ||
            r.Prices.Select(p => p.Item).Distinct().Count() != r.Prices.Count || r.Prices.Count != r.Outputs.Count ||
            r.Prices.Any(p => !r.Outputs.ContainsValue(p.Item) || p.Buy <= 0 || p.Sell <= p.Buy))
            throw new InvalidDataException("config/settlements/runtime.json: invalid initial values, capacity, wages, output, levels or prices");
    }

    public static void Validate(WorldState w)
    {
        SettlementConstructionIntegrity.Validate(w);
        OrganizationSiteIntegrity.Validate(w);
        SettlementOperationsIntegrity.Validate(w);
        if (w.SettlementRules is not { } r)
        {
            if (w.SettlementDays.Count > 0 || w.SettlementInfluence.Count > 0) throw new InvalidDataException("Settlement receipts without rules");
            return;
        }
        ValidateRules(r);
        if (w.Time is null) throw new InvalidDataException("Settlement economy requires calendar");
        foreach (var s in w.Settlements)
        {
            if (!w.Organizations.Any(o => o.Id == s.ResidentsAccountId) || s.OrganizationId == s.ResidentsAccountId ||
                w.Settlements.Any(other => other.OrganizationId == s.ResidentsAccountId) ||
                s.Happiness < r.MinimumHappiness || s.Happiness > r.MaximumHappiness || s.LastPayrollTick > w.Clock.Tick ||
                s.Level != r.InitialLevel && !r.Levels.Any(l => l.Level == s.Level) ||
                s.Buildings.Count(b => b.Kind == SettlementBuildingKind.TownHall) != 1 ||
                s.PopulationCapacity != checked(r.HallCapacity + (s.Level-r.InitialLevel)*r.HallCapacityPerLevel +
                    s.Buildings.Count(b=>b.Kind==SettlementBuildingKind.Housing)*r.HousingCapacity) ||
                s.Buildings.Any(b => b.PaidThroughTick < 0 || b.PaidThroughTick > w.Clock.Tick || w.SettlementOperationRules is null&&b.EmployeeLimit > r.EmployeeLimit) ||
                s.Influence.Any(p => p.Value < 0 || !w.Organizations.Any(o => o.Id == p.Key)))
                throw new InvalidDataException("Invalid settlement runtime state: " + s.Id);
        }
        if (w.SettlementDays.Select(d => d.Id).Distinct().Count() != w.SettlementDays.Count ||
            w.SettlementDays.Select(d => (d.SettlementId,d.BuildingId,d.Tick)).Distinct().Count() != w.SettlementDays.Count ||
            w.SettlementInfluence.Select(d => d.Id).Distinct().Count() != w.SettlementInfluence.Count)
            throw new InvalidDataException("Duplicate settlement receipt");
        foreach (var d in w.SettlementDays)
        {
            if(w.SettlementOperationRules is not null)continue;
            var s = w.Settlements.SingleOrDefault(s => s.Id == d.SettlementId);
            var b = s?.Buildings.SingleOrDefault(b => b.Id == d.BuildingId);
            if (s is null || b is null || d.Employees <= 0 || d.Employees > r.EmployeeLimit ||
                d.Tick <= 0 || d.Tick > w.Clock.Tick || d.Wages != checked(d.Employees * r.DailyWage) ||
                d.Output != r.Outputs.GetValueOrDefault(b.Kind) ||
                d.Quantity != (d.Output is null ? 0 : checked(d.Employees * r.DailyOutput(b!.Kind))) ||
                w.Ledger.Count(e => e.ActionId == d.Id && e.Tick == d.Tick && e.From == s.OrganizationId &&
                    e.To == s.ResidentsAccountId && e.ResourceId == "spirit-stone" && e.Quantity == d.Wages && e.Reason == "SettlementWages") != 1 ||
                (d.Quantity > 0 && w.Ledger.Count(e => e.ActionId == d.Id && e.From == "$settlement-production" &&
                    e.To == s.OrganizationId && e.Tick == d.Tick && e.ResourceId == d.Output && e.Quantity == d.Quantity && e.Reason == "SettlementProduced") != 1))
                throw new InvalidDataException("Invalid settlement payroll/production receipt");
        }
        foreach (var d in w.SettlementInfluence)
        {
            if(d.ResourceId=="population-investment")
            {
                if(!w.PopulationInvestments.Any(p=>p.Id==d.Id&&p.SettlementId==d.SettlementId&&p.OwnerId==d.OrganizationId&&p.Influence==d.Points&&d.Quantity==1))
                    throw new InvalidDataException("Unbacked population investment influence");
                continue;
            }
            if (d.ResourceId.StartsWith("building:",StringComparison.Ordinal))
            {
                if(w.SettlementConstruction.Count(b=>b.Id==d.Id&&b.SettlementId==d.SettlementId&&b.SponsorId==d.OrganizationId&&
                    "building:"+b.BuildingId==d.ResourceId&&d.Quantity==1&&b.Points==d.Points)!=1)
                    throw new InvalidDataException("Invalid building influence source");
                continue;
            }
            if (d.ResourceId is not ("wood" or "mineral") || d.Quantity <= 0 || d.Points != checked(d.Quantity * r.MaterialInfluence) ||
                !w.Settlements.Any(s => s.Id == d.SettlementId && w.Ledger.Count(e => e.ActionId == d.Id &&
                    e.From == d.OrganizationId && e.To == s.OrganizationId && e.ResourceId == d.ResourceId && e.Quantity == d.Quantity && e.Reason == "SettlementSupport") == 1))
                throw new InvalidDataException("Invalid settlement influence source");
        }
        foreach (var s in w.Settlements)
            if (s.Influence.Any(p => p.Value != w.SettlementInfluence.Where(d => d.SettlementId == s.Id && d.OrganizationId == p.Key).Sum(d => d.Points)) ||
                w.SettlementInfluence.Where(d => d.SettlementId == s.Id).Any(d => !s.Influence.ContainsKey(d.OrganizationId)))
                throw new InvalidDataException("Settlement influence total mismatch");
    }
}
