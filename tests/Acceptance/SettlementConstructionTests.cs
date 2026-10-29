using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SettlementConstructionTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static WorldState Fixture(bool materials=true)
    {
        var w=SettlementScenario.Create(); w.SettlementDailyEconomy=null; w.SettlementRules=w.SettlementRules! with{DailyOutputOverrides=null}; w.OrganizationLaborRules=null; w.MemberRecruitmentRules=null; w.PopulationInvestmentRules=null; w.SettlementOperationRules=null; w.SettlementTaxRules=null;
        // Retain the 2026-10-22 paid-construction mode as a compatibility regression.
        w.OrganizationSiteRules=null;
        if(materials)w.Ledger.Add(new(w.NextSequence++,0,"construction-fixture","$genesis","sect","wood",
            w.SettlementConstructionRules!.Sum(r=>r.Wood),"Genesis"));
        return w;
    }
    private static ActionIntent Build(WorldState w,SettlementBuildingKind kind,HexCoord? position=null)=>new(
        "build-test:"+w.NextSequence,w.ControlledCharacterId,w.Settlements.Single().Id,kind.ToString(),1,0,DecisionSource.Player,
        ActionKind.BuildSettlement,OrganizationId:"sect",SettlementBuildPosition:position??w.HexMap!.Sites.Single(s=>s.Id==w.Settlements.Single().SiteId).Anchor);
    private static void Accepted(SimulationEngine e,ActionIntent i)
    {
        e.Submit(i);Check(e.Step().Single(r=>r.IntentId==i.Id).Accepted,"Construction rejected");
        Check(!e.Snapshot().Events.Any(v=>v.ActionId==i.Id&&v.Kind=="TimedCompletionRejected"),"Construction failed on completion");
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[] {
        ("Settlement construction pays sponsor assets builds five types and awards once",()=>{
            var w=Fixture();var s=w.Settlements.Single();var r=w.SettlementRules!;
            long start=Inventory.Balance(w,"sect","spirit-stone");var costs=w.SettlementConstructionRules!;
            var e=new SimulationEngine(w);
            foreach(var cost in costs)
            {
                w=e.Snapshot();var rule=w.SettlementBuildingRules!.Single(d=>d.Kind==cost.Kind);
                var tile=w.HexMap!.Tiles.First(t=>s.Territory.Contains(t.Coord)&&SettlementPlacement.Allows(rule,t));
                Accepted(e,Build(w,cost.Kind,tile.Coord));
            }
            w=e.Snapshot();var built=w.Settlements.Single();
            Check(w.SettlementConstruction.Count==costs.Count&&built.Buildings.Count==s.Buildings.Count+costs.Count,"Missing buildings");
            long otherIn=w.Ledger.Where(l=>l.Tick>0&&l.To=="sect"&&l.ResourceId=="spirit-stone"&&l.Reason!="SettlementConstruction").Sum(l=>l.Quantity);
            long otherOut=w.Ledger.Where(l=>l.Tick>0&&l.From=="sect"&&l.ResourceId=="spirit-stone"&&l.Reason!="SettlementConstruction").Sum(l=>l.Quantity);
            Check(Inventory.Balance(w,"sect","spirit-stone")==start-costs.Sum(c=>c.Stones)+otherIn-otherOut&&Inventory.Balance(w,"sect","wood")==0,"Sponsor not charged");
            Check(Inventory.Balance(w,built.OrganizationId,"spirit-stone")==r.InitialStones,"Charged village instead of sponsor");
            Check(built.PopulationCapacity==s.PopulationCapacity+r.HousingCapacity&&built.Influence["sect"]==costs.Count*r.BuildingInfluence,"Capacity or influence");
            Check(w.SettlementInfluence.Count==costs.Count,"Building also awarded material influence");
            var copy=WorldCodec.Clone(w);WorldIntegrity.Validate(copy);Check(WorldCodec.Digest(copy)==WorldCodec.Digest(w),"Construction snapshot changed");
        }),
        ("New settlement mine starts empty and pays wages before village receives minerals",()=>{
            var w=Fixture();var s=w.Settlements.Single();var pos=w.HexMap!.Tiles.Single(t=>s.Territory.Contains(t.Coord)&&t.Mountain).Coord;
            var e=new SimulationEngine(w);Accepted(e,Build(w,SettlementBuildingKind.Mine,pos));w=e.Snapshot();
            var mine=w.Settlements.Single().Buildings.Single(b=>b.Kind==SettlementBuildingKind.Mine);
            Check(mine.EmployeeCount==0&&Inventory.Balance(w,s.OrganizationId,"mineral")==0,"Free initial output/staff");
            var hire=new ActionIntent("hire-new-mine",w.ControlledCharacterId,s.Id,mine.Id,2,0,DecisionSource.Player,ActionKind.SetSettlementEmployees);
            e.Submit(hire);Check(e.Step().Single().Accepted,"Mine staffing failed");
            for(int n=0;n<w.Time!.Rules.TicksPerDay;n++)e.Step();w=e.Snapshot();
            Check(Inventory.Balance(w,s.OrganizationId,"mineral")==2*w.SettlementRules!.DailyOutputPerEmployee&&
                Inventory.Balance(w,"sect","mineral")==0,"Mine income did not belong to settlement");
            Check(Inventory.Balance(w,s.ResidentsAccountId,"spirit-stone")==2*w.SettlementRules.DailyWage,"Mine did not pay residents");
        }),
        ("Settlement construction refuses wrong terrain land outsiders and unsupported recipes",()=>{
            var w=Fixture();var e=new SimulationEngine(w);var bad=Build(w,SettlementBuildingKind.Mine);
            e.Submit(bad);Check(!e.Step().Single().Accepted,"Mine built on plains");
            w=e.Snapshot();bad=Build(w,SettlementBuildingKind.Pharmacy);e.Submit(bad);Check(!e.Step().Single().Accepted,"Pharmacy built");
            w=e.Snapshot();bad=Build(w,SettlementBuildingKind.Farm,new(99,99));e.Submit(bad);Check(!e.Step().Single().Accepted,"Built outside settlement");
            w=e.Snapshot();bad=Build(w,SettlementBuildingKind.Farm) with { OrganizationId="gang" };e.Submit(bad);Check(!e.Step().Single().Accepted,"Spent unowned organization assets");
            w=Fixture();var s=w.Settlements.Single();var pos=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
            int tile=w.HexMap.Tiles.FindIndex(t=>t.Coord==pos);w.HexMap.Tiles[tile]=w.HexMap.Tiles[tile] with { LocalOrganizationId="gang" };
            e=new(w);e.Submit(Build(w,SettlementBuildingKind.Farm));Check(!e.Step().Single().Accepted,"Built without land rights");
        }),
        ("Settlement construction requires donor wood and never falls back to village or player",()=>{
            var w=Fixture(false);var s=w.Settlements.Single();
            w.Ledger.Add(new(w.NextSequence++,0,"wrong-account-material","$genesis",s.OrganizationId,"wood",100,"Genesis"));
            var e=new SimulationEngine(w);long before=Inventory.Balance(w,"sect","spirit-stone");
            e.Submit(Build(w,SettlementBuildingKind.Housing));Check(!e.Step().Single().Accepted,"Used village wood as sponsor materials");
            w=e.Snapshot();Check(w.SettlementConstruction.Count==0&&w.Settlements.Single().Buildings.Count==4&&
                !w.Ledger.Any(l=>l.Reason=="SettlementConstruction"),"Failed construction charged money");
            w=Fixture();w.Ledger.RemoveAll(l=>l.To=="sect"&&l.ResourceId=="spirit-stone");e=new(w);
            e.Submit(Build(w,SettlementBuildingKind.Housing));Check(!e.Step().Single().Accepted,"Used village money as sponsor funds");
        }),
        ("Settlement construction duplicate reload and legacy mode do not repeat charges",()=>{
            var w=Fixture();var e=new SimulationEngine(w);var i=Build(w,SettlementBuildingKind.Housing);Accepted(e,i);
            w=e.Snapshot();long after=Inventory.Balance(w,"sect","spirit-stone");e=new(WorldCodec.Clone(w));e.Submit(i);
            Check(!e.Step().Single().Accepted,"Duplicate construction accepted");w=e.Snapshot();
            Check(w.SettlementConstruction.Count==1&&Inventory.Balance(w,"sect","spirit-stone")==after,"Duplicate charge/building");
            w=Fixture();w.SettlementConstructionRules=null;e=new(w);e.Submit(Build(w,SettlementBuildingKind.Housing));
            Check(!e.Step().Single().Accepted,"Old save enabled new construction");
        }),
        ("Settlement construction detects forged costs ownership points and invalid config",()=>{
            var w=Fixture();var e=new SimulationEngine(w);Accepted(e,Build(w,SettlementBuildingKind.Housing));w=e.Snapshot();
            var r=w.SettlementConstruction[0];w.SettlementConstruction[0]=r with { Wood=r.Wood+1 };
            try{WorldIntegrity.Validate(w);throw new Exception("Forged receipt accepted");}catch(InvalidDataException){}
            var costs=Fixture().SettlementConstructionRules!;costs[0]=costs[0] with { Stones=0 };
            try{SettlementConstructionIntegrity.ValidateRules(costs);throw new Exception("Invalid cost accepted");}catch(InvalidDataException){}
        })
    };
}
