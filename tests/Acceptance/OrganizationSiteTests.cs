using Cultivation.Domain;
using Cultivation.Simulation;

internal static class OrganizationSiteTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static WorldState Fixture(bool materials=true)
    {
        var w=SettlementScenario.Create(); w.SettlementDailyEconomy=null; w.SettlementRules=w.SettlementRules! with{DailyOutputOverrides=null}; w.OrganizationLaborRules=null; w.MemberRecruitmentRules=null; w.PopulationInvestmentRules=null; w.SettlementOperationRules=null; w.SettlementTaxRules=null;
        if(materials)w.Ledger.Add(new(w.NextSequence++,0,"org-site-fixture","$genesis","sect","wood",200,"Genesis"));
        return w;
    }
    private static ActionIntent Order(WorldState w,ActionKind kind,string resource,HexCoord? pos=null)=>new("site-test:"+w.NextSequence,
        w.ControlledCharacterId,w.Settlements.Single().Id,resource,1,0,DecisionSource.Player,kind,OrganizationId:"sect",SettlementBuildPosition:pos);
    private static bool Act(SimulationEngine e,ActionIntent i)
    {
        e.Submit(i);var results=e.Step();return results.Single(r=>r.IntentId==i.Id).Accepted&&
            !e.Snapshot().Events.Any(v=>v.ActionId==i.Id&&v.Kind=="TimedCompletionRejected");
    }
    private static void Build(SimulationEngine e)
    {
        var w=e.Snapshot();var anchor=w.HexMap!.Sites.Single(s=>s.Id==w.Settlements.Single().SiteId).Anchor;
        Check(Act(e,Order(w,ActionKind.BuildSettlement,"Housing",anchor)),"Public building rejected");
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Organization branch gates construction counts sponsor buildings and expands quota on paid upgrade",()=>{
            var e=new SimulationEngine(Fixture());var w=e.Snapshot();var anchor=w.HexMap!.Sites.Single(s=>s.Id==w.Settlements.Single().SiteId).Anchor;
            Check(!Act(e,Order(w,ActionKind.BuildSettlement,"Housing",anchor)),"Built without organization site");
            Check(Act(e,Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Branch")),"Branch creation failed");
            w=e.Snapshot();var site=w.OrganizationSites.Single();Check(OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,site)==3,"Level1 branch capacity");
            Check(OrganizationSiteCapacity.Used(w,site)==0,"Counted initial buildings or branch itself");
            for(int n=0;n<3;n++)Build(e);
            Check(!Act(e,Order(e.Snapshot(),ActionKind.BuildSettlement,"Housing",anchor)),"Exceeded branch quota");
            w=e.Snapshot();Check(Act(e,Order(w,ActionKind.UpgradeOrganizationSite,site.Id)),"Branch upgrade failed");
            Build(e);w=e.Snapshot();site=w.OrganizationSites.Single();
            Check(site.Level==2&&OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,site)==4&&OrganizationSiteCapacity.Used(w,site)==4,"Level2 branch quota");
            Check(w.OrganizationSiteReceipts.Count==2&&w.SettlementConstruction.Count==4,"Missing receipts");
        }),
        ("Organization headquarters starts at three then adds two per level with one site per organization per settlement",()=>{
            var e=new SimulationEngine(Fixture());Check(Act(e,Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Headquarters")),"HQ failed");
            var w=e.Snapshot();var site=w.OrganizationSites.Single();Check(OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,site)==3,"HQ initial quota");
            Check(!Act(e,Order(w,ActionKind.CreateOrganizationSite,"Branch")),"Created duplicate organization site");
            Check(Act(e,Order(e.Snapshot(),ActionKind.UpgradeOrganizationSite,site.Id)),"HQ upgrade failed");
            w=e.Snapshot();Check(OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,w.OrganizationSites.Single())==5,"HQ level2 quota");
            Check(Act(e,Order(w,ActionKind.UpgradeOrganizationSite,site.Id)),"HQ second upgrade failed");
            w=e.Snapshot();Check(OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,w.OrganizationSites.Single())==7,"HQ level3 quota");
        }),
        ("Organization sites consume sponsor configured costs without granting public building influence",()=>{
            var e=new SimulationEngine(Fixture());Check(Act(e,Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Branch")),"Site failed");
            var w=e.Snapshot();var site=w.OrganizationSites.Single();Check(Act(e,Order(w,ActionKind.UpgradeOrganizationSite,site.Id)),"Upgrade failed");
            w=e.Snapshot();var r=w.OrganizationSiteRules!;
            Check(w.Ledger.Where(l=>l.Reason=="OrganizationSitePayment"&&l.ResourceId=="wood").Sum(l=>l.Quantity)==r.ConstructionWood+r.UpgradeWood,"Wrong wood costs");
            Check(w.Ledger.Where(l=>l.Reason=="OrganizationSitePayment"&&l.ResourceId=="spirit-stone").Sum(l=>l.Quantity)==r.ConstructionStones+r.UpgradeStones,"Wrong stone costs");
            Check(w.SettlementInfluence.Count==0&&w.Settlements.Single().Influence.Count==0,"Extra influence from site");
        }),
        ("Organization site material shortage permissions and duplicate intent cannot create or double-charge",()=>{
            var e=new SimulationEngine(Fixture(false));Check(!Act(e,Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Branch")),"Free materials");
            Check(e.Snapshot().OrganizationSites.Count==0&&!e.Snapshot().Ledger.Any(l=>l.Reason=="OrganizationSitePayment"),"Failed site charged assets");
            e=new(Fixture());var i=Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Branch");
            Check(!Act(e,i with{OrganizationId="gang"}),"Used other organization funds");i=Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Branch");
            Check(Act(e,i),"Site rejected");var w=e.Snapshot();e=new(WorldCodec.Clone(w));
            Check(!Act(e,i)&&e.Snapshot().OrganizationSiteReceipts.Count==1,"Duplicate site charge");
        }),
        ("Organization site snapshots and new config do not activate legacy saves",()=>{
            var w=Fixture();w.OrganizationSiteRules=null;var e=new SimulationEngine(w);
            Check(!Act(e,Order(w,ActionKind.CreateOrganizationSite,"Branch")),"Activated legacy save");
            e=new(Fixture());Check(Act(e,Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Headquarters")),"HQ failed");
            w=e.Snapshot();var clone=WorldCodec.Clone(w);WorldIntegrity.Validate(clone);
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(clone),"Site snapshot changed");
            Check(NewGameFactory.CreateLike(w,"新據點").OrganizationSites.Count==0,"New game inherited site");
        }),
        ("Organization site integrity rejects missing paid level receipts and invalid config",()=>{
            var e=new SimulationEngine(Fixture());Check(Act(e,Order(e.Snapshot(),ActionKind.CreateOrganizationSite,"Branch")),"Site failed");
            var w=e.Snapshot();w.OrganizationSites[0]=w.OrganizationSites[0] with{Level=2};
            try{WorldIntegrity.Validate(w);throw new Exception("Unpaid site level accepted");}catch(InvalidDataException){}
            var r=Fixture().OrganizationSiteRules!;
            try{OrganizationSiteIntegrity.ValidateRules(r with{UpgradeWood=0});throw new Exception("Invalid cost accepted");}catch(InvalidDataException){}
        })
    };
}
