using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SettlementConstruction
{
    internal static string? Validate(WorldState w, SettlementState s, ActionIntent i)
    {
        if (w.SettlementConstructionRules is null) return "Settlement construction not enabled in this save";
        if (!Enum.TryParse<SettlementBuildingKind>(i.ResourceId, out var kind) ||
            w.SettlementConstructionRules.SingleOrDefault(r => r.Kind == kind) is not { } cost ||
            i.Quantity != 1 || i.SettlementBuildPosition is not { } coord)
            return "Invalid settlement building kind, quantity or position";
        if (i.OrganizationId is null || i.OrganizationId == s.OrganizationId ||
            !w.Organizations.Any(o => o.Id == i.OrganizationId && o.LeaderId == i.ActorId) ||
            w.OrganizationSiteRules is null&&!w.Organizations.Any(o => o.Id == s.OrganizationId && o.LeaderId == i.ActorId))
            return "Sponsor and settlement management permission required";
        if(w.OrganizationSiteRules is { } siteRules)
        {
            var site=w.OrganizationSites.SingleOrDefault(site=>site.OrganizationId==i.OrganizationId&&site.SettlementId==s.Id);
            if(site is null)return "Organization site required before settlement construction";
            if(OrganizationSiteCapacity.Used(w,site)>=OrganizationSiteCapacity.Limit(siteRules,site))return "Organization site building capacity reached";
        }
        var tile = w.HexMap!.Tiles.SingleOrDefault(t => t.Coord == coord);
        if (tile is null || !s.Territory.Contains(coord) || tile.LocalOrganizationId != s.OrganizationId ||
            !SettlementPlacement.Allows(w.SettlementBuildingRules!.Single(r => r.Kind == kind), tile) ||
            w.HexMap.Buildings.Any(b => b.Coord == coord && b.Status is BuildingStatus.Complete or BuildingStatus.Constructing))
            return "Invalid building terrain or unavailable settlement land";
        if (Inventory.Balance(w, i.OrganizationId, "spirit-stone") < cost.Stones ||
            Inventory.Balance(w, i.OrganizationId, "wood") < cost.Wood) return "Insufficient sponsor construction assets";
        if (s.Influence.GetValueOrDefault(i.OrganizationId) > long.MaxValue-w.SettlementRules!.BuildingInfluence ||
            kind == SettlementBuildingKind.Housing && s.PopulationCapacity > int.MaxValue-w.SettlementRules.HousingCapacity)
            return "Building influence or capacity overflow";
        return null;
    }

    internal static void Execute(WorldState w, SettlementState s, ActionIntent i)
    {
        var kind=Enum.Parse<SettlementBuildingKind>(i.ResourceId);
        var cost=w.SettlementConstructionRules!.Single(r=>r.Kind==kind);
        foreach(var (item,amount) in new[] { ("spirit-stone",cost.Stones),("wood",cost.Wood) })
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,i.OrganizationId!,"consumed",item,amount,"SettlementConstruction"));
        string buildingId="settlement-building:"+i.Id;
        bool staffed=w.SettlementBuildingRules!.Single(r=>r.Kind==kind).RequiresEmployees;
        s.Buildings.Add(new(buildingId,kind,i.SettlementBuildPosition!.Value,0,staffed ? w.SettlementRules!.EmployeeLimit : 0)
            { PaidThroughTick=w.Clock.Tick,OwnerOrganizationId=w.SettlementOperationRules is null?null:i.OrganizationId,
                Level=w.SettlementOperationRules?.InitialBuildingLevel });
        if(kind==SettlementBuildingKind.Housing) s.PopulationCapacity=checked(s.PopulationCapacity+w.SettlementRules!.HousingCapacity);
        long points=w.SettlementRules!.BuildingInfluence;
        s.Influence[i.OrganizationId!]=checked(s.Influence.GetValueOrDefault(i.OrganizationId!)+points);
        w.SettlementConstruction.Add(new(i.Id,s.Id,buildingId,i.OrganizationId!,w.Clock.Tick,kind,
            i.SettlementBuildPosition.Value,cost.Stones,cost.Wood,points));
        w.SettlementInfluence.Add(new(i.Id,s.Id,i.OrganizationId!,"building:"+buildingId,1,points));
    }
}

public static class SettlementConstructionIntegrity
{
    public static void ValidateRules(List<SettlementConstructionRule> rules)
    {
        var allowed=new[] { SettlementBuildingKind.Housing,SettlementBuildingKind.Farm,SettlementBuildingKind.Forestry,
            SettlementBuildingKind.Mine,SettlementBuildingKind.Shop,SettlementBuildingKind.Dormitory,SettlementBuildingKind.SeniorDormitory };
        if(rules.Count is not (5 or 7)||!allowed.Take(5).All(k=>rules.Any(r=>r.Kind==k)) || rules.Select(r=>r.Kind).Distinct().Count()!=rules.Count ||
            rules.Any(r=>!allowed.Contains(r.Kind)||r.Stones<=0||r.Wood<=0))
            throw new InvalidDataException("config/settlements/construction.json: Kind must cover five enabled types, Stones/Wood must be positive");
    }
    public static void Validate(WorldState w)
    {
        if(w.SettlementConstructionRules is not { } rules)
        {
            if(w.SettlementConstruction.Count>0) throw new InvalidDataException("Construction receipts without rules");
            return;
        }
        ValidateRules(rules);
        if(w.SettlementRules is null || w.SettlementConstruction.Select(r=>r.Id).Distinct().Count()!=w.SettlementConstruction.Count ||
            w.SettlementConstruction.Select(r=>r.BuildingId).Distinct().Count()!=w.SettlementConstruction.Count)
            throw new InvalidDataException("Invalid settlement construction state");
        foreach(var r in w.SettlementConstruction)
        {
            var s=w.Settlements.SingleOrDefault(s=>s.Id==r.SettlementId);
            var b=s?.Buildings.SingleOrDefault(b=>b.Id==r.BuildingId);
            var cost=rules.SingleOrDefault(c=>c.Kind==r.Kind);
            if(s is null||b is null||cost is null||b.Kind!=r.Kind||b.Position!=r.Position||r.Tick<=0||r.Tick>w.Clock.Tick||
                w.SettlementOperationRules is not null&&b.OwnerOrganizationId!=r.SponsorId||
                r.Stones!=cost.Stones||r.Wood!=cost.Wood||r.Points!=w.SettlementRules.BuildingInfluence||
                r.SponsorId==s.OrganizationId||!w.Organizations.Any(o=>o.Id==r.SponsorId)||
                w.Ledger.Count(e=>e.ActionId==r.Id&&e.Tick==r.Tick&&e.From==r.SponsorId&&e.To=="consumed"&&
                    e.ResourceId=="spirit-stone"&&e.Quantity==r.Stones&&e.Reason=="SettlementConstruction")!=1||
                w.Ledger.Count(e=>e.ActionId==r.Id&&e.Tick==r.Tick&&e.From==r.SponsorId&&e.To=="consumed"&&
                    e.ResourceId=="wood"&&e.Quantity==r.Wood&&e.Reason=="SettlementConstruction")!=1||
                w.SettlementInfluence.Count(d=>d.Id==r.Id&&d.SettlementId==s.Id&&d.OrganizationId==r.SponsorId&&
                    d.ResourceId=="building:"+b.Id&&d.Quantity==1&&d.Points==r.Points)!=1)
                throw new InvalidDataException("Invalid settlement construction source/ownership/influence");
        }
    }
}
