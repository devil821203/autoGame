using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
using System.Text.Json.Nodes;

internal static class MarketOrderTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    internal static WorldState Fresh()
    {
        var w=TestScenarioCatalog.Create("methods");DiscreteTime.EnableFresh(w);ActionLifecycle.EnableFresh(w);EventDecisions.EnableFresh(w);ContentProfile.EnableFresh(w);MarketOrders.EnableFresh(w);
        // Same-location finite fixtures; no test-market issuance.
        var player=w.Characters.Single(a=>a.Id=="player");w.Characters=w.Characters.Select(a=>a with {DemoLocationId="town",HexPosition=player.HexPosition}).ToList();
        foreach(string owner in new[]{"player","npc:03","npc:06","npc:09"}){
            w.Ledger.Add(new(w.NextSequence++,0,"r5-cash:"+owner,"$genesis",owner,"spirit-stone",1000,"Genesis"));
            w.Ledger.Add(new(w.NextSequence++,0,"r5-herbs:"+owner,"$genesis",owner,"herb",100,"Genesis"));
        }
        WorldIntegrity.Validate(w);return w;
    }
    internal static ActionIntent Post(string id,string actor,bool buy,long price,long quantity=1)=>new(id,actor,"market","herb",quantity,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,buy?ActionKind.PlaceMarketBuy:ActionKind.PlaceMarketSell,LimitPrice:price);
    private static void Start(SimulationEngine e,ActionIntent i){Check(ActionValidator.Validate(e.Snapshot(),i) is null,"Invalid fixture: "+ActionValidator.Validate(e.Snapshot(),i));e.Submit(i);e.AdvanceTo(e.WorldTimeSeconds!.Value);Check(e.Snapshot().Time!.ActionRuntime!.Actions.ContainsKey(i.Id),"Command rejected");}
    private static void Finish(SimulationEngine e)=>e.AdvanceTo(e.WorldTimeSeconds!.Value+600);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R5 posting reserves real assets before activation and retains escrow after operation",()=>{
            var w=Fresh();long cash=Inventory.Balance(w,"player","spirit-stone");var e=new SimulationEngine(w);Start(e,Post("bid","player",true,8,5));w=e.Snapshot();
            Check(w.Time!.MarketOrders!.Orders["bid"].Phase==MarketOrderPhase.Submitting&&Inventory.Balance(w,"player","spirit-stone")==cash-40,"Not reserved at start");
            Finish(e);w=e.Snapshot();Check(w.Time!.MarketOrders!.Orders["bid"].Phase==MarketOrderPhase.Open&&w.Time.ActionRuntime!.Actions["bid"].Phase==ActionPhase.Completed&&Inventory.Balance(w,"$order:bid","spirit-stone")==40,"Posting completion refunded open order");
        }),
        ("R5 price time priority resting price partial fills and immediate improvement refunds",()=>{
            var e=new SimulationEngine(Fresh());Start(e,Post("first-ask","npc:03",false,5,2));Start(e,Post("cheaper-ask","npc:06",false,4,2));Start(e,Post("same-ask","npc:09",false,5,2));Finish(e);
            long cash=Inventory.Balance(e.Snapshot(),"player","spirit-stone"),herbs=Inventory.Balance(e.Snapshot(),"player","herb");Start(e,Post("buy","player",true,8,5));Finish(e);var w=e.Snapshot();var m=w.Time!.MarketOrders!;
            Check(m.Fills.Select(f=>f.SellOrderId).SequenceEqual(new[]{"cheaper-ask","first-ask","same-ask"}),"Wrong priority");
            Check(m.Fills.Select(f=>f.UnitPrice).SequenceEqual(new long[]{4,5,5})&&m.Orders["same-ask"].Remaining==1,"Wrong resting price or partial fill");
            Check(Inventory.Balance(w,"player","spirit-stone")==cash-23&&Inventory.Balance(w,"player","herb")==herbs+5&&m.Orders["buy"].Refunded==17,"Incorrect transfer/improvement");
            Check(MarketOrders.Reference(w,"market","herb")==23m/5,"Wrong quantity-weighted reference");WorldIntegrity.Validate(w);
        }),
        ("R5 incoming sell executes against older bid and self trading is excluded",()=>{
            var e=new SimulationEngine(Fresh());Start(e,Post("self-bid","player",true,9,2));Finish(e);Start(e,Post("self-ask","player",false,4,1));Finish(e);
            Check(e.Snapshot().Time!.MarketOrders!.Fills.Count==0,"Self fill occurred");Start(e,Post("other-ask","npc:03",false,4,1));Finish(e);
            Check(e.Snapshot().Time!.MarketOrders!.Fills.Single().UnitPrice==9,"Older bid did not set price");
        }),
        ("R5 cancellation consumes time AP and refunds only remaining quantity once",()=>{
            var e=new SimulationEngine(Fresh());Start(e,Post("ask","player",false,4,5));Finish(e);Start(e,Post("buy","npc:03",true,4,2));Finish(e);
            var before=e.Snapshot();long herbs=Inventory.Balance(before,"player","herb");int ap=before.Time!.Budgets.Single(b=>b.ActorId=="player").Remaining;
            var cancel=new ActionIntent("cancel","player","ask","herb",1,0,DecisionSource.Player,ActionKind.CancelMarketOrder);Start(e,cancel);
            Check(e.Snapshot().Time!.MarketOrders!.Orders["ask"].Phase==MarketOrderPhase.Open,"Cancelled before operation elapsed");Finish(e);var w=e.Snapshot();
            Check(w.Time!.MarketOrders!.Orders["ask"].Phase==MarketOrderPhase.Cancelled&&Inventory.Balance(w,"player","herb")==herbs+3&&w.Time.Budgets.Single(b=>b.ActorId=="player").Remaining==ap-1,"Refund/AP wrong");
            e.Submit(cancel);e.AdvanceTo(e.WorldTimeSeconds!.Value);Check(Inventory.Balance(e.Snapshot(),"player","herb")==herbs+3,"Replay refunded twice");
        }),
        ("R5 expiry is scheduled and SQLite JSON partial-order continuation is identical",()=>{
            var e=new SimulationEngine(Fresh());Start(e,Post("ask","player",false,4,5));Finish(e);Start(e,Post("bid","npc:03",true,5,2));Finish(e);var snapshot=e.Snapshot();
            foreach(string extension in new[]{"json","sqlite"}){
                string path=Path.Combine(Path.GetTempPath(),"r5-"+Guid.NewGuid()+"."+extension);
                try{var store=WorldStores.ForPath(path);store.Save(path,snapshot);var copy=store.Load(path);Check(WorldCodec.Digest(copy)==WorldCodec.Digest(snapshot),"Order/escrow/queue lost");
                    var a=new SimulationEngine(WorldCodec.Clone(snapshot));var b=new SimulationEngine(copy);long due=snapshot.Time!.MarketOrders!.Orders["ask"].ExpiresAt;a.AdvanceTo(due);b.AdvanceTo(due);
                    Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot())&&b.Snapshot().Time!.MarketOrders!.Orders["ask"].Phase==MarketOrderPhase.Expired,"Expiry continuation mismatch");}
                finally{if(File.Exists(path))File.Delete(path);}
            }
        }),
        ("R5 invalid assets overflow location authority and forged escrow are rejected",()=>{
            var w=Fresh();Check(ActionValidator.Validate(w,Post("huge","player",true,long.MaxValue,2)) is not null,"Overflow accepted");
            Check(ActionValidator.Validate(w,Post("poor","player",true,10000,10)) is not null,"Unfunded bid accepted");
            Check(ActionValidator.Validate(w,Post("org","npc:03",false,4) with {OrganizationId="sect"}) is not null,"Organization theft allowed");
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {DemoLocationId="grove"}:a).ToList();Check(ActionValidator.Validate(w,Post("far","player",false,4)) is not null,"Wrong location accepted");
            var e=new SimulationEngine(Fresh());Start(e,Post("real","player",true,4,2));w=e.Snapshot();w.Time!.MarketOrders!.Orders["real"].Reserved++;
            try{WorldIntegrity.Validate(w);throw new Exception("Forged escrow accepted");}catch(InvalidDataException){}
        }),
        ("R5 order knowledge does not reveal remote live prices or private cash",()=>{
            var w=Fresh();w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {DemoLocationId="grove",HexPosition=HexNavigation.Resolve(w.HexMap!,"grove")}:a).ToList();
            Check(MarketOrders.Observe(w,"player","market","herb") is null,"Remote book leaked");
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {DemoLocationId="town",HexPosition=HexNavigation.Resolve(w.HexMap!,"town")}:a).ToList();var fact=MarketOrders.Observe(w,"player","market","herb")!;
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {DemoLocationId="grove",HexPosition=HexNavigation.Resolve(w.HexMap!,"grove")}:a).ToList();
            var e=new SimulationEngine(w);Start(e,Post("new-ask","npc:03",false,9,2));Finish(e);w=e.Snapshot();
            var cached=MarketOrders.Observe(w,"player","market","herb");
            Check(cached is not null&&cached.ObservedAt==fact.ObservedAt&&cached.ReferencePrice==fact.ReferencePrice&&cached.Asks.SequenceEqual(fact.Asks)&&cached.Bids.SequenceEqual(fact.Bids),"Unseen live book changed cached fact");
            w.Time!.DiscreteEvents!.WorldTime=fact.ExpiresAt;Check(MarketOrders.Observe(w,"player","market","herb") is null,"Expired knowledge returned");
        }),
        ("R5 wire eight rejects hidden orders in old format and new game resets book",()=>{
            var w=Fresh();string path=Path.Combine(Path.GetTempPath(),"r5-"+Guid.NewGuid()+".json");
            try{new JsonWorldStore().Save(path,w);var node=JsonNode.Parse(File.ReadAllText(path))!;Check(node["SchemaVersion"]!.GetValue<int>()==8,"Missing wire guard");node["SchemaVersion"]=7;File.WriteAllText(path,node.ToJsonString());
                try{new JsonWorldStore().Load(path);throw new Exception("Old format hid orders");}catch(InvalidDataException){}
            }finally{if(File.Exists(path))File.Delete(path);}
            var e=new SimulationEngine(w);Start(e,Post("reset","player",true,4));Finish(e);var fresh=NewGameFactory.CreateLike(e.Snapshot(),"新交易者");Check(fresh.Time!.MarketOrders!.Orders.Count==0&&fresh.Time.Charges.Count==0,"New game copied live book");
        })
    };
}
