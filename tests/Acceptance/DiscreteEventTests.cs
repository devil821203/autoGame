using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
using Cultivation.AI;

internal static class DiscreteEventTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static DiscreteEventState Fresh() => new() { Rules = GameConfiguration.Find().Read<EventQueueRules>("time/discrete-events.json") };
    private static SimulationEngine Engine()
    {
        var w = TestScenarioCatalog.Create("methods"); DiscreteTime.EnableFresh(w); return new(w);
    }
    private static ActionIntent Train(string id, long count = 1) => new(id, "player", "player", "method:QingyunFist", count, 0,
        DecisionSource.Player, ActionKind.TrainMortalAttribute);
    private sealed class CountingPlanner(bool fail = false) : IActionPlanner
    {
        public int Calls;
        public IReadOnlyList<ScoredAction> Evaluate(LocalObservation observation)
        { Calls++; if (fail && Calls == 2) throw new InvalidOperationException("Injected decision failure"); return []; }
    }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("DES exact 17 and 30 minute events resolve at their due seconds in stable order", () => {
            var s = Fresh(); var q = new EventScheduler(s); var seen = new List<string>();
            q.Schedule(1800, SimulationEventKind.Signal, "a"); q.Schedule(1020, SimulationEventKind.Signal, "b");
            q.Schedule(1020, SimulationEventKind.Signal, "c");
            q.AdvanceTo(1020, e => seen.Add(e.CorrelationId+":"+q.WorldTime));
            Check(seen.SequenceEqual(new[]{"b:1020","c:1020"}) && s.Pending.Count == 1, "Due time/order wrong");
            q.AdvanceTo(1800, e => seen.Add(e.CorrelationId+":"+q.WorldTime));
            q.AdvanceTo(5000, _ => throw new Exception("Completed twice"));
            Check(seen.Last() == "a:1800" && q.WorldTime == 5000, "Independent finish or empty jump wrong");
        }),
        ("DES cancellation invalidates old versions while a reused correlation can run once", () => {
            var s=Fresh(); var q=new EventScheduler(s); q.Schedule(1800,SimulationEventKind.Signal,"a");q.Cancel("a");
            q.Schedule(1800,SimulationEventKind.Signal,"a");int count=0;
            var r=q.AdvanceTo(1800,_=>count++); Check(count==1&&r.Discarded==1,"Cancellation/reuse wrong");
        }),
        ("DES important event stops exactly and same time unprocessed events survive save continuation", () => {
            var s=Fresh();var q=new EventScheduler(s);q.Schedule(10,SimulationEventKind.Signal,"before");
            q.Schedule(17,SimulationEventKind.Signal,"stop",blocking:true);q.Schedule(17,SimulationEventKind.Signal,"after");
            var seen=new List<string>();var r=q.AdvanceTo(30,e=>seen.Add(e.CorrelationId));
            Check(r.WorldTime==17&&r.Reason==AdvanceStopReason.ImportantEvent&&seen.SequenceEqual(new[]{"before","stop"}),"Block wrong");
            var copy=JsonSerializer.Deserialize<DiscreteEventState>(JsonSerializer.Serialize(s))!;
            new EventScheduler(copy).AdvanceTo(30,e=>seen.Add(e.CorrelationId));Check(seen.Last()=="after","Resume lost event");
        }),
        ("DES same time chains and per advance budgets stop without skipping queued work", () => {
            var s=Fresh();s.Rules=s.Rules with {MaximumEventsAtSameTime=3};var q=new EventScheduler(s);
            q.Schedule(0,SimulationEventKind.Signal,"loop");int n=0;
            var r=q.AdvanceTo(100,_=>{n++;q.Schedule(0,SimulationEventKind.Signal,"loop");});
            Check(n==3&&r.Reason==AdvanceStopReason.SameTimeBudget&&r.WorldTime==0&&s.Pending.Count==1,"Chain not bounded");
            s=Fresh();s.Rules=s.Rules with {MaximumEventsPerAdvance=2};q=new(s);
            for(int j=1;j<=3;j++)q.Schedule(j,SimulationEventKind.Signal,"x"+j);
            r=q.AdvanceTo(9,_=>{});Check(r.Reason==AdvanceStopReason.EventBudget&&r.WorldTime==2&&s.Pending.Single().DueAt==3,"Budget skipped");
        }),
        ("DES rejects malformed state backward scheduling and sequence overflow without mutation", () => {
            var s=Fresh();var q=new EventScheduler(s);q.AdvanceTo(20,_=>{});
            try{q.Schedule(19,SimulationEventKind.Signal,"x");throw new Exception("Accepted past");}catch(ArgumentException){}
            s.NextSequence=long.MaxValue;
            try{q.Schedule(21,SimulationEventKind.Signal,"x");throw new Exception("Accepted overflow");}catch(OverflowException){}
            Check(s.Pending.Count==0,"Failed schedule mutated");
            s.Rules=s.Rules with {TimeUnit="Minutes"};try{new EventScheduler(s);throw new Exception("Accepted unit");}catch(InvalidDataException){}
        }),
        ("DES real method batch retains AP and segment rewards across atomic JSON save continuation", () => {
            var e=Engine();e.Submit(Train("batch",6));e.AdvanceTo(600);
            var w=e.Snapshot();Check(w.Methods!.Learners["player"]["QingyunFist"]==1&&w.Time!.Budgets.Single(b=>b.ActorId=="player").Remaining==24,"First segment wrong");
            string path=Path.Combine(Path.GetTempPath(),"des-"+Guid.NewGuid().ToString("N")+".json");
            try{
                var store=new JsonWorldStore();store.Save(path,w);
                Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("SchemaVersion").GetInt32()==4,"Missing old-engine format guard");
                var other=new SimulationEngine(store.Load(path));
                e.AdvanceTo(3600);other.AdvanceTo(3600);
                Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(other.Snapshot()),"Save continuation differs");
                w=e.Snapshot();Check(w.MortalTrainingWork.Count==0&&w.Methods!.Learners["player"]["QingyunFist"]==6&&w.Characters.Single(a=>a.Id=="player").Stamina!.Current==94,"Batch double/under settled");
            }finally{File.Delete(path);}
        }),
        ("DES training starting between legacy boundaries lasts full 600 seconds", () => {
            var e=Engine();e.AdvanceTo(1020);e.Submit(Train("offset"));e.AdvanceTo(1619);
            Check(e.Snapshot().Methods!.Learners["player"]["QingyunFist"]==0,"Completed early");
            e.AdvanceTo(1620);Check(e.Snapshot().Methods!.Learners["player"]["QingyunFist"]==1,"Completion not exact");
        }),
        ("DES engine blocking signal preserves pending training at the same timestamp", () => {
            var e=Engine();e.ScheduleWorldSignal(600,"important","pause",true);e.Submit(Train("pending"));
            var r=e.AdvanceTo(1800);Check(r.WorldTime==600&&r.Reason==AdvanceStopReason.ImportantEvent,"Engine overshot stop");
            Check(e.Snapshot().Methods!.Learners["player"]["QingyunFist"]==0,"Ran after blocker");
            e=new(WorldCodec.Clone(e.Snapshot()));e.AdvanceTo(1800);
            Check(e.Snapshot().Methods!.Learners["player"]["QingyunFist"]==1,"Lost pending same time training");
        }),
        ("DES no-event interval and signals do not call the NPC planner", () => {
            var w=TestScenarioCatalog.Create("methods");DiscreteTime.EnableFresh(w);var planner=new CountingPlanner();var e=new SimulationEngine(w,planner);
            e.ScheduleWorldSignal(17,"short","signal");e.AdvanceTo(599,autonomous:true);
            Check(e.WorldTimeSeconds==599&&planner.Calls==0&&e.Snapshot().Clock.Tick==0,"Ordinary empty time called planner");
        }),
        ("DES engine exceptions roll back clock queue assets and pending commands together", () => {
            var w=TestScenarioCatalog.Create("methods");DiscreteTime.EnableFresh(w);var planner=new CountingPlanner(true);var e=new SimulationEngine(w,planner);
            e.Submit(Train("rollback",6));string before=WorldCodec.Digest(e.Snapshot());
            try{e.AdvanceTo(1200,autonomous:true);throw new Exception("Missing injected failure");}catch(InvalidOperationException ex) when(ex.Message=="Injected decision failure"){}
            Check(planner.Calls==2&&WorldCodec.Digest(e.Snapshot())==before,"Failed advance left partial state");
        }),
        ("DES attack invalidates the remaining real training event without refunding AP", () => {
            var w=TestScenarioCatalog.Create("methods");var player=w.Characters.Single(a=>a.Id=="player");
            string enemy=w.Characters.First(a=>a.Id!="player"&&a.MonsterKind==MonsterKind.None).Id;
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Hostile=true}:a.Id==enemy?a with {
                DemoLocationId=player.DemoLocationId,HexPosition=player.HexPosition,RegionId=player.RegionId}:a).ToList();
            DiscreteTime.EnableFresh(w);var e=new SimulationEngine(w);e.Submit(Train("interrupted",6));e.AdvanceTo(600);
            e.Submit(new("attack",enemy,"player","combat",1,0,DecisionSource.Npc,ActionKind.Engage));
            var stop=e.AdvanceTo(3600);w=e.Snapshot();
            Check(stop.Reason==AdvanceStopReason.PlayerBattle&&w.MortalTrainingWork.Count==0&&
                w.Methods!.Learners["player"]["QingyunFist"]==1&&w.Time!.Charges.Single(c=>c.ActionId=="interrupted").Cost==6,"Interrupt duplicated gain/refund");
            var events=w.Time!.DiscreteEvents!;
            Check(events.Pending.Where(p=>p.Kind==SimulationEventKind.MortalTraining).All(p=>p.Version!=events.Versions[p.CorrelationId]),"Interrupted version still live");
        }),
        ("DES rest AP and new game preserve shared rules and explicit event mode", () => {
            var w=TestScenarioCatalog.Create("methods");int n=w.Time!.Budgets.FindIndex(b=>b.ActorId=="player");
            w.Time.Budgets[n]=w.Time.Budgets[n] with {Remaining=29};DiscreteTime.EnableFresh(w);
            var e=new SimulationEngine(w);e.Submit(new("rest","player","player","stamina",1,0,DecisionSource.Player,ActionKind.Rest));e.AdvanceTo(600);
            Check(e.Snapshot().Time!.Budgets.Single(b=>b.ActorId=="player").Remaining==30,"Rest AP changed");
            var fresh=NewGameFactory.CreateLike(e.Snapshot(),"fresh");Check(fresh.Time!.DiscreteEvents?.WorldTime==0&&fresh.Methods is not null,"New game lost mode");
            try{e.StepValidatedBatch(10,10);throw new Exception("Legacy batch bypassed clock");}catch(InvalidOperationException){}
        }),
        ("DES callbacks add same time events after previously queued sequences", () => {
            var s=Fresh();var q=new EventScheduler(s);q.Schedule(10,SimulationEventKind.Signal,"a");q.Schedule(10,SimulationEventKind.Signal,"b");
            var seen=new List<string>();q.AdvanceTo(10,item=>{seen.Add(item.CorrelationId);if(item.CorrelationId=="a")q.Schedule(10,SimulationEventKind.Signal,"c");});
            Check(seen.SequenceEqual(new[]{"a","b","c"}),"New event reordered existing events");
        }),
        ("DES remains opt in and old minute worlds keep their original Step behavior", () => {
            var w=TestScenarioCatalog.Create("methods");Check(w.Time!.DiscreteEvents is null,"Implicit migration");
            var e=new SimulationEngine(w);e.Submit(Train("legacy"));Check(e.Step().Single().Accepted&&e.Snapshot().Clock.Tick==1,"Legacy Step changed");
            try{DiscreteTime.EnableFresh(e.Snapshot());throw new Exception("Migrated active world");}catch(InvalidOperationException){}
        }),
        ("DES persisted consumed event ids reject replayed completion records", () => {
            var s=Fresh();var q=new EventScheduler(s);var item=q.Schedule(10,SimulationEventKind.Signal,"once");q.AdvanceTo(10,_=>{});
            s=JsonSerializer.Deserialize<DiscreteEventState>(JsonSerializer.Serialize(s))!;s.Pending.Add(item);
            try{new EventScheduler(s);throw new Exception("Consumed event replay accepted");}catch(InvalidDataException){}
        }),
        ("DES rejects unconverted off-grid actions rather than shortening their duration", () => {
            var e=Engine();e.AdvanceTo(1020);string before=WorldCodec.Digest(e.Snapshot());
            var result=e.ExecutePlayerActivity(new("off-grid-rest","player","player","stamina",1,0,DecisionSource.Player,ActionKind.Rest));
            Check(!result.Accepted&&WorldCodec.Digest(e.Snapshot())==before,"Off-grid legacy action changed world");
            e.AdvanceTo(1200);e.Submit(Train("aligned"));e.AdvanceTo(1800);
            Check(e.Snapshot().Methods!.Learners["player"]["QingyunFist"]==1,"Failed after explicit boundary advance");
        }),
        ("DES cannot attach an event clock to an existing zero-time reserved action", () => {
            var e=Engine();e.Submit(Train("reserved"));e.AdvanceTo(0);
            var w=e.Snapshot();w.Time!.DiscreteEvents=null;
            try{DiscreteTime.EnableFresh(w);throw new Exception("Enabled over existing reservations");}catch(InvalidOperationException){}
        }),
        ("DES unversioned queue saves are rejected and original files remain unchanged", () => {
            string path=Path.Combine(Path.GetTempPath(),"des-old-"+Guid.NewGuid().ToString("N")+".json");
            var content=WorldCodec.Serialize(Engine().Snapshot());File.WriteAllText(path,content);
            try{try{new JsonWorldStore().Load(path);throw new Exception("Unversioned queue save accepted");}catch(InvalidDataException){}
                Check(File.ReadAllText(path)==content,"Rejected save modified source");}finally{File.Delete(path);}
        }),
        ("DES persisted queue has its own validated format version", () => {
            var w=Engine().Snapshot();w.Time!.DiscreteEvents!.FormatVersion=99;
            try{new SimulationEngine(w);throw new Exception("Unknown queue format accepted");}catch(InvalidDataException){}
        })
    };
}
