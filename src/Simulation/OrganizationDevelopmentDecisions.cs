using Cultivation.Domain;

namespace Cultivation.Simulation;

// Read-only planning: every returned intent still passes the shared action validator/executor.
internal static class OrganizationDevelopmentDecisions
{
    internal static IEnumerable<(SettlementBuildingKind Kind,int Level)> Targets(WorldState w)
    {
        if(w.LaborAutonomy is { } a)
        {
            yield return (SettlementBuildingKind.Dormitory,a.DormitoryTargetLevel);
            yield return (SettlementBuildingKind.SeniorDormitory,a.DormitoryTargetLevel);
        }
        foreach(var k in w.OrganizationDevelopment!.BuildOrder)yield return(k,w.OrganizationDevelopment.TargetBuildingLevel);
    }
    internal static void Validate(WorldState w)
    {
        if(w.OrganizationDevelopment is not { } r)return;
        if(w.SettlementOrganizationDemo is null||w.SettlementOperationRules is null||w.OrganizationSiteRules is null||
            r.BuildOrder is null||r.BuildOrder.Count==0||r.BuildOrder.Distinct().Count()!=r.BuildOrder.Count||
            r.BuildOrder.Any(k=>!w.SettlementRules!.Outputs.ContainsKey(k)||!w.SettlementConstructionRules!.Any(c=>c.Kind==k))||
            r.TargetBuildingLevel<w.SettlementOperationRules.InitialBuildingLevel||r.MaximumSiteLevel<w.OrganizationSiteRules.InitialLevel||
            r.ReserveMoney<0||r.ReserveWood<0)
            throw new InvalidDataException("config/settlements/organization-development.json: invalid BuildOrder, level targets or reserves / missing resident organization scenario");
    }

    internal static ActionIntent? Choose(WorldState w, CharacterState actor)
    {
        if(w.OrganizationDevelopment is not { } r||w.SettlementOrganizationDemo is not { } p||actor.Id==w.ControlledCharacterId||
            actor.OrganizationId!=p.Organization.Id||!w.Organizations.Any(o=>o.Id==p.Organization.Id&&o.LeaderId==actor.Id)||
            actor.Health<=0||actor.SeverelyInjured||CombatActions.Active(w,actor.Id) is not null)return null;
        var s=w.Settlements.Single(s=>s.Id==p.SettlementId);
        var site=w.OrganizationSites.SingleOrDefault(t=>t.OrganizationId==p.Organization.Id&&t.SettlementId==s.Id);
        if(site is null)return null;
        bool CanSpend(long money,long wood)=>Inventory.Balance(w,p.Organization.Id,"spirit-stone")>=r.ReserveMoney&&
            Inventory.Balance(w,p.Organization.Id,"spirit-stone")-r.ReserveMoney>=money&&
            Inventory.Balance(w,p.Organization.Id,"wood")>=r.ReserveWood&&Inventory.Balance(w,p.Organization.Id,"wood")-r.ReserveWood>=wood;
        ActionIntent Intent(ActionKind kind,string resource,long quantity=1,string? organization=null,HexCoord? position=null)=>
            new($"organization-development:{actor.Id}:{w.Clock.Tick}",actor.Id,s.Id,resource,quantity,0,DecisionSource.Npc,kind,
                OrganizationId:organization,SettlementBuildPosition:position,DecisionDetail:"組織自主發展：依設定順序、配額、資源保留及可用居民規劃");
        var owned=s.Buildings.Where(b=>b.OwnerOrganizationId==p.Organization.Id).ToArray();
        var targets=Targets(w).ToArray();
        ActionIntent? next=null;
        if(r.AutoStaff)
            foreach(var kind in r.BuildOrder)
            foreach(var b in owned.Where(b=>next is null&&b.Kind==kind).OrderBy(b=>b.Id,StringComparer.Ordinal))
            {
                if(w.LaborAutonomy is not null)
                {
                    long availableLabor=OrganizationCapacity.LaborCount(w,p.Organization.Id)-OrganizationCapacity.Assigned(w,p.Organization.Id)+b.LaborCount;
                    long desiredLabor=Math.Min(b.EmployeeLimit,availableLabor);
                    if(desiredLabor>b.LaborCount)
                    {
                        next=b.EmployeeCount+desiredLabor>b.EmployeeLimit?Intent(ActionKind.SetSettlementEmployees,b.Id,b.EmployeeLimit-desiredLabor):
                            Intent(ActionKind.SetOrganizationLabor,b.Id,desiredLabor,p.Organization.Id);
                        break;
                    }
                }
                long available=s.WorkingPopulation-s.Buildings.Sum(t=>(long)t.EmployeeCount)+b.EmployeeCount;
                long target=Math.Min(b.EmployeeLimit-b.LaborCount,available);
                if(target>b.EmployeeCount&&Inventory.Balance(w,p.Organization.Id,"spirit-stone")>=r.ReserveMoney&&
                    Inventory.Balance(w,p.Organization.Id,"spirit-stone")-r.ReserveMoney>=target*w.SettlementRules!.DailyWage)
                {next=Intent(ActionKind.SetSettlementEmployees,b.Id,target);break;}
            }
        if(next is null)
        foreach(var kind in targets.Select(t=>t.Kind).Where(kind=>!owned.Any(b=>b.Kind==kind)))
        {
            var cost=w.SettlementConstructionRules!.Single(c=>c.Kind==kind);
            HexCoord? plot=s.Territory.Where(coord=>w.HexMap!.Tiles.Any(t=>t.Coord==coord&&t.LocalOrganizationId==s.OrganizationId&&
                SettlementPlacement.Allows(w.SettlementBuildingRules!.Single(d=>d.Kind==kind),t))&&
                !w.HexMap!.Buildings.Any(b=>b.Coord==coord&&b.Status is BuildingStatus.Complete or BuildingStatus.Constructing)).Select(coord=>(HexCoord?)coord).FirstOrDefault();
            if(plot is null)continue;
            if(OrganizationSiteCapacity.Used(w,site)>=OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,site))
            {
                var sr=w.OrganizationSiteRules!;
                if(site.Level<r.MaximumSiteLevel&&CanSpend(sr.UpgradeStones,sr.UpgradeWood))next=Intent(ActionKind.UpgradeOrganizationSite,site.Id,organization:p.Organization.Id);
            }
            else if(CanSpend(cost.Stones,cost.Wood))next=Intent(ActionKind.BuildSettlement,kind.ToString(),organization:p.Organization.Id,position:plot);
            break;
        }
        if(next is null&&targets.All(t=>owned.Any(b=>b.Kind==t.Kind)))
        {
            var op=w.SettlementOperationRules!;
            var b=targets.SelectMany(t=>owned.Where(b=>b.Kind==t.Kind&&b.Level<t.Level)).FirstOrDefault();
            if(b is not null&&CanSpend(op.UpgradeStones,op.UpgradeWood))next=Intent(ActionKind.UpgradeSettlementBuilding,b.Id);
        }
        if(next is null)return null;
        var anchor=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
        if(HexNavigation.Position(w.HexMap,actor)!=anchor)
            next=new(next.Id,actor.Id,anchor.ToString(),"travel",1,0,DecisionSource.Npc,ActionKind.Travel,DecisionDetail:"返回所屬聚落執行組織建設");
        return ActionValidator.Validate(w,next) is null?next:null;
    }
}
