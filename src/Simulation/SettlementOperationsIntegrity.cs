using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SettlementOperationsIntegrity
{
    public static void Validate(WorldState w)
    {
        if(w.SettlementOperationRules is not { } r)
        {
            if(w.SettlementTaxRules is not null||w.SettlementBuildingUpgrades.Count>0)throw new InvalidDataException("Settlement operations rules missing");
            return;
        }
        if(r.InitialBuildingLevel<=0||r.EmployeesPerLevel<=0||r.UpgradeStones<=0||r.UpgradeWood<=0||w.SettlementRules is null||
            w.SettlementTaxRules is not { } tax||tax.ProductionRate<0||tax.ProductionRate>1)
            throw new InvalidDataException("config/settlements/operations.json or taxes.json: invalid level, employee increment, cost or ProductionRate (0..1)");
        if(w.SettlementDays.Any(d=>!w.Settlements.Any(s=>s.Id==d.SettlementId&&s.Buildings.Any(b=>b.Id==d.BuildingId))))
            throw new InvalidDataException("Unknown settlement production building");
        foreach(var s in w.Settlements)foreach(var b in s.Buildings)
        {
            bool staffed=w.SettlementBuildingRules!.Single(d=>d.Kind==b.Kind).RequiresEmployees;
            if(b.Level is null||b.Level<r.InitialBuildingLevel||b.OwnerOrganizationId is null||!w.Organizations.Any(o=>o.Id==b.OwnerOrganizationId)||
                staffed&&b.EmployeeLimit!=SettlementOperations.EmployeeLimit(w,b)||b.TaxRemainder<0||b.TaxRemainder>=1||
                SettlementOperations.Automatic(w,s,b)&&(b.EmployeeCount!=0||b.TaxRemainder!=0)||
                b.OwnerOrganizationId!=s.OrganizationId&&!w.SettlementConstruction.Any(c=>c.BuildingId==b.Id&&c.SponsorId==b.OwnerOrganizationId&&c.SettlementId==s.Id))
                throw new InvalidDataException("Invalid settlement building ownership, level, employees or tax remainder");
            var upgrades=w.SettlementBuildingUpgrades.Where(u=>u.BuildingId==b.Id&&u.SettlementId==s.Id).OrderBy(u=>u.ResultLevel).ToList();
            if(upgrades.Count!=(long)b.Level-r.InitialBuildingLevel||upgrades.Where((u,n)=>u.ResultLevel!=(long)r.InitialBuildingLevel+n+1).Any())
                throw new InvalidDataException("Unpaid settlement building upgrade");
            decimal remainder=0;
            foreach(var d in w.SettlementDays.Where(d=>d.SettlementId==s.Id&&d.BuildingId==b.Id).OrderBy(d=>d.Tick))
            {
                string owner=SettlementOperations.Owner(w,s,b);bool automatic=SettlementOperations.Automatic(w,s,b);
                string? output=w.SettlementRules.Outputs.GetValueOrDefault(b.Kind);
                long gross=output is null?0:checked(d.Employees*w.SettlementRules.DailyOutput(b.Kind));
                decimal exact=automatic||output is null?0:checked(gross*tax.ProductionRate+remainder);
                long taxAmount=checked((long)decimal.Floor(exact));remainder=exact-taxAmount;
                if(d.OwnerId!=owner||d.Automatic!=automatic||d.Employees<=0||d.Employees>b.EmployeeLimit||d.Output!=output||
                    d.Tick<=0||d.Tick>w.Clock.Tick||r.FixedDailySettlement&&d.Tick%w.Time!.Rules.TicksPerDay!=0||d.GrossQuantity!=gross||d.TaxQuantity!=taxAmount||d.Quantity!=gross-taxAmount||
                    d.TaxRemainder!=remainder||d.Laborers<0||d.Laborers>d.Employees||
                    d.Laborers>0&&!w.LaborPayroll.Any(p=>p.OrganizationId==owner&&p.Tick==d.Tick&&p.Paid&&p.Count>=d.Laborers)||
                    d.Wages!=(automatic?0:checked((d.Employees-d.Laborers)*w.SettlementRules.DailyWage)))
                    throw new InvalidDataException("Invalid automatic/private settlement output or tax receipt");
                var entries=w.Ledger.Where(e=>e.ActionId==d.Id).ToList();
                if(entries.Count(e=>e.Reason=="SettlementWages")!=(d.Wages>0?1:0)||
                    d.Wages>0&&!entries.Any(e=>e.From==owner&&e.To==s.ResidentsAccountId&&e.ResourceId=="spirit-stone"&&e.Quantity==d.Wages&&e.Tick==d.Tick&&e.Reason=="SettlementWages")||
                    entries.Count(e=>e.Reason=="SettlementProduced")!=(d.Quantity>0?1:0)||
                    d.Quantity>0&&!entries.Any(e=>e.From=="$settlement-production"&&e.To==owner&&e.ResourceId==output&&e.Quantity==d.Quantity&&e.Tick==d.Tick&&e.Reason=="SettlementProduced")||
                    entries.Count(e=>e.Reason=="SettlementProductionTax")!=(taxAmount>0?1:0)||
                    taxAmount>0&&!entries.Any(e=>e.From=="$settlement-production"&&e.To==s.OrganizationId&&e.ResourceId==output&&e.Quantity==taxAmount&&e.Tick==d.Tick&&e.Reason=="SettlementProductionTax"))
                    throw new InvalidDataException("Invalid settlement output, wages or tax ledger");
            }
            if(b.TaxRemainder!=remainder)throw new InvalidDataException("Settlement tax remainder mismatch");
        }
        if(w.SettlementBuildingUpgrades.Select(u=>u.Id).Distinct().Count()!=w.SettlementBuildingUpgrades.Count)throw new InvalidDataException("Duplicate building upgrade");
        foreach(var u in w.SettlementBuildingUpgrades)
        {
            var s=w.Settlements.SingleOrDefault(s=>s.Id==u.SettlementId);var b=s?.Buildings.SingleOrDefault(b=>b.Id==u.BuildingId);
            if(s is null||b is null||!w.SettlementRules.Outputs.ContainsKey(b.Kind)&&b.Kind is not (SettlementBuildingKind.Dormitory or SettlementBuildingKind.SeniorDormitory)||u.OwnerId!=b.OwnerOrganizationId||u.Tick<=0||u.Tick>w.Clock.Tick||u.Stones!=r.UpgradeStones||u.Wood!=r.UpgradeWood||
                w.Ledger.Count(e=>e.ActionId==u.Id&&e.From==u.OwnerId&&e.To=="consumed"&&e.ResourceId=="spirit-stone"&&e.Quantity==u.Stones&&e.Reason=="SettlementBuildingUpgrade"&&e.Tick==u.Tick)!=1||
                w.Ledger.Count(e=>e.ActionId==u.Id&&e.From==u.OwnerId&&e.To=="consumed"&&e.ResourceId=="wood"&&e.Quantity==u.Wood&&e.Reason=="SettlementBuildingUpgrade"&&e.Tick==u.Tick)!=1)
                throw new InvalidDataException("Invalid building upgrade source");
        }
    }
}
