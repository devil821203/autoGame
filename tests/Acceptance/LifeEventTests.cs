using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class LifeEventTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static ActionIntent Craft(string id="precise-life",string actor="player")=>new(id,actor,actor,"ointment",1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.CraftLifeItem);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Life seconds work starts at minute seventeen and completes at minute forty seven exactly once",()=>{
            var e=new SimulationEngine(LifeEventScenario.Create());e.AdvanceTo(1020);var before=e.Snapshot();Check(ActionValidator.Validate(before,Craft()) is null,"Start rejected: "+ActionValidator.Validate(before,Craft()));long quantity=Inventory.Balance(before,"player","ointment");e.Submit(Craft());e.AdvanceTo(1020);
            Check(e.Snapshot().LifeSystem!.Work.Single().DueAtSeconds==2820,"Deadline rounded to grid");e.AdvanceTo(2819);Check(Inventory.Balance(e.Snapshot(),"player","ointment")==quantity,"Early output");
            e.AdvanceTo(2820);Check(Inventory.Balance(e.Snapshot(),"player","ointment")==quantity+1,"Missing output");e.AdvanceTo(3600);Check(Inventory.Balance(e.Snapshot(),"player","ointment")==quantity+1,"Duplicate output");
        }),
        ("Life seconds snapshot at important completion boundary preserves queued work and continuation",()=>{
            var e=new SimulationEngine(LifeEventScenario.Create());e.ScheduleWorldSignal(1800,"before-life","stop before same-time completion",true);e.Submit(Craft());e.AdvanceTo(1800);
            Check(e.LastAdvance!.Reason==AdvanceStopReason.ImportantEvent&&e.Snapshot().LifeSystem!.Work.Count==1,"Important stop skipped work");
            var replay=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));e.AdvanceTo(1800);replay.AdvanceTo(1800);
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(replay.Snapshot())&&e.Snapshot().LifeSystem!.Work.Count==0,"Boundary continuation diverged");
        }),
        ("Life seconds SQLite and JSON preserve off-grid deadline without recharging materials or AP",()=>{
            var e=new SimulationEngine(LifeEventScenario.Create());e.AdvanceTo(1020);e.Submit(Craft());e.AdvanceTo(1500);var w=e.Snapshot();Check(w.LifeSystem!.Work.Count==1,"No pending work saved");string dir=Path.Combine(Path.GetTempPath(),"life-event-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var loaded=store.Load(path);var a=new SimulationEngine(w);var b=new SimulationEngine(loaded);a.AdvanceTo(2820);b.AdvanceTo(2820);
                Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Save continuation changed");File.Delete(path);
            }}finally{foreach(string path in Directory.GetFiles(dir))File.Delete(path);Directory.Delete(dir);}
        }),
        ("Life seconds legacy mode remains gated while fresh reset retains explicit new mode",()=>{
            var old=new SimulationEngine(PlayerControlScenario.Create());old.AdvanceTo(1020);Check(ActionValidator.Validate(old.Snapshot(),Craft()) is not null,"Old world silently migrated");
            var fresh=NewGameFactory.CreateLike(LifeEventScenario.Create(),"重建");Check(fresh.Time!.IndependentLifeEvents,"New game lost mode");
        }),
        ("Life seconds interactive action stops at its own completion instead of next grid",()=>{
            var e=new SimulationEngine(LifeEventScenario.Create());e.AdvanceTo(1020);var result=e.ExecutePlayerActivity(Craft());Check(result.Accepted&&e.WorldTimeSeconds==2820,"Interactive completion rounded forward");
        }),
        ("Life seconds treatment and commissioned pharmacy use the same exact deadlines",()=>{
            var w=LifeEventScenario.Create();int n=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[n]=w.Characters[n] with{Health=50};var e=new SimulationEngine(w);e.AdvanceTo(1020);
            var heal=new ActionIntent("precise-medical","player","player","herb",1,0,DecisionSource.Player,ActionKind.MedicalTreatment);Check(ActionValidator.Validate(e.Snapshot(),heal) is null,"Treatment invalid");
            e.Submit(heal);e.AdvanceTo(1020);long due=e.Snapshot().LifeSystem!.Work.Single().DueAtSeconds!.Value;e.AdvanceTo(due-1);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Health==50,"Early treatment");e.AdvanceTo(due);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Health>50,"Missing treatment");
            w=LifeEventScenario.Create();var owner=w.Characters.Single(a=>a.Id=="player");n=w.Characters.FindIndex(a=>a.TestProfession==TestProfession.Pharmacist);var provider=w.Characters[n];
            w.Characters[n]=provider with{RegionId=owner.RegionId,DemoLocationId=owner.DemoLocationId,HexPosition=owner.HexPosition};
            // Establish finite demand rather than assuming the scenario starts without medicine.
            long initial=Inventory.Balance(w,"player","ointment");if(initial>0)w.Ledger.Add(new(w.NextSequence++,0,"life-demand","player","consumed","ointment",initial,"TestDemand"));
            e=new(w);e.AdvanceTo(1020);long money=Inventory.Balance(e.Snapshot(),provider.Id,"spirit-stone");
            long quote=PaidLifeServices.Quote(w,"ointment",ServiceMaterialSource.Client);var request=new ActionIntent("precise-service","player",provider.Id,"ointment",1,0,DecisionSource.Player,ActionKind.RequestLifeService,LimitPrice:quote,ServiceMaterials:ServiceMaterialSource.Client);
            Check(ActionValidator.Validate(e.Snapshot(),request) is null,"Service rejected: "+ActionValidator.Validate(e.Snapshot(),request));e.Submit(request);e.AdvanceTo(2819);Check(Inventory.Balance(e.Snapshot(),provider.Id,"spirit-stone")==money,"Service paid early");e.AdvanceTo(2820);
            Check(Inventory.Balance(e.Snapshot(),provider.Id,"spirit-stone")==money+quote&&Inventory.Balance(e.Snapshot(),"player","ointment")==1,"Service payout/output mismatch");
        }),
        ("R8 repairs started by NPCs inside one long advance join the active scheduler",()=>{
            var e=new SimulationEngine(EquipmentRepairScenario.Create());e.AdvanceTo(7200,true);Check(e.Snapshot().Time!.EquipmentRepair!.Receipts.Any(r=>r.OwnerId=="npc:03"&&r.Completed),"Long advance lost repair event");
        })
    };
}
