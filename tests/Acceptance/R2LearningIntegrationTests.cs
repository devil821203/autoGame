using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class R2LearningIntegrationTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("R2 method learning reserves funds and completes once across save", () => {
            var w=LifeEventScenario.Create();
            if(w.Advancement is null) throw new Exception("Missing advancement rules");
            int n=w.Characters.FindIndex(c=>c.Id=="player");
            w.Characters[n]=w.Characters[n] with {MethodId=null,Health=100,Realm=0,MethodExperience=0};
            long initial=Inventory.Balance(w,"player","spirit-stone");
            var intent=new ActionIntent("r2-learning","player","player","spirit-stone",1,0,DecisionSource.Player,ActionKind.LearnMethod);
            var e=new SimulationEngine(w);e.Submit(intent);e.AdvanceTo(0);w=e.Snapshot();
            if(!w.Time!.ActionRuntime!.Actions.TryGetValue(intent.Id,out var a)) throw new Exception("Learning was not started: "+string.Join(";",w.Events.Where(x=>x.ActionId==intent.Id).Select(x=>x.Detail)));
            var r=w.Time.ActionRuntime.Reservations[intent.Id+":advancement-resource"];
            if(a.Phase!=ActionPhase.Running||r.Remaining!=w.Advancement!.LearningCost||Inventory.Balance(w,"player","spirit-stone")!=initial-r.Amount)throw new Exception("Learning reservation wrong");
            string file=Path.Combine(Path.GetTempPath(),"r2-learning-"+Guid.NewGuid()+".json");
            try {
                var store=new JsonWorldStore();store.Save(file,w);var resumed=new SimulationEngine(store.Load(file));
                e.AdvanceTo(a.DueAt);resumed.AdvanceTo(a.DueAt);w=e.Snapshot();
                if(WorldCodec.Digest(w)!=WorldCodec.Digest(resumed.Snapshot()))throw new Exception("Save continuation differs");
                if(w.Time!.ActionRuntime!.Actions[intent.Id].Phase!=ActionPhase.Completed||w.Time.ActionRuntime.Reservations[r.Id].Consumed!=r.Amount||w.Characters.Single(c=>c.Id=="player").MethodId!=w.Advancement!.MethodId)throw new Exception("Learning settlement wrong");
                e.Submit(intent);e.AdvanceTo(a.DueAt);if(e.Snapshot().Time!.ActionRuntime!.Reservations[r.Id].Consumed!=r.Amount)throw new Exception("Duplicate settlement");
            } finally {File.Delete(file);}
        })
    };
}
