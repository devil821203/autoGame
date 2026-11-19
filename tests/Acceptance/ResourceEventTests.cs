using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class ResourceEventTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static WorldState World(long? capacity=null)
    {
        var w=LifeEventScenario.Create();
        // Short test interval; production still reads the configured, already day-scaled snapshot.
        w.HerbRegeneration=w.HerbRegeneration! with{IntervalTicks=3,Capacity=capacity??checked(Inventory.Balance(w,"grove","herb")+w.HerbRegeneration!.Amount*4)};
        var q=new EventScheduler(w.Time!.DiscreteEvents!);q.Cancel("$resource-regeneration");
        w.Time.ResourceRegenerationDueSeconds=1800;
        q.Schedule(1800,SimulationEventKind.ResourceRegeneration,"$resource-regeneration");
        return w;
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Resource seconds preserve configured day conversion",()=>{
            var w=LifeEventScenario.Create();Check(w.Time!.ResourceRegenerationDueSeconds==w.HerbRegeneration!.IntervalTicks*DiscreteTime.SecondsPerStep(w),"Wrong unit conversion");
            Check(w.Time.DiscreteEvents!.Pending.Count(e=>e.Kind==SimulationEventKind.ResourceRegeneration)==1,"Missing initial event");
        }),
        ("Resource seconds produce only at exact deadlines without compatibility duplicates",()=>{
            var w=World();long before=Inventory.Balance(w,"grove","herb");var e=new SimulationEngine(w);
            e.AdvanceTo(1799);Check(Inventory.Balance(e.Snapshot(),"grove","herb")==before,"Early production");
            e.AdvanceTo(1800);Check(Inventory.Balance(e.Snapshot(),"grove","herb")==before+w.HerbRegeneration!.Amount,"Missing or duplicate first yield");
            e.AdvanceTo(5400);w=e.Snapshot();Check(w.Events.Count(x=>x.Kind=="ResourceRegenerationResolved")==3&&w.Ledger.Count(x=>x.From=="$regeneration")==3,"Wrong pulse count");
            Check(w.Time!.ResourceRegenerationDueSeconds==7200,"Period drift");
        }),
        ("Resource seconds cap partial output and still schedule full-capacity periods",()=>{
            long stock=Inventory.Balance(LifeEventScenario.Create(),"grove","herb");var e=new SimulationEngine(World(stock+1));e.AdvanceTo(5400);var w=e.Snapshot();
            Check(Inventory.Balance(w,"grove","herb")==stock+1&&w.Ledger.Count(x=>x.From=="$regeneration")==1&&w.Ledger.Single(x=>x.From=="$regeneration").Quantity==1,"Capacity exceeded");
            Check(w.Events.Count(x=>x.Kind=="ResourceRegenerationResolved")==3&&w.Time!.ResourceRegenerationDueSeconds==7200,"Full-capacity period lost");
        }),
        ("Resource seconds same-boundary interruption resumes without double issuance",()=>{
            var e=new SimulationEngine(World());e.ScheduleWorldSignal(1800,"stop-resource","boundary",blocking:true);
            var stop=e.AdvanceTo(6000);Check(stop.Reason==AdvanceStopReason.ImportantEvent,"Did not stop");var w=e.Snapshot();WorldIntegrity.Validate(w);
            Check(w.Ledger.Count(x=>x.From=="$regeneration")==1,"Resource event not before signal");
            var resumed=new SimulationEngine(WorldCodec.Clone(w));resumed.AdvanceTo(5400);Check(resumed.Snapshot().Ledger.Count(x=>x.From=="$regeneration")==3,"Resume duplicated yield");
        }),
        ("Resource seconds latest JSON and SQLite retain next period",()=>{
            var e=new SimulationEngine(World());e.AdvanceTo(2000);var w=e.Snapshot();
            string dir=Path.Combine(Path.GetTempPath(),"resource-events-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(path));
                a.AdvanceTo(5400);b.AdvanceTo(5400);Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Changed continuation");
            }}finally{foreach(string p in Directory.GetFiles(dir))File.Delete(p);Directory.Delete(dir);}
        }),
        ("Resource seconds reject lost or duplicate active period",()=>{
            foreach(bool duplicate in new[]{false,true}){
                var w=World();if(duplicate)new EventScheduler(w.Time!.DiscreteEvents!).Schedule(1800,SimulationEventKind.ResourceRegeneration,"$resource-regeneration");
                else w.Time!.DiscreteEvents!.Pending.RemoveAll(x=>x.Kind==SimulationEventKind.ResourceRegeneration);
                bool rejected=false;try{WorldIntegrity.Validate(w);}catch(InvalidDataException){rejected=true;}Check(rejected,"Corrupt period accepted");
            }
        })
    };
}
