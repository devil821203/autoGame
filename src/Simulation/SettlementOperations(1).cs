using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SettlementOperations
{
    public static string Owner(WorldState w,SettlementState s,SettlementBuilding b)=>
        w.SettlementOperationRules is null?s.OrganizationId:b.OwnerOrganizationId??s.OrganizationId;
    public static bool Automatic(WorldState w,SettlementState s,SettlementBuilding b)=>
        w.SettlementOperationRules is not null&&Owner(w,s,b)==s.OrganizationId;
    public static int EmployeeLimit(WorldState w,SettlementBuilding b)=>w.SettlementOperationRules is not { } r?
        b.EmployeeLimit:checked(w.SettlementRules!.EmployeeLimit+((b.Level??r.InitialBuildingLevel)-r.InitialBuildingLevel)*r.EmployeesPerLevel);
    public static SettlementBuilding? Shop(WorldState w,SettlementState s,ActionIntent i)=>i.SettlementShopId is { } id?
        s.Buildings.SingleOrDefault(b=>b.Id==id&&b.Kind==SettlementBuildingKind.Shop):
        s.Buildings.FirstOrDefault(b=>b.Kind==SettlementBuildingKind.Shop&&Owner(w,s,b)==s.OrganizationId)??s.Buildings.FirstOrDefault(b=>b.Kind==SettlementBuildingKind.Shop);
    internal static string? ValidateUpgrade(WorldState w,SettlementState s,ActionIntent i)
    {
        if(w.SettlementOperationRules is not { } r||i.Quantity!=1||i.OrganizationId is not null)return "Building upgrades unavailable or invalid payload";
        var b=s.Buildings.SingleOrDefault(b=>b.Id==i.ResourceId);
        if(b is null||!w.SettlementRules!.Outputs.ContainsKey(b.Kind)&&b.Kind is not (SettlementBuildingKind.Dormitory or SettlementBuildingKind.SeniorDormitory))return "Only enabled resource or dormitory buildings can upgrade";
        string owner=Owner(w,s,b);
        if(!w.Organizations.Any(o=>o.Id==owner&&o.LeaderId==i.ActorId))return "Building owner permission required";
        if(b.Level is null||b.Level==int.MaxValue||b.EmployeeLimit>int.MaxValue-r.EmployeesPerLevel)return "Building level or employee limit overflow";
        return SettlementDailyEconomy.Available(w,s,owner,"spirit-stone")<r.UpgradeStones||SettlementDailyEconomy.Available(w,s,owner,"wood")<r.UpgradeWood?
            "Insufficient building owner upgrade assets":null;
    }
    internal static void Upgrade(WorldState w,SettlementState s,ActionIntent i)
    {
        var r=w.SettlementOperationRules!;int n=s.Buildings.FindIndex(b=>b.Id==i.ResourceId);var b=s.Buildings[n];string owner=Owner(w,s,b);
        foreach(var(item,amount)in new[]{("spirit-stone",r.UpgradeStones),("wood",r.UpgradeWood)})
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,owner,"consumed",item,amount,"SettlementBuildingUpgrade"));
        s.Buildings[n]=b with{Level=checked(b.Level!.Value+1),EmployeeLimit=w.SettlementRules!.Outputs.ContainsKey(b.Kind)?checked(b.EmployeeLimit+r.EmployeesPerLevel):b.EmployeeLimit,PaidThroughTick=w.Clock.Tick};
        w.SettlementBuildingUpgrades.Add(new(i.Id,s.Id,b.Id,owner,w.Clock.Tick,s.Buildings[n].Level!.Value,r.UpgradeStones,r.UpgradeWood));
    }
    internal static void Advance(WorldState w)
    {
        if(w.SettlementOperationRules is null||w.SettlementRules is not { } r||w.Time is null||w.Clock.Tick==0)return;
        bool fixedDay=w.SettlementOperationRules.FixedDailySettlement;
        if(fixedDay&&w.Clock.Tick%w.Time.Rules.TicksPerDay!=0)return;
        OrganizationLaborActions.PayDaily(w);
        foreach(var s in w.Settlements.OrderBy(s=>s.Id,StringComparer.Ordinal))
        {
            if(s.LastPayrollTick>=w.Clock.Tick)continue;
            foreach(var b in s.Buildings.OrderBy(b=>b.Id,StringComparer.Ordinal))
            {
                if(!w.SettlementBuildingRules!.Single(d=>d.Kind==b.Kind).RequiresEmployees||b.Kind is SettlementBuildingKind.Smithy or SettlementBuildingKind.Pharmacy)continue;
                bool automatic=Automatic(w,s,b);string owner=Owner(w,s,b);
                int laborers=!automatic&&OrganizationLaborActions.PaidToday(w,owner)?b.LaborCount:0;
                int employees=automatic?b.EmployeeLimit:checked(b.EmployeeCount+laborers);
                if(employees==0||!fixedDay&&(b.PaidThroughTick>=w.Clock.Tick||w.Clock.Tick-b.PaidThroughTick<w.Time.Rules.TicksPerDay))continue;
                int index=s.Buildings.FindIndex(item=>item.Id==b.Id);
                string id=$"settlement-day:{s.Id}:{b.Id}:{w.Clock.Tick}";long wages=automatic?0:checked(b.EmployeeCount*r.DailyWage);
                s.Buildings[index]=b with{PaidThroughTick=w.Clock.Tick};
                if(Inventory.Balance(w,owner,"spirit-stone")<wages)
                {ActionExecutor.Emit(w,id,"SettlementStopped","Insufficient wages: "+b.Id);continue;}
                if(wages>0)w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,id,owner,s.ResidentsAccountId,"spirit-stone",wages,"SettlementWages"));
                string? output=r.Outputs.GetValueOrDefault(b.Kind);long gross=output is null?0:checked(employees*r.DailyOutput(b.Kind));
                decimal exactTax=automatic||output is null?0:checked(gross*(w.SettlementTaxRules?.ProductionRate??0)+b.TaxRemainder);
                long tax=checked((long)decimal.Floor(exactTax)),net=gross-tax;decimal remainder=exactTax-tax;
                if(net>0)w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,id,"$settlement-production",owner,output!,net,"SettlementProduced"));
                if(tax>0)w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,id,"$settlement-production",s.OrganizationId,output!,tax,"SettlementProductionTax"));
                s.Buildings[index]=s.Buildings[index] with{TaxRemainder=remainder};
                w.SettlementDays.Add(new(id,s.Id,b.Id,w.Clock.Tick,employees,wages,output,net){OwnerId=owner,Automatic=automatic,
                    GrossQuantity=gross,TaxQuantity=tax,TaxRemainder=remainder,Laborers=laborers});
                ActionExecutor.Emit(w,id,"SettlementDayCompleted",$"{b.Id}:owner={owner}:wages={wages}:{output}={gross}:tax={tax}");
            }
            SettlementDailyEconomy.Advance(w,s);
            s.LastPayrollTick=w.Clock.Tick;
        }
    }
}
