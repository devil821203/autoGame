using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
internal static class R2MissionCommandsTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static SimulationEngine Engine(){var w=LifeEventScenario.Create();w.SectDuty=null;WorldPeriodFixture.Reset(w);int n=w.Characters.FindIndex(c=>c.Id=="player");var a=w.Characters[n];w.Characters[n]=a with {DemoLocationId="sect",HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect")};return new(w);}
    private static ActionIntent Publish(string id="publish")=>new(id,"player","sect","herb",3,0,DecisionSource.Player,ActionKind.PublishMission,Offer:new("mission",3,4,8,12));
    private static ActionIntent Command(string id,ActionKind kind)=>new(id,"player","sect","herb",1,0,DecisionSource.Player,kind,MissionId:"mission");
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 publish delays reservation and resumes through both stores",()=>{
            var e=Engine();long before=Inventory.Balance(e.Snapshot(),"sect","spirit-stone");var i=Publish();e.Submit(i);e.AdvanceTo(0);var w=e.Snapshot();var a=w.Time!.ActionRuntime!.Actions[i.Id];Check(a.Phase==ActionPhase.Running&&Inventory.Balance(w,"sect","spirit-stone")==before&&!w.Missions.Any(m=>m.Id=="mission"),"Publish settled early");
            string dir=Path.Combine(Path.GetTempPath(),"r2-mission-command-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var x=new SimulationEngine(w);var y=new SimulationEngine(store.Load(path));x.AdvanceTo(a.DueAt);y.AdvanceTo(a.DueAt);var done=y.Snapshot();Check(WorldCodec.Digest(x.Snapshot())==WorldCodec.Digest(done)&&done.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Completed&&Inventory.Balance(done,"$mission:mission","spirit-stone")==12&&done.Ledger.Count(l=>l.ActionId==i.Id&&l.Reason=="MissionReserved")==1,"Publish replay failed");}}finally{foreach(string path in Directory.GetFiles(dir))File.Delete(path);Directory.Delete(dir);}
        }),
        ("R2 acceptance and cancellation retain existing refund rules",()=>{
            var e=Engine();Check(e.ExecutePlayerActivity(Publish()).Accepted,"Publish failed");Check(e.ExecutePlayerActivity(Command("accept",ActionKind.AcceptMission)).Accepted,"Accept failed");var w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions["accept"].Phase==ActionPhase.Completed&&w.Missions.Single(m=>m.Id=="mission").Participants.Contains("player"),"Accept not recorded");
            Check(e.ExecutePlayerActivity(Command("cancel",ActionKind.CancelMission)).Accepted,"Cancel failed");w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions["cancel"].Phase==ActionPhase.Completed&&w.Missions.Single(m=>m.Id=="mission").Status==MissionStatus.Cancelled&&Inventory.Balance(w,"$mission:mission","spirit-stone")==0&&w.Ledger.Count(l=>l.ActionId=="cancel"&&l.Reason=="MissionRefund")==1,"Cancellation refund failed");
        }),
        ("R2 acceptance expires at deadline without refunding AP",()=>{
            var e=Engine();Check(e.ExecutePlayerActivity(Publish()).Accepted,"Publish failed");e.AdvanceTo(4200);var i=Command("late-accept",ActionKind.AcceptMission);e.Submit(i);e.AdvanceTo(4200);var w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Running,"Acceptance did not start");e.AdvanceTo(4800);w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Failed&&!w.Missions.Single(m=>m.Id=="mission").Participants.Contains("player")&&w.Time.Charges.Count(c=>c.ActionId==i.Id)==1,"Expired acceptance changed mission or AP");
        })
    };
}
