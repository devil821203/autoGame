using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class GatherLifecycleTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static WorldState World()
    {
        var w=LifeEventScenario.Create();var region=w.HexMap!.Regional!.Regions[1];
        for(int n=0;n<w.Characters.Count;n++)
            if(w.Characters[n].Id is "player" or "npc:03")
                w.Characters[n]=w.Characters[n] with {RegionId=region.Id,HexPosition=region.Resource,DemoLocationId="grove"};
        return w;
    }
    private static ActionIntent Gather(string id,string actor)=>new(id,actor,"grove","herb",1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.Gather);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 gathering reserves local stock and completes exactly once",()=>{
            var w=World();var owner=w.HexMap!.Regional!.Regions[1].ResourceOwner;long stock=Inventory.Balance(w,owner,"herb"),before=Inventory.Balance(w,"player","herb");
            var e=new SimulationEngine(w);e.AdvanceTo(1020);e.Submit(Gather("r2-gather","player"));e.AdvanceTo(1020);w=e.Snapshot();
            var action=w.Time!.ActionRuntime!.Actions["r2-gather"];var reservation=w.Time.ActionRuntime.Reservations["r2-gather:gather-resource"];
            Check(action.Phase==ActionPhase.Running&&reservation.OwnerId==owner&&reservation.Remaining==1&&Inventory.Balance(w,owner,"herb")==stock-1,"Incorrect reservation");
            long due=w.Scheduled.Single(t=>t.Intent.Id==action.Id).DueAtSeconds!.Value;var restored=new SimulationEngine(WorldCodec.Clone(w));e.AdvanceTo(due);restored.AdvanceTo(due);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[action.Id].Phase==ActionPhase.Completed&&w.Time.ActionRuntime.Reservations[reservation.Id].Consumed==1&&Inventory.Balance(w,"player","herb")==before+1,"Settlement failed");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(restored.Snapshot()),"Continuation diverged");e.Submit(Gather("r2-gather","player"));e.AdvanceTo(due);Check(Inventory.Balance(e.Snapshot(),"player","herb")==before+1,"Duplicate settlement");
        }),
        ("R2 player and NPC gathering reserve distinct stock",()=>{
            var w=World();var owner=w.HexMap!.Regional!.Regions[1].ResourceOwner;long stock=Inventory.Balance(w,owner,"herb");var e=new SimulationEngine(w);
            e.Submit(Gather("player-gather","player"));e.Submit(Gather("npc-gather","npc:03"));e.AdvanceTo(0);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions.ContainsKey("player-gather")&&w.Time.ActionRuntime.Actions.ContainsKey("npc-gather")&&Inventory.Balance(w,owner,"herb")==stock-2,"Concurrent reservation failed");
            long due=w.Scheduled.Where(t=>t.Intent.Id is "player-gather" or "npc-gather").Max(t=>t.DueAtSeconds!.Value);e.AdvanceTo(due);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions["player-gather"].Phase==ActionPhase.Completed&&w.Time.ActionRuntime.Actions["npc-gather"].Phase==ActionPhase.Completed,"Concurrent completion failed");
        }),
        ("R2 last herb cannot be reserved twice",()=>{
            var w=World();var owner=w.HexMap!.Regional!.Regions[1].ResourceOwner;long stock=Inventory.Balance(w,owner,"herb");Check(stock>1,"Missing fixture stock");
            w.Ledger.Add(new(w.NextSequence++,0,"fixture",owner,"sect","herb",stock-1,"FixtureTransfer"));var e=new SimulationEngine(w);
            e.Submit(Gather("first","player"));e.Submit(Gather("second","npc:03"));e.AdvanceTo(0);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions.ContainsKey("first")&&!w.Time.ActionRuntime.Actions.ContainsKey("second")&&Inventory.Balance(w,owner,"herb")==0&&!w.Time.Charges.Any(c=>c.ActionId=="second"),"Over-reservation or rejected AP charge");
        }),
        ("R2 gathering rejects missing reservation",()=>{
            var e=new SimulationEngine(World());e.Submit(Gather("forged","player"));e.AdvanceTo(0);var w=e.Snapshot();w.Time!.ActionRuntime!.Reservations.Remove("forged:gather-resource");
            bool rejected=false;try{WorldIntegrity.Validate(w);}catch(InvalidDataException){rejected=true;}Check(rejected,"Orphan escrow accepted");
        }),
        ("R2 gathering resumes once through JSON and SQLite",()=>{
            var e=new SimulationEngine(World());e.AdvanceTo(1020);e.Submit(Gather("persist","player"));e.AdvanceTo(1020);var w=e.Snapshot();long due=w.Scheduled.Single(t=>t.Intent.Id=="persist").DueAtSeconds!.Value;
            string dir=Path.Combine(Path.GetTempPath(),"r2-gather-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var original=new SimulationEngine(w);var loaded=new SimulationEngine(store.Load(path));original.AdvanceTo(due);loaded.AdvanceTo(due);var result=loaded.Snapshot();
                Check(WorldCodec.Digest(original.Snapshot())==WorldCodec.Digest(result)&&result.Time!.ActionRuntime!.Actions["persist"].Phase==ActionPhase.Completed&&result.Ledger.Count(x=>x.ActionId=="persist"&&x.Reason=="Gathered")==1,"Store continuation diverged");
            }}finally{foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
        }),
        ("R2 gathering preserves historical mode",()=>{
            var w=RegionalWorldScenario.Create();var region=w.HexMap!.Regional!.Regions[1];int n=w.Characters.FindIndex(c=>c.Id=="player");w.Characters[n]=w.Characters[n] with {RegionId=region.Id,HexPosition=region.Resource,DemoLocationId="grove"};long before=Inventory.Balance(w,"player","herb");
            var e=new SimulationEngine(w);e.Submit(Gather("legacy-gather","player"));e.AdvanceTo(0);w=e.Snapshot();Check(!w.Time!.ActionRuntime!.Actions.ContainsKey("legacy-gather"),"Historical mode changed");
            var task=w.Scheduled.Single(t=>t.Intent.Id=="legacy-gather");e.AdvanceTo(checked(task.DueTick*DiscreteTime.SecondsPerStep(w)));Check(Inventory.Balance(e.Snapshot(),"player","herb")==before+1,"Historical gather failed");
        })
    };
}
