using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class RegionalWorldTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static ActionIntent Travel(string id,string target)=>new(id,"player",target,"travel",1,0,DecisionSource.Player,ActionKind.Travel);
    private static SimulationEngine Trip(out WorldRegion destination,out long cost)
    {
        var w=RegionalWorldScenario.Create();destination=w.HexMap!.Regional!.Regions[1];string target=RegionGeometry.PlaceId(w.HexMap,destination.Id,"town");
        cost=HexNavigation.Duration(w,w.Characters.Single(a=>a.Id=="player"),target);Check(cost<=w.Time!.Rules.DailyActionPoints,"Fixture route needs intermediate waypoint");
        var i=Travel("regional-trip",target);Check(ActionValidator.Validate(w,i) is null,"Travel rejected: "+ActionValidator.Validate(w,i));var e=new SimulationEngine(w);e.Submit(i);e.AdvanceTo(0);return e;
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R6a same seed content and independent generation streams reproduce world",()=>{
            var a=RegionalWorldScenario.Create(seed:12345);var b=RegionalWorldScenario.Create(seed:12345);var c=RegionalWorldScenario.Create(seed:98765);
            Check(WorldCodec.Digest(a)==WorldCodec.Digest(b),"Same seed changed initial world");
            Check(JsonSerializer.Serialize(a.HexMap!.Tiles)!=JsonSerializer.Serialize(c.HexMap!.Tiles),"Different seed ignored");
            Check(a.HexMap.Regional!.Regions.Count==3&&a.HexMap.Tiles.Count==147,"Confirmed demo dimensions wrong");
            Console.WriteLine("R6A_INITIAL_EVIDENCE "+JsonSerializer.Serialize(new{Content=a.Time!.Content,Topology=a.HexMap.Regional,TileCount=a.HexMap.Tiles.Count,InitialLedgerCount=a.Ledger.Count,InitialRng=a.RandomState,Digest=WorldCodec.Digest(a)}));
        }),
        ("R6a generated settlements resources routes and initial assets are legal",()=>{
            foreach(ulong seed in new ulong[]{0,1,17,12345,98765}){
                var w=RegionalWorldScenario.Create(seed:seed);var map=w.HexMap!;var t=map.Regional!;
                foreach(var region in t.Regions){Check(HexNavigation.Path(map,t.Regions[0].Town,region.Town) is not null&&HexNavigation.Path(map,region.Town,region.Resource) is not null,"Disconnected supply");Check(Inventory.Balance(w,region.ResourceOwner,"herb")>0,"No initial resource supply");}
                WorldIntegrity.Validate(w);
            }
        }),
        ("R6a cross-region travel uses existing per-hex duration AP and coherent arrival",()=>{
            var e=Trip(out var destination,out long cost);var initial=e.Snapshot();int ap=initial.Time!.Budgets.Single(b=>b.ActorId=="player").Remaining;
            Check(ap==initial.Time.Rules.DailyActionPoints-cost,"Wrong upfront travel AP");e.AdvanceTo(cost*DiscreteTime.SecondsPerStep(initial));var w=e.Snapshot();var a=w.Characters.Single(c=>c.Id=="player");
            Check(a.RegionId==destination.Id&&a.DemoLocationId=="town"&&a.HexPosition==destination.Town&&w.Time!.ActionRuntime!.Actions["regional-trip"].Phase==ActionPhase.Completed,"Cross-region arrival mismatch");
            var site=w.HexMap!.Sites.Single(s=>s.Anchor==destination.Town);Check(SceneSites.CanEnter(w,site),"Generated settlement cannot be entered");
            Check(MarketOrders.Observe(w,"player",destination.MarketOwner,"herb") is not null&&MarketOrders.Observe(w,"player","market","herb") is null,"Market knowledge not regional");
            string home=RegionGeometry.PlaceId(w.HexMap,w.HexMap.Regional!.Regions[0].Id,"town");Check(ActionValidator.Validate(w,Travel("return",home)) is null,"Cannot travel back from non-valley region");
        }),
        ("R6a local resource gathering consumes that region finite stock",()=>{
            var e=Trip(out var destination,out long cost);e.AdvanceTo(cost*600);var w=e.Snapshot();
            string target=RegionGeometry.PlaceId(w.HexMap!,destination.Id,"grove");long walk=HexNavigation.Duration(w,w.Characters.Single(a=>a.Id=="player"),target);
            e.Submit(Travel("to-local-resource",target));e.AdvanceTo(e.WorldTimeSeconds!.Value+walk*600);w=e.Snapshot();
            long local=Inventory.Balance(w,destination.ResourceOwner,"herb"),home=Inventory.Balance(w,"grove","herb"),own=Inventory.Balance(w,"player","herb");
            var gather=new ActionIntent("regional-gather","player","grove","herb",1,0,DecisionSource.Player,ActionKind.Gather);
            Check(ActionValidator.Validate(w,gather) is null,"Regional gather rejected: "+ActionValidator.Validate(w,gather));e.Submit(gather);e.AdvanceTo(e.WorldTimeSeconds!.Value+w.Time!.Costs[ActionKind.Gather]*600);
            w=e.Snapshot();Check(Inventory.Balance(w,destination.ResourceOwner,"herb")==local-1&&Inventory.Balance(w,"grove","herb")==home&&Inventory.Balance(w,"player","herb")==own+1,"Gather borrowed home inventory or created stock");
        }),
        ("R6a closing a route interrupts at boundary without refunding prepaid travel AP",()=>{
            var e=Trip(out _,out long cost);var initial=e.Snapshot();var route=initial.HexMap!.Regional!.Routes[0];
            while(e.Snapshot().Characters.Single(a=>a.Id=="player").HexPosition!=route.From)e.Step();
            var w=e.Snapshot();Check(w.HexMap!.Journeys.Single(j=>j.ActorId=="player").Remaining[0]==route.To,"Wrong boundary fixture");
            w.HexMap.Regional!.Routes[0]=route with {Open=false};e=new(w);e.Step();w=e.Snapshot();var a=w.Characters.Single(a=>a.Id=="player");
            Check(a.HexPosition==route.From&&a.RegionId==route.FromRegion&&w.HexMap!.Journeys.All(j=>j.ActorId!="player")&&w.Time!.ActionRuntime!.Actions["regional-trip"].Phase==ActionPhase.Interrupted&&w.Time.Budgets.Single(b=>b.ActorId=="player").Remaining==w.Time.Rules.DailyActionPoints-cost,"Interruption location/refund wrong");
        }),
        ("R6a JSON SQLite persist region routes and partial itinerary with exact continuation",()=>{
            var e=Trip(out _,out long cost);e.Step();var checkpoint=e.Snapshot();
            foreach(string extension in new[]{"json","sqlite"}){
                string path=Path.Combine(Path.GetTempPath(),"r6a-"+Guid.NewGuid()+"."+extension);
                try{var store=WorldStores.ForPath(path);store.Save(path,checkpoint);var restored=store.Load(path);Check(WorldCodec.Digest(restored)==WorldCodec.Digest(checkpoint),"Regional checkpoint changed");
                    var a=new SimulationEngine(WorldCodec.Clone(checkpoint));var b=new SimulationEngine(restored);a.AdvanceTo(cost*600);b.AdvanceTo(cost*600);Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Regional continuation diverged");
                    if(extension=="json"){var node=JsonNode.Parse(File.ReadAllText(path))!;Check(node["SchemaVersion"]!.GetValue<int>()==9,"Missing format guard");node["SchemaVersion"]=8;File.WriteAllText(path,node.ToJsonString());try{store.Load(path);throw new Exception("Old format hid regions");}catch(InvalidDataException){}}
                }finally{if(File.Exists(path))File.Delete(path);}
            }
        }),
        ("R6a bad region position routes and dimensions are rejected",()=>{
            var w=RegionalWorldScenario.Create();var t=w.HexMap!.Regional!;try{RegionalWorldIntegrity.ValidateRules(t.Rules with {Width=0});throw new Exception("Accepted dimensions");}catch(InvalidDataException){}
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {RegionId=t.Regions[1].Id}:a).ToList();try{WorldIntegrity.Validate(w);throw new Exception("Accepted wrong region");}catch(InvalidDataException){}
            w=RegionalWorldScenario.Create();t=w.HexMap!.Regional!;t.Routes[0]=t.Routes[0] with {To=t.Regions[1].Town};try{WorldIntegrity.Validate(w);throw new Exception("Accepted invalid route");}catch(InvalidDataException){}
        }),
        ("R6a regional order venues never match across region boundaries",()=>{
            var w=RegionalWorldScenario.Create();string seller="npc:03";var home=w.HexMap!.Regional!.Regions[0];
            w.Characters=w.Characters.Select(a=>a.Id==seller?a with {DemoLocationId="town",HexPosition=home.Town}:a).ToList();
            w.Ledger.Add(new(w.NextSequence++,0,"r6a-fixture-herb","$genesis",seller,"herb",1,"Genesis"));
            var e=new SimulationEngine(w);e.Submit(new("home-offer",seller,"market","herb",1,0,DecisionSource.Npc,ActionKind.PlaceMarketSell,LimitPrice:1));e.AdvanceTo(600);
            var region=w.HexMap.Regional.Regions[1];string target=RegionGeometry.PlaceId(w.HexMap,region.Id,"town");long cost=HexNavigation.Duration(e.Snapshot(),e.Snapshot().Characters.Single(a=>a.Id=="player"),target);
            e.Submit(Travel("regional-market-trip",target));e.AdvanceTo(600+cost*600);
            var bid=new ActionIntent("local-bid","player",region.MarketOwner,"herb",1,0,DecisionSource.Player,ActionKind.PlaceMarketBuy,LimitPrice:2);
            Check(ActionValidator.Validate(e.Snapshot(),bid) is null,"Regional market order rejected");e.Submit(bid);e.AdvanceTo(e.WorldTimeSeconds!.Value+600);w=e.Snapshot();
            Check(w.Time!.MarketOrders!.Fills.Count==0&&w.Time.MarketOrders.Orders["local-bid"].Phase==MarketOrderPhase.Open&&w.Time.MarketOrders.Orders["home-offer"].Phase==MarketOrderPhase.Open,"Cross-region fill leaked inventory");
        }),
        ("R6a new game preserves seed and generation mode without carrying itinerary",()=>{
            var e=Trip(out _,out _);e.Step();var w=NewGameFactory.CreateLike(e.Snapshot(),"新旅人");
            Check(w.HexMap!.Regional!.Seed==e.Snapshot().HexMap!.Regional!.Seed&&w.HexMap.Journeys.Count==0&&w.Clock.Tick==0&&w.Time!.Charges.Count==0,"New game lost seed or copied work");WorldIntegrity.Validate(w);
        })
    };
}
