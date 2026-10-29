using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SettlementOrganizationTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(WorldState w){try{_ = new SimulationEngine(w);}catch(InvalidDataException){return;}throw new Exception("Invalid initial organization accepted");}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Village organization starts with one NPC leader and a paid level one HQ with finite assets",()=>{
            var baseline=SettlementScenario.Create();var w=SettlementOrganizationScenario.Create();var p=w.SettlementOrganizationDemo!;
            Check(w.Characters.Count==baseline.Characters.Count+1&&w.ControlledCharacterId==baseline.ControlledCharacterId,"NPC addition changed control or population template");
            var chief=w.Characters.Single(c=>c.Id==p.Organization.LeaderId);var site=w.OrganizationSites.Single();
            Check(chief.Id!=w.ControlledCharacterId&&chief.OrganizationId==p.Organization.Id&&chief.Position==OrganizationPosition.Leader,"Chief not autonomous leader");
            Check(site.Kind==OrganizationSiteKind.Headquarters&&site.Level==w.OrganizationSiteRules!.InitialLevel&&OrganizationSiteCapacity.Limit(w.OrganizationSiteRules,site)==3,"Initial HQ quota wrong");
            Check(Inventory.Balance(w,p.Organization.Id,"spirit-stone")==p.InitialStock["spirit-stone"]-w.OrganizationSiteRules!.ConstructionStones&&
                Inventory.Balance(w,p.Organization.Id,"wood")==p.InitialStock["wood"]-w.OrganizationSiteRules.ConstructionWood,"Initial construction cost or assets wrong");
            var village=w.HexMap!.Sites.Single(s=>s.Id==p.SettlementId);var scene=w.HexMap.Sites.Single(s=>s.OrganizationId==p.Organization.Id);
            Check(scene.Anchor==village.Anchor&&SceneSites.CanEnter(w,scene)&&w.Characters.Count(c=>c.OrganizationId==p.Organization.Id)==1,"Village independent entry or roster missing");
        }),
        ("Village organization saves keep assets and new game reconstructs the same dedicated profile",()=>{
            var w=SettlementOrganizationScenario.Create();var engine=new SimulationEngine(w);engine.Step(true);
            var snapshot=engine.Snapshot();var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(snapshot)));
            engine.Step(true);resumed.Step(true);Check(WorldCodec.Digest(engine.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Dedicated scenario continuation diverged");
            var fresh=NewGameFactory.CreateLike(snapshot,"重開測試");
            Check(fresh.SettlementOrganizationDemo is not null&&fresh.Clock.Tick==0&&fresh.OrganizationSites.Count==1,"New game lost organization scenario");
            Check(WorldCodec.Digest(snapshot)==WorldCodec.Digest(WorldCodec.Deserialize(WorldCodec.Serialize(snapshot))),"Snapshot changed during reconstruction");
        }),
        ("Initial headquarters exception cannot be used by unrelated organizations or unmatched genesis stock",()=>{
            var w=SettlementOrganizationScenario.Create();w.SettlementOrganizationDemo=null;
            Reject(w);
            w=SettlementOrganizationScenario.Create();w.SettlementOrganizationDemo!.InitialStock["wood"]++;
            Reject(w);
        })
    };
}
