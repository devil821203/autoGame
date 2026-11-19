using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class ConstructionLifecycleTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static (WorldState,ActionIntent) Fixture(){
        var w=LifeEventScenario.Create();var def=w.HexMap!.BuildingDefinitions.First(d=>d.Kind==OrganizationKind.Sect);int n=w.HexMap.BuildingDefinitions.IndexOf(def);w.HexMap.BuildingDefinitions[n]=def with {Ticks=10};w.Time!.Costs[ActionKind.ConstructBuilding]=10;
        var tile=w.HexMap.Tiles.First(t=>t.LocalOrganizationId=="sect"&&HexNavigation.Passable(t)&&!w.HexMap.Buildings.Any(b=>b.Coord==t.Coord));
        return (w,new("r2-building","player",tile.Coord.ToString(),"spirit-stone",def.Cost,0,DecisionSource.Player,ActionKind.ConstructBuilding,MissionId:def.Id,OrganizationId:"sect"));
    }
    private static SimulationEngine Start(WorldState w,ActionIntent i){var e=new SimulationEngine(w);e.AdvanceTo(1020);e.Submit(i);e.AdvanceTo(1020);return e;}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 historical construction remains in old mode",()=>{
            var w=RegionalWorldScenario.Create();var def=w.HexMap!.BuildingDefinitions.First(d=>d.Kind==OrganizationKind.Sect);var tile=w.HexMap.Tiles.First(t=>t.LocalOrganizationId=="sect"&&HexNavigation.Passable(t)&&!w.HexMap.Buildings.Any(b=>b.Coord==t.Coord));
            var i=new ActionIntent("legacy-building","player",tile.Coord.ToString(),"spirit-stone",def.Cost,0,DecisionSource.Player,ActionKind.ConstructBuilding,MissionId:def.Id,OrganizationId:"sect");
            var e=new SimulationEngine(w);e.Submit(i);e.AdvanceTo(0);w=e.Snapshot();Check(!w.Time!.ActionRuntime!.Actions.ContainsKey(i.Id),"Historical construction mode changed");
            var task=w.Scheduled.Single(t=>t.Intent.Id==i.Id);e.AdvanceTo(task.DueTick*DiscreteTime.SecondsPerStep(w));Check(e.Snapshot().HexMap!.Buildings.Single(b=>b.Id==i.Id).Status==BuildingStatus.Complete,"Historical construction stopped");
        }),
        ("R2 construction suspension preserves funds and resumes",()=>{
            var (world,i)=Fixture();var e=Start(world,i);var w=e.Snapshot();long due=w.Scheduled.Single(t=>t.Intent.Id==i.Id).DueAtSeconds!.Value;
            int n=w.HexMap!.Tiles.FindIndex(t=>t.Coord==w.HexMap.Buildings.Single(b=>b.Id==i.Id).Coord);w.HexMap.Tiles[n]=w.HexMap.Tiles[n] with {LocalOrganizationId=null};
            e=new SimulationEngine(w);e.AdvanceTo(due);w=e.Snapshot();
            Check(w.HexMap!.Buildings.Single(b=>b.Id==i.Id).Status==BuildingStatus.Suspended&&w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Running&&w.Time.ActionRuntime.Reservations[i.Id+":building-funds"].Remaining==i.Quantity,"Suspension lost resources");
            w.HexMap.Tiles[n]=w.HexMap.Tiles[n] with {LocalOrganizationId="sect"};long next=w.Scheduled.Single(t=>t.Intent.Id==i.Id).DueAtSeconds!.Value;e=new SimulationEngine(w);e.AdvanceTo(next);
            Check(e.Snapshot().Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Completed,"Construction failed to resume");
        }),
        ("R2 construction JSON and SQLite continuation",()=>{
            var (world,i)=Fixture();var w=Start(world,i).Snapshot();long due=w.Scheduled.Single(t=>t.Intent.Id==i.Id).DueAtSeconds!.Value;
            string dir=Path.Combine(Path.GetTempPath(),"r2-building-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(path));a.AdvanceTo(due);b.AdvanceTo(due);Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot())&&b.Snapshot().Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Completed,"Construction continuation differs");}}
            finally{foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
        }),
        ("R2 construction reserves funds and completes once",()=>{
            var (world,i)=Fixture();var e=Start(world,i);var w=e.Snapshot();var a=w.Time!.ActionRuntime!.Actions[i.Id];var r=w.Time.ActionRuntime.Reservations[i.Id+":building-funds"];
            Check(a.Phase==ActionPhase.Running&&r.OwnerId=="sect"&&r.Remaining==i.Quantity,"Missing construction reservation");long due=w.Scheduled.Single(t=>t.Intent.Id==i.Id).DueAtSeconds!.Value;
            e.AdvanceTo(due-1);Check(e.Snapshot().HexMap!.Buildings.Single(b=>b.Id==i.Id).Status==BuildingStatus.Constructing,"Early completion");e.AdvanceTo(due);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Completed&&w.Time.ActionRuntime.Reservations[r.Id].Consumed==i.Quantity&&w.HexMap!.Buildings.Single(b=>b.Id==i.Id).Status==BuildingStatus.Complete&&w.Ledger.Count(x=>x.ActionId==i.Id&&x.Reason=="BuildingConsumed")==1,"Incorrect settlement");
        }),
        ("R2 construction cancellation refunds and invalidates completion",()=>{
            var (world,i)=Fixture();var e=Start(world,i);var w=e.Snapshot();long balance=Inventory.Balance(w,"sect","spirit-stone")+i.Quantity;long due=w.Scheduled.Single(t=>t.Intent.Id==i.Id).DueAtSeconds!.Value;
            int n=w.Time!.Budgets.FindIndex(b=>b.ActorId=="player");w.Time.Budgets[n]=w.Time.Budgets[n] with {BusyThroughTick=w.Clock.Tick};e=new SimulationEngine(w);
            e.Submit(new("r2-cancel","player",i.Id,"spirit-stone",1,0,DecisionSource.Player,ActionKind.CancelBuilding,OrganizationId:"sect"));e.AdvanceTo(due);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Cancelled&&w.Time.ActionRuntime.Reservations[i.Id+":building-funds"].Released==i.Quantity&&Inventory.Balance(w,"sect","spirit-stone")==balance&&w.Ledger.Count(x=>x.ActionId==i.Id&&x.Reason=="BuildingRefund")==1&&!w.Ledger.Any(x=>x.ActionId==i.Id&&x.Reason=="BuildingConsumed"),"Refund or cancellation failed");
        }),
        ("R2 construction rejects missing reservation",()=>{
            var (world,i)=Fixture();var w=Start(world,i).Snapshot();w.Time!.ActionRuntime!.Reservations.Remove(i.Id+":building-funds");bool rejected=false;try{WorldIntegrity.Validate(w);}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid building reservation accepted");
        })
    };
}
