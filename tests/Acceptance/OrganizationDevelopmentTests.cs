using Cultivation.Domain;
using Cultivation.Simulation;

internal static class OrganizationDevelopmentTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static WorldState LegacyDevelopment(){var w=SettlementOrganizationScenario.Create();w.LaborAutonomy=null;return w;}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Resident leader autonomously builds, staffs and upgrades using finite organization assets",()=>{
            var engine=new SimulationEngine(LegacyDevelopment());
            for(int tick=0;tick<90;tick++)engine.Step(true);
            var w=engine.Snapshot();var org=w.SettlementOrganizationDemo!.Organization.Id;
            var buildings=w.Settlements.Single().Buildings.Where(b=>b.OwnerOrganizationId==org).ToArray();
            Check(buildings.Length==3&&buildings.All(b=>b.Level==2),"Expected exactly three level two production buildings");
            Check(buildings.Sum(b=>b.EmployeeCount)>0&&w.Settlements.Single().Buildings.Sum(b=>b.EmployeeCount)<=w.Settlements.Single().WorkingPopulation,"Invalid autonomous staffing");
            Check(w.OrganizationSites.Single().Level==1,"HQ upgraded without a quota need");
            Check(w.SettlementConstruction.Where(c=>c.SponsorId==org).Select(c=>c.Kind).SequenceEqual(w.OrganizationDevelopment!.BuildOrder),"Build order changed");
            Check(w.SettlementConstruction.Count(c=>c.SponsorId==org)==3&&w.SettlementBuildingUpgrades.Count(c=>c.OwnerId==org)==3,"Missing paid construction receipts");
            var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w)));
            engine.Step(true);resumed.Step(true);
            Check(WorldCodec.Digest(engine.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Autonomous development diverged after save");
        }),
        ("Reserves stop autonomous spending and absent snapshots preserve old behavior",()=>{
            var w=LegacyDevelopment();w.OrganizationDevelopment=w.OrganizationDevelopment! with{ReserveMoney=1000};
            w.ExternalMarket=null; // Isolate construction reserves from the newly authorized sales income.
            var engine=new SimulationEngine(w);for(int t=0;t<15;t++)engine.Step(true);
            Check(engine.Snapshot().SettlementConstruction.Count==0,"Spent reserved funds");
            w=LegacyDevelopment();w.OrganizationDevelopment=null;
            engine=new SimulationEngine(w);for(int t=0;t<15;t++)engine.Step(true);
            Check(engine.Snapshot().SettlementConstruction.Count==0,"Old snapshot enabled new strategy");
        }),
        ("Quota pressure upgrades HQ once and synchronizes its scene level",()=>{
            var w=LegacyDevelopment();
            w.OrganizationSiteRules=w.OrganizationSiteRules! with{Sites=w.OrganizationSiteRules!.Sites.Select(s=>s with{BaseBuildingCapacity=2}).ToList()};
            var engine=new SimulationEngine(w);for(int t=0;t<70;t++)engine.Step(true);
            w=engine.Snapshot();var site=w.OrganizationSites.Single();
            Check(site.Level==2&&w.SettlementConstruction.Count==3,"Quota upgrade did not unblock construction");
            Check(w.HexMap!.Sites.Single(s=>s.OrganizationId==site.OrganizationId).MainLevel==2,"HQ scene level stale");
        }),
        ("Invalid autonomous development config is rejected",()=>{
            var w=LegacyDevelopment();w.OrganizationDevelopment=w.OrganizationDevelopment! with{ReserveWood=-1};
            try{_ = new SimulationEngine(w);}catch(InvalidDataException){return;}
            throw new Exception("Negative reserve accepted");
        })
    };
}
