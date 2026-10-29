using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class OrganizationSiteActions
{
    internal static bool Handles(ActionKind k)=>k is ActionKind.CreateOrganizationSite or ActionKind.UpgradeOrganizationSite;
    internal static string? Validate(WorldState w,SettlementState s,ActionIntent i)
    {
        if(w.OrganizationSiteRules is not { } rules||i.Quantity!=1)return "Organization site rules unavailable or invalid quantity";
        bool create=i.Kind==ActionKind.CreateOrganizationSite;
        if(i.OrganizationId is null||i.OrganizationId==s.OrganizationId||
            !w.Organizations.Any(o=>o.Id==i.OrganizationId&&o.LeaderId==i.ActorId)||
            create&&!w.Organizations.Any(o=>o.Id==s.OrganizationId&&o.LeaderId==i.ActorId))return "Sponsor and settlement management permission required";
        var site=w.OrganizationSites.SingleOrDefault(site=>site.SettlementId==s.Id&&site.OrganizationId==i.OrganizationId);
        if(create)
        {
            if(site is not null)return "Organization already has a site in this settlement";
            if(!Enum.TryParse<OrganizationSiteKind>(i.ResourceId,out var kind)||!rules.Sites.Any(d=>d.Kind==kind))return "Invalid organization site kind";
        }
        else if(site is null||site.Id!=i.ResourceId||site.Level==int.MaxValue)return "Organization site upgrade unavailable";
        long stones=create?rules.ConstructionStones:rules.UpgradeStones,wood=create?rules.ConstructionWood:rules.UpgradeWood;
        return Inventory.Balance(w,i.OrganizationId,"spirit-stone")<stones||Inventory.Balance(w,i.OrganizationId,"wood")<wood?
            "Insufficient sponsor site assets":null;
    }
    internal static void Execute(WorldState w,SettlementState s,ActionIntent i)
    {
        var rules=w.OrganizationSiteRules!;bool create=i.Kind==ActionKind.CreateOrganizationSite;
        long stones=create?rules.ConstructionStones:rules.UpgradeStones,wood=create?rules.ConstructionWood:rules.UpgradeWood;
        foreach(var (item,amount) in new[]{("spirit-stone",stones),("wood",wood)})
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,i.OrganizationId!,"consumed",item,amount,"OrganizationSitePayment"));
        OrganizationSettlementSite site;
        if(create)
        {
            site=new("organization-site:"+i.Id,s.Id,i.OrganizationId!,Enum.Parse<OrganizationSiteKind>(i.ResourceId),rules.InitialLevel);
            w.OrganizationSites.Add(site);
        }
        else
        {
            int n=w.OrganizationSites.FindIndex(site=>site.Id==i.ResourceId);
            site=w.OrganizationSites[n] with{Level=checked(w.OrganizationSites[n].Level+1)};w.OrganizationSites[n]=site;
        }
        w.OrganizationSiteReceipts.Add(new(i.Id,site.Id,w.Clock.Tick,site.Level,stones,wood));
        if(w.HexMap is { } map)
        {
            var anchor=map.Sites.Single(t=>t.Id==s.SiteId).Anchor;
            for(int n=0;n<map.Sites.Count;n++)
                if(map.Sites[n].OrganizationId==site.OrganizationId&&map.Sites[n].Anchor==anchor)
                    map.Sites[n]=map.Sites[n] with{MainLevel=site.Level};
        }
    }
}

