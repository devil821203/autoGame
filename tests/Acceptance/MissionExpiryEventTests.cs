using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class MissionExpiryEventTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static SimulationEngine Engine(bool duties=false)
    {
        var w=LifeEventScenario.Create();if(!duties)w.SectDuty=null;
        else w.SectDuty=w.SectDuty! with{MissionIntervalTicks=8};
        WorldPeriodFixture.Reset(w);
        int n=w.Characters.FindIndex(c=>c.Id=="player");var a=w.Characters[n];
        w.Characters[n]=a with{DemoLocationId="sect",HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect")};
        return new(w);
    }
    private static void Publish(SimulationEngine e,long quantity=3)
    {
        var result=e.ExecutePlayerActivity(new("expiry-publish","player","sect","herb",quantity,0,DecisionSource.Player,ActionKind.PublishMission,
            Offer:new("expiry-test",quantity,4,8,quantity*4)));
        Check(result.Accepted,result.ToString());
    }
    private static void Do(SimulationEngine e,ActionKind kind,string id,long quantity=1)
    {
        var result=e.ExecutePlayerActivity(new(id,"player","sect","herb",quantity,0,DecisionSource.Player,kind,MissionId:"expiry-test"));Check(result.Accepted,result.ToString());
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Mission expiry seconds release escrow only at deadline and only once",()=>{
            var e=Engine();Publish(e);e.AdvanceTo(4799);Check(e.Snapshot().Missions.Single(m=>m.Id=="expiry-test").Status==MissionStatus.Active,"Early expiry");
            e.AdvanceTo(4800);var w=e.Snapshot();Check(w.Missions.Single(m=>m.Id=="expiry-test").Status==MissionStatus.Expired&&Inventory.Balance(w,"$mission:expiry-test","spirit-stone")==0,"No expiry/refund");
            Check(w.Ledger.Single(x=>x.ActionId=="expire:expiry-test").Quantity==12,"Wrong refund");e.AdvanceTo(6000);Check(e.Snapshot().Ledger.Count(x=>x.ActionId=="expire:expiry-test")==1,"Duplicate refund");
        }),
        ("Mission expiry seconds partial delivery refunds only unpaid remainder",()=>{
            var e=Engine();Publish(e);Do(e,ActionKind.AcceptMission,"accept-part");Do(e,ActionKind.DeliverMission,"deliver-part");e.AdvanceTo(4800);var w=e.Snapshot();
            Check(w.Ledger.Single(x=>x.ActionId=="expire:expiry-test").Quantity==8&&w.Deliveries.Single(x=>x.MissionId=="expiry-test").Payment==4,"Partial refund changed");
        }),
        ("Mission expiry seconds completed and cancelled tasks invalidate expiry events",()=>{
            foreach(bool cancel in new[]{false,true}){
                var e=Engine();Publish(e,1);if(cancel)Do(e,ActionKind.CancelMission,"cancel-expiry");else{Do(e,ActionKind.AcceptMission,"accept-all");Do(e,ActionKind.DeliverMission,"deliver-all");}
                e.AdvanceTo(6000);var w=e.Snapshot();Check(!w.Events.Any(x=>x.Kind=="MissionExpired"&&x.Detail=="expiry-test")&&!w.Ledger.Any(x=>x.ActionId=="expire:expiry-test"),"Closed mission expired again");
            }
        }),
        ("Mission expiry seconds reject delivery completing at exact deadline",()=>{
            var e=Engine();Publish(e);Do(e,ActionKind.AcceptMission,"accept-late");e.AdvanceTo(4200);
            e.Submit(new("late-delivery","player","sect","herb",1,0,DecisionSource.Player,ActionKind.DeliverMission,MissionId:"expiry-test"));e.AdvanceTo(4800);var w=e.Snapshot();
            Check(!w.Deliveries.Any(x=>x.ActionId=="late-delivery")&&w.Events.Any(x=>x.ActionId=="late-delivery"&&x.Kind=="TimedCompletionRejected"),"Deadline delivery succeeded");
        }),
        ("Mission expiry seconds precede same-time automatic publication even if bridge queued first",()=>{
            var e=Engine(true);Publish(e);e.AdvanceTo(4200);var w=e.Snapshot();var q=new EventScheduler(w.Time!.DiscreteEvents!);q.Cancel("$mission-expiry:expiry-test");
            q.Schedule(4800,SimulationEventKind.MissionExpiry,"$mission-expiry:expiry-test",detail:"expiry-test");e=new(w);e.AdvanceTo(4800);w=e.Snapshot();
            long expired=w.Events.Single(x=>x.Kind=="MissionExpired"&&x.Detail=="expiry-test").Sequence;
            Check(w.Events.Any(x=>x.Kind=="AutoMissionPublished"&&x.Detail=="herb"&&x.Sequence>expired),"Publication missed refund/expiry");
        }),
        ("Mission expiry seconds boundary stop and latest JSON SQLite continue exactly once",()=>{
            var e=Engine();e.ScheduleWorldSignal(4800,"stop-before-expiry","stop",true);Publish(e);Check(e.AdvanceTo(6000).Reason==AdvanceStopReason.ImportantEvent,"No stop");var w=e.Snapshot();
            string dir=Path.Combine(Path.GetTempPath(),"mission-expiry-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                string p=Path.Combine(dir,store.GetType().Name);store.Save(p,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(p));a.AdvanceTo(6000);b.AdvanceTo(6000);
                Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot())&&b.Snapshot().Ledger.Count(x=>x.ActionId=="expire:expiry-test")==1,"Expiry replay mismatch");
            }}finally{foreach(string p in Directory.GetFiles(dir))File.Delete(p);Directory.Delete(dir);}
        }),
        ("Mission expiry seconds reject lost and duplicate active deadline events",()=>{
            foreach(bool duplicate in new[]{false,true}){var e=Engine();Publish(e);var w=e.Snapshot();if(duplicate)new EventScheduler(w.Time!.DiscreteEvents!).Schedule(4800,SimulationEventKind.MissionExpiry,"$mission-expiry:expiry-test",detail:"expiry-test");
                else w.Time!.DiscreteEvents!.Pending.RemoveAll(x=>x.Kind==SimulationEventKind.MissionExpiry);
                bool rejected=false;try{WorldIntegrity.Validate(w);}catch(InvalidDataException){rejected=true;}Check(rejected,"Bad mission expiry event accepted");}
        })
    };
}
