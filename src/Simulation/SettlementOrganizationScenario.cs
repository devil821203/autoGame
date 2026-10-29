using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SettlementOrganizationScenario
{
    public static WorldState Create(string name="行者",GameConfiguration? configuration=null)
    {
        var config=configuration??GameConfiguration.Find();var w=MedicineOnlyProfile.ApplyFresh(SettlementScenario.Create(name,config),config);
        var profile=config.Read<SettlementOrganizationDemo>("settlements/resident-organization.json");
        if(profile.Organization is null||profile.InitialStock is null)throw new InvalidDataException("settlements/resident-organization.json: Organization and InitialStock are required");
        var s=w.Settlements.SingleOrDefault(s=>s.Id==profile.SettlementId);
        var template=w.Characters.SingleOrDefault(c=>c.Id==profile.LeaderTemplateId);
        var scene=w.HexMap!.Sites.SingleOrDefault(s=>s.Id==profile.SceneTemplateId);
        var rules=w.OrganizationSiteRules!;var organization=profile.Organization;
        if(s is null||template is null||scene is null||scene.Kind!=(SiteKind)organization.Kind||
            string.IsNullOrWhiteSpace(organization.Id)||string.IsNullOrWhiteSpace(organization.Name)||string.IsNullOrWhiteSpace(organization.LeaderId)||string.IsNullOrWhiteSpace(profile.LeaderName)||
            !Enum.IsDefined(organization.Kind)||!Enum.IsDefined(profile.InitialSiteKind)||
            w.Organizations.Any(o=>o.Id==organization.Id)||w.Characters.Any(c=>c.Id==organization.LeaderId)||
            profile.InitialStock is null||profile.InitialStock.Count!=2||profile.InitialStock.Keys.Any(k=>k is not ("spirit-stone" or "wood"))||
            profile.InitialStock.GetValueOrDefault("spirit-stone")<rules.ConstructionStones||profile.InitialStock.GetValueOrDefault("wood")<rules.ConstructionWood)
            throw new InvalidDataException("settlements/resident-organization.json: invalid Organization/LeaderTemplateId/SettlementId/SceneTemplateId/InitialSiteKind/InitialStock");
        w.SettlementOrganizationDemo=profile;w.Organizations.Add(organization);
        var anchor=w.HexMap.Sites.Single(site=>site.Id==s.SiteId).Anchor;
        w.Characters.Add(template with{Id=organization.LeaderId!,Name=profile.LeaderName,OrganizationId=organization.Id,
            Position=OrganizationPosition.Leader,TestProfession=TestProfession.None,HexPosition=anchor,DemoLocationId=anchor.ToString()});
        foreach(var(item,quantity)in profile.InitialStock)
            w.Ledger.Add(new(w.NextSequence++,0,"resident-organization-genesis:"+item,"$genesis",organization.Id,item,quantity,"Genesis"));
        string payment="resident-organization-initial-site";string siteId="organization-site:"+payment;
        foreach(var(item,quantity)in new[]{("spirit-stone",rules.ConstructionStones),("wood",rules.ConstructionWood)})
            w.Ledger.Add(new(w.NextSequence++,0,payment,organization.Id,"consumed",item,quantity,"OrganizationSitePayment"));
        w.OrganizationSites.Add(new(siteId,s.Id,organization.Id,profile.InitialSiteKind,rules.InitialLevel));
        w.OrganizationSiteReceipts.Add(new(payment,siteId,0,rules.InitialLevel,rules.ConstructionStones,rules.ConstructionWood){InitialPlacement=true});
        string siteName=rules.Sites.Single(d=>d.Kind==profile.InitialSiteKind).Name;
        w.HexMap.Sites.Add(scene with{Id="organization:"+organization.Id,Name=organization.Name+siteName,Anchor=anchor,
            MainBuilding=siteName,MainLevel=rules.InitialLevel,OrganizationId=organization.Id,Slots=scene.Slots.Select(slot=>slot with{}).ToList()});
        w.OrganizationDevelopment=config.Read<OrganizationDevelopmentRules>("settlements/organization-development.json");
        w.LaborAutonomy=config.Read<LaborAutonomyRules>("organizations/labor-autonomy.json");
        ExternalMarketActions.EnableFresh(w,config);
        TimeActions.Refresh(w);WorldIntegrity.Validate(w);return w;
    }
    internal static void Validate(WorldState w)
    {
        OrganizationDevelopmentDecisions.Validate(w);
        if(w.SettlementOrganizationDemo is not { } p)return;
        if(w.SettlementRules is null||w.HexMap is null||w.OrganizationSiteRules is null||p.Organization is null||p.InitialStock is null||
            !Enum.IsDefined(p.Organization.Kind)||!Enum.IsDefined(p.InitialSiteKind)||
            !w.Organizations.Any(o=>o.Id==p.Organization.Id&&o.Kind==p.Organization.Kind)||
            !w.Characters.Any(c=>c.Id==p.Organization.LeaderId)||!w.Settlements.Any(s=>s.Id==p.SettlementId)||
            !w.HexMap.Sites.Any(s=>s.OrganizationId==p.Organization.Id)||
            p.InitialStock.Count!=2||p.InitialStock.Any(stock=>stock.Key is not ("spirit-stone" or "wood")||stock.Value<=0||
                w.Ledger.Count(l=>l.Tick==0&&l.From=="$genesis"&&l.To==p.Organization.Id&&l.ResourceId==stock.Key&&l.Quantity==stock.Value&&l.Reason=="Genesis")!=1)||
            w.OrganizationSiteReceipts.Count(r=>r.InitialPlacement&&w.OrganizationSites.Any(s=>s.Id==r.SiteId&&s.OrganizationId==p.Organization.Id&&s.SettlementId==p.SettlementId))!=1)
            throw new InvalidDataException("settlements/resident-organization.json: invalid demo snapshot or initial stock/site backing");
    }
}
