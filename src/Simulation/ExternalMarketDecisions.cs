using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ExternalMarketDecisions
{
    internal static ActionIntent? Choose(WorldState w,CharacterState actor)
    {
        if(w.ExternalMarket is not { Rules.OrganizationAutoTrade:true } market||w.OrganizationDevelopment is not { } policy||
            w.SettlementOrganizationDemo is not { } profile||actor.Id==w.ControlledCharacterId||actor.OrganizationId!=profile.Organization.Id||
            !w.Organizations.Any(o=>o.Id==profile.Organization.Id&&o.LeaderId==actor.Id)||actor.Health<=0||actor.SeverelyInjured||CombatActions.Active(w,actor.Id) is not null)return null;
        string owner=profile.Organization.Id;
        var s=w.Settlements.Single(s=>s.Id==market.Rules.SettlementId);
        long money=Inventory.Balance(w,owner,"spirit-stone");
        long woodNeeded=policy.ReserveWood;
        var targets=OrganizationDevelopmentDecisions.Targets(w).ToArray();
        foreach(var (kind,level) in targets)
        {
            var building=s.Buildings.FirstOrDefault(b=>b.OwnerOrganizationId==owner&&b.Kind==kind);
            if(building is null)woodNeeded=checked(woodNeeded+w.SettlementConstructionRules!.Single(c=>c.Kind==kind).Wood+
                (level-w.SettlementOperationRules!.InitialBuildingLevel)*w.SettlementOperationRules.UpgradeWood);
            else woodNeeded=checked(woodNeeded+Math.Max(0,level-building.Level!.Value)*w.SettlementOperationRules!.UpgradeWood);
        }
        var site=w.OrganizationSites.Single(t=>t.OrganizationId==owner&&t.SettlementId==s.Id);
        if(targets.Length>OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,site)&&site.Level<policy.MaximumSiteLevel)
            woodNeeded=checked(woodNeeded+w.OrganizationSiteRules!.UpgradeWood);
        ActionIntent? choice=null;
        foreach(var item in market.Rules.BasePrices.Keys.OrderBy(k=>k,StringComparer.Ordinal))
        {
            long stock=Inventory.Balance(w,owner,item);
            long quantity=item=="wood"?Math.Max(0,stock-woodNeeded):stock;
            if(quantity<=0)continue;
            if(ExternalMarketPricing.Total(w,item,quantity,false)<1)continue;
            choice=Intent(ActionKind.ExternalMarketSell,item,quantity);break;
        }
        if(choice is null)
        {
            long missing=Math.Max(0,woodNeeded-Inventory.Balance(w,owner,"wood"));
            if(missing>0&&money>policy.ReserveMoney)
            {
                long affordable=(long)decimal.Floor((money-policy.ReserveMoney)/((decimal)market.Rules.BasePrices["wood"]*market.Rules.SaleMultiplier));
                if(affordable>0)choice=Intent(ActionKind.ExternalMarketBuy,"wood",Math.Min(missing,affordable));
            }
        }
        if(choice is null)return null;
        var anchor=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
        if(HexNavigation.Position(w.HexMap,actor)!=anchor)
            choice=new(choice.Id,actor.Id,anchor.ToString(),"travel",1,0,DecisionSource.Npc,ActionKind.Travel,DecisionDetail:"返回村莊處理組織市場交易");
        return ActionValidator.Validate(w,choice) is null?choice:null;
        ActionIntent Intent(ActionKind kind,string item,long amount)=>new($"external-market-ai:{actor.Id}:{w.Clock.Tick}",actor.Id,s.Id,item,amount,0,DecisionSource.Npc,kind,
            OrganizationId:owner,DecisionDetail:"出售超出建設保留需求的物資，或以保留錢財外資金補足建材");
    }
}
