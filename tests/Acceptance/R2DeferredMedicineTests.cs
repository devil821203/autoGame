using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class R2DeferredMedicineTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static WorldState World(){var w=LifeEventScenario.Create();int n=w.Characters.FindIndex(c=>c.Id=="player");w.Characters[n]=w.Characters[n] with {Health=50};w.Ledger.Add(new(w.NextSequence++,0,"medicine-test-stock","$genesis","player","ointment",2,"Genesis"));return w;}
    private static ActionIntent Use(string id)=>new(id,"player","player","ointment",1,0,DecisionSource.Player,ActionKind.UseMedicine);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 delayed medicine consumes only on completion and survives stores",()=>{
            var w=World();long quantity=Inventory.Balance(w,"player","ointment");var e=new SimulationEngine(w);e.Submit(Use("medicine"));e.AdvanceTo(0);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions.TryGetValue("medicine",out var action)&&action.Phase==ActionPhase.Running&&Inventory.Balance(w,"player","ointment")==quantity&&w.Characters.Single(c=>c.Id=="player").Health==50,"Medicine applied early or rejected");
            long due=w.Time.ActionRuntime.Actions["medicine"].DueAt;string dir=Path.Combine(Path.GetTempPath(),"r2-medicine-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var original=new SimulationEngine(w);var restored=new SimulationEngine(store.Load(path));original.AdvanceTo(due);restored.AdvanceTo(due);var done=restored.Snapshot();Check(WorldCodec.Digest(original.Snapshot())==WorldCodec.Digest(done)&&done.Time!.ActionRuntime!.Actions["medicine"].Phase==ActionPhase.Completed&&Inventory.Balance(done,"player","ointment")==quantity-1&&done.Ledger.Count(l=>l.ActionId=="medicine"&&l.Reason=="MedicineConsumed")==1,"Medicine continuation incorrect");}}
            finally{foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
        }),
        ("R2 delayed medicine fails without item and retains AP",()=>{
            var e=new SimulationEngine(World());e.Submit(Use("medicine-fail"));e.AdvanceTo(0);var w=e.Snapshot();var action=w.Time!.ActionRuntime!.Actions["medicine-fail"];
            long stock=Inventory.Balance(w,"player","ointment");w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"consume-fixture","player","sect","ointment",stock,"FixtureTransfer"));e=new SimulationEngine(w);e.AdvanceTo(action.DueAt);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[action.Id].Phase==ActionPhase.Failed&&!w.Ledger.Any(l=>l.ActionId==action.Id&&l.Reason=="MedicineConsumed")&&w.Time.Charges.Count(c=>c.ActionId==action.Id)==1,"Medicine failure changed resources or AP");
        })
    };
}
