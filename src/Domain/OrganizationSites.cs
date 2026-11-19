namespace Cultivation.Domain;

public enum OrganizationSiteKind { Branch, Headquarters }
public sealed record OrganizationSiteDefinition(OrganizationSiteKind Kind,string Name,int BaseBuildingCapacity,int BuildingsPerLevel);
public sealed record OrganizationSiteRules(string Status,int InitialLevel,long ConstructionStones,long ConstructionWood,
    long UpgradeStones,long UpgradeWood,List<OrganizationSiteDefinition> Sites);
public sealed record OrganizationSettlementSite(string Id,string SettlementId,string OrganizationId,OrganizationSiteKind Kind,int Level);
public sealed record OrganizationSiteReceipt(string Id,string SiteId,long Tick,int ResultLevel,long Stones,long Wood)
{
    public bool InitialPlacement { get; init; }
}
public static class OrganizationSiteCapacity
{
    public static long Limit(OrganizationSiteRules rules,OrganizationSettlementSite site)
    {
        var kind=rules.Sites.Single(d=>d.Kind==site.Kind);
        return checked(kind.BaseBuildingCapacity+(long)(site.Level-rules.InitialLevel)*kind.BuildingsPerLevel);
    }
    public static long Used(WorldState w,OrganizationSettlementSite site)=>w.SettlementConstruction.LongCount(r=>
        r.SettlementId==site.SettlementId&&r.SponsorId==site.OrganizationId);
}