public static class OrganizationSiteIntegrity
{
    public static void ValidateRules(OrganizationSiteRules rules)
    {
        if(rules.Status!="Enabled"||rules.InitialLevel<=0||rules.ConstructionStones<=0||rules.ConstructionWood<=0||
            rules.UpgradeStones<=0||rules.UpgradeWood<=0||rules.Sites is null||rules.Sites.Count!=Enum.GetValues<OrganizationSiteKind>().Length||
            rules.Sites.Select(d=>d.Kind).Distinct().Count()!=rules.Sites.Count||rules.Sites.Any(d=>!Enum.IsDefined(d.Kind)||
                string.IsNullOrWhiteSpace(d.Name)||d.BaseBuildingCapacity<0||d.BuildingsPerLevel<=0))
            throw new InvalidDataException("config/settlements/organization-sites.json: invalid Status, InitialLevel, costs or Sites capacity");
    }
    public static void Validate(WorldState w)
    {
        if(w.OrganizationSiteRules is not { } rules)
        {
            if(w.OrganizationSites.Count>0||w.OrganizationSiteReceipts.Count>0)throw new InvalidDataException("Organization sites without rules");
            return;
        }
        ValidateRules(rules);
        if(w.SettlementRules is null||w.OrganizationSites.Select(s=>s.Id).Distinct().Count()!=w.OrganizationSites.Count||
            w.OrganizationSites.Select(s=>(s.SettlementId,s.OrganizationId)).Distinct().Count()!=w.OrganizationSites.Count||
            w.OrganizationSiteReceipts.Select(r=>r.Id).Distinct().Count()!=w.OrganizationSiteReceipts.Count)
            throw new InvalidDataException("Invalid organization site identity");
        foreach(var site in w.OrganizationSites)
        {
            if(!Enum.IsDefined(site.Kind)||site.Level<rules.InitialLevel||!w.Settlements.Any(s=>s.Id==site.SettlementId&&s.OrganizationId!=site.OrganizationId)||
                !w.Organizations.Any(o=>o.Id==site.OrganizationId)||OrganizationSiteCapacity.Used(w,site)>OrganizationSiteCapacity.Limit(rules,site))
                throw new InvalidDataException("Invalid organization site or construction capacity");
            var receipts=w.OrganizationSiteReceipts.Where(r=>r.SiteId==site.Id).OrderBy(r=>r.Tick).ThenBy(r=>r.ResultLevel).ToList();
            if(receipts.Count!=(long)site.Level-rules.InitialLevel+1||receipts.Where((r,n)=>r.ResultLevel!=(long)rules.InitialLevel+n).Any())
                throw new InvalidDataException("Missing or duplicate organization site level receipt");
        }
        foreach(var r in w.OrganizationSiteReceipts)
        {
            var site=w.OrganizationSites.SingleOrDefault(s=>s.Id==r.SiteId);bool create=r.ResultLevel==rules.InitialLevel;
            if(site is null||r.Tick<0||r.Tick>w.Clock.Tick||
                (r.InitialPlacement ? r.Tick!=0||!create||w.SettlementOrganizationDemo is not { } initial||initial.Organization.Id!=site.OrganizationId||initial.SettlementId!=site.SettlementId||initial.InitialSiteKind!=site.Kind : r.Tick==0)||
                r.Stones!=(create?rules.ConstructionStones:rules.UpgradeStones)||
                r.Wood!=(create?rules.ConstructionWood:rules.UpgradeWood)||
                w.Ledger.Count(e=>e.ActionId==r.Id&&e.Tick==r.Tick&&e.From==site.OrganizationId&&e.To=="consumed"&&
                    e.ResourceId=="spirit-stone"&&e.Quantity==r.Stones&&e.Reason=="OrganizationSitePayment")!=1||
                w.Ledger.Count(e=>e.ActionId==r.Id&&e.Tick==r.Tick&&e.From==site.OrganizationId&&e.To=="consumed"&&
                    e.ResourceId=="wood"&&e.Quantity==r.Wood&&e.Reason=="OrganizationSitePayment")!=1)
                throw new InvalidDataException("Invalid organization site payment receipt");
        }
        if(w.SettlementConstruction.Any(c=>!w.OrganizationSites.Any(s=>s.OrganizationId==c.SponsorId&&s.SettlementId==c.SettlementId)))
            throw new InvalidDataException("Settlement construction without organization site");
    }
}
