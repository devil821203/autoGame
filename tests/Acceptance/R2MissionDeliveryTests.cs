using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class R2MissionDeliveryTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static SimulationEngine Ready(){
        var w=LifeEventScenario.Create();w.SectDuty=null;WorldPeriodFixture.Reset(w);int n=w.Characters.FindIndex(c=>c.Id=="player");var a=w.Characters[n];
        w.Characters[n]=a with {DemoLocationId="sect",HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect")};
        w.Ledger.Add(new(w.NextSequence++,0,"delivery-fixture","$genesis","player","herb",5,"Genesis"));
        var e=new SimulationEngine(w);var publish=new ActionIntent("r2-publish","player","sect","herb",3,0,DecisionSource.Player,ActionKind.PublishMission,Offer:new("r2-mission",3,4,8,12));
        Check(e.ExecutePlayerActivity(publish).Accepted,"Publish rejected");var accept=new ActionIntent("r2-accept","player","sect","herb",1,0,DecisionSource.Player,ActionKind.AcceptMission,MissionId:"r2-mission");Check(e.ExecutePlayerActivity(accept).Accepted,"Accept rejected");return e;
    }
    private static ActionIntent Deliver(string id)=>new(id,"player","sect","herb",1,0,DecisionSource.Player,ActionKind.DeliverMission,MissionId:"r2-mission");
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 mission delivery pays once after delay and survives JSON SQLite",()=>{
            var e=Ready();var i=Deliver("r2-deliver");long now=e.WorldTimeSeconds!.Value;var before=e.Snapshot();long herbs=Inventory.Balance(before,"player","herb"),funds=Inventory.Balance(before,"player","spirit-stone");e.Submit(i);e.AdvanceTo(now);var w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions.TryGetValue(i.Id,out var a)&&a.Phase==ActionPhase.Running&&Inventory.Balance(w,"player","herb")==herbs&&Inventory.Balance(w,"player","spirit-stone")==funds,"Delivery charged early or rejected");
            long due=w.Time.ActionRuntime.Actions[i.Id].DueAt;string dir=Path.Combine(Path.GetTempPath(),"r2-delivery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var x=new SimulationEngine(w);var y=new SimulationEngine(store.Load(path));x.AdvanceTo(due);y.AdvanceTo(due);var done=y.Snapshot();Check(WorldCodec.Digest(x.Snapshot())==WorldCodec.Digest(done)&&done.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Completed&&done.Deliveries.Count(d=>d.ActionId==i.Id)==1&&Inventory.Balance(done,"player","herb")==herbs-1&&Inventory.Balance(done,"player","spirit-stone")==funds+4,"Delivery settlement differs");}}
            finally{foreach(string path in Directory.GetFiles(dir))File.Delete(path);Directory.Delete(dir);}
        }),
        ("R2 mission delivery fails at expiry and preserves paid AP",()=>{
            var e=Ready();e.AdvanceTo(4200);var i=Deliver("r2-expired-deliver");e.Submit(i);e.AdvanceTo(4200);var w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Running,"Delivery not started");e.AdvanceTo(4800);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Failed&&!w.Deliveries.Any(d=>d.ActionId==i.Id)&&w.Time.Charges.Count(c=>c.ActionId==i.Id)==1,"Expiry did not fail safely");
        })
    };
}
