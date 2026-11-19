using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class R2DeferredTradeTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static WorldState World(){
        var w=LifeEventScenario.Create();int n=w.Characters.FindIndex(c=>c.Id=="player");var actor=w.Characters[n];
        w.Characters[n]=actor with {DemoLocationId="town",HexPosition=RegionGeometry.Resolve(w.HexMap!,actor,"town")};
        w.Ledger.Add(new(w.NextSequence++,0,"trade-fixture-herb","$genesis","market","herb",5,"Genesis"));
        w.Ledger.Add(new(w.NextSequence++,0,"trade-fixture-funds","$genesis","player","spirit-stone",100,"Genesis"));
        return w;
    }
    private static ActionIntent Buy(string id,long? limit=null)=>new(id,"player","market","herb",1,0,DecisionSource.Player,ActionKind.Buy,LimitPrice:limit);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 deferred sale uses original price and transfers once",()=>{
            var w=World();w.Ledger.Add(new(w.NextSequence++,0,"sell-fixture","$genesis","player","herb",1,"Genesis"));
            w.Ledger.Add(new(w.NextSequence++,0,"market-fixture","$genesis","market","spirit-stone",100,"Genesis"));
            long stock=Inventory.Balance(w,"player","herb"),funds=Inventory.Balance(w,"player","spirit-stone");
            var e=new SimulationEngine(w);e.Submit(new("sale","player","market","herb",1,0,DecisionSource.Player,ActionKind.Sell));e.AdvanceTo(0);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions.TryGetValue("sale",out var action)&&action.Phase==ActionPhase.Running&&Inventory.Balance(w,"player","herb")==stock&&Inventory.Balance(w,"player","spirit-stone")==funds,"Sale debited early or rejected");
            e.AdvanceTo(w.Time.ActionRuntime.Actions["sale"].DueAt);w=e.Snapshot();var receipt=w.Trades.Single(t=>t.ActionId=="sale");
            Check(w.Time!.ActionRuntime!.Actions["sale"].Phase==ActionPhase.Completed&&Inventory.Balance(w,"player","herb")==stock-1&&Inventory.Balance(w,"player","spirit-stone")==funds+receipt.UnitPrice,"Incorrect sale settlement");
        }),
        ("R2 deferred trade completes once and preserves JSON SQLite continuation",()=>{
            var w=World();long stock=Inventory.Balance(w,"market","herb"),money=Inventory.Balance(w,"player","spirit-stone");var e=new SimulationEngine(w);e.Submit(Buy("trade"));e.AdvanceTo(0);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions.TryGetValue("trade",out var action)&&action.Phase==ActionPhase.Running,"Trade not running: "+string.Join(";",w.Events.Where(x=>x.ActionId=="trade").Select(x=>x.Detail)));
            Check(Inventory.Balance(w,"market","herb")==stock&&Inventory.Balance(w,"player","spirit-stone")==money,"Trade reserved before deadline");
            long due=w.Time.ActionRuntime.Actions["trade"].DueAt;string dir=Path.Combine(Path.GetTempPath(),"r2-trade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(path));a.AdvanceTo(due);b.AdvanceTo(due);var completed=b.Snapshot();Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(completed)&&completed.Time!.ActionRuntime!.Actions["trade"].Phase==ActionPhase.Completed&&completed.Trades.Count(t=>t.ActionId=="trade")==1,"Trade continuation failed");}}
            finally{foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
        }),
        ("R2 deferred trade fails without stock and does not refund AP",()=>{
            var e=new SimulationEngine(World());e.Submit(Buy("trade-fail"));e.AdvanceTo(0);var w=e.Snapshot();var action=w.Time!.ActionRuntime!.Actions["trade-fail"];
            long stock=Inventory.Balance(w,"market","herb");w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"empty-market","market","sect","herb",stock,"FixtureTransfer"));e=new SimulationEngine(w);e.AdvanceTo(action.DueAt);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[action.Id].Phase==ActionPhase.Failed&&!w.Trades.Any(t=>t.ActionId==action.Id)&&w.Time.Charges.Count(c=>c.ActionId==action.Id)==1,"Incorrect failed trade settlement");
        })
    };
}
