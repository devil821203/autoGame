using Cultivation.Domain;
using Cultivation.Simulation;

internal static class TimeTests
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static WorldState World()
    {
        var w = TimeScenario.Create(); w.PlayerAutoSchedule = false; return w;
    }
    static int AP(WorldState w, string id = "player") => w.Time!.Budgets.Single(b => b.ActorId == id).Remaining;
    static ActionIntent Gather(string id = "timed-gather") => new(id, "player", "grove", "herb", 1, 0, DecisionSource.Player, ActionKind.Gather);
    static WorldState GatheringWorld(long tick = 0)
    {
        var w = World(); w.SectFacilities = null; w.HuntRules = null;
        w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { DemoLocationId = "grove", HexPosition = w.HexMap!.Tiles.Single(t => t.PlaceId == "grove").Coord } : c).ToList();
        w.Clock = w.Clock with { Tick = tick };
        w.Time!.Budgets = w.Time.Budgets.Select(b => b with { Day = tick / 30 }).ToList(); return w;
    }
    static WorldState FightWorld(string actor = "player", long tick = 0)
    {
        var w = World(); w.Clock = w.Clock with { Tick = tick };
        w.Time!.Budgets = w.Time.Budgets.Select(b => b with { Day = tick / 30 }).ToList();
        var target = w.Characters.First(c => c.Id != actor && c.Id != "player");
        w.Characters = w.Characters.Select(c => c.Id == actor ? c with { Health = 100, BaseAttack = 100, DemoLocationId = "town", HexPosition = w.HexMap!.Tiles.Single(t => t.PlaceId == "town").Coord } :
            c.Id == target.Id ? c with { Hostile = true, Health = 40, BaseAttack = 0, DemoLocationId = "town", HexPosition = w.HexMap!.Tiles.Single(t => t.PlaceId == "town").Coord } : c).ToList();
        return w;
    }
    static ActionIntent Fight(WorldState w, string actor = "player") => new("timed-fight", actor,
        w.Characters.First(c => c.Hostile && c.Id != actor).Id, "combat", 1, 0,
        actor == "player" ? DecisionSource.Player : DecisionSource.Npc, ActionKind.Engage);
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Time calendar seasons and years use 30 ticks per day", () => {
            var r = new TimeRules();
            Check(CalendarCalculator.Date(0,r) == new CalendarDate(1,"春",1,0), "Initial date");
            Check(CalendarCalculator.Date(900,r) == new CalendarDate(1,"夏",1,0), "Summer boundary");
            Check(CalendarCalculator.Date(3600,r) == new CalendarDate(2,"春",1,0), "Year boundary");
        }),
        ("Time gather takes exactly two ticks and pays once at end", () => {
            var w = GatheringWorld(); long herbs = Inventory.Balance(w,"player","herb"); var e = new SimulationEngine(w);
            e.Submit(Gather()); Check(e.Step().Single().Accepted,"Gather rejected");
            Check(AP(e.Snapshot()) == 28 && Inventory.Balance(e.Snapshot(),"player","herb") == herbs,"Early output or wrong AP");
            e.Submit(Gather("busy")); Check(!e.Step().Single().Accepted,"Busy actor accepted");
            Check(Inventory.Balance(e.Snapshot(),"player","herb") == herbs+1,"Missing end tick reward");
            e.Step(); Check(Inventory.Balance(e.Snapshot(),"player","herb") == herbs+1,"Repeated reward");
        }),
        ("Time cross-day work retains progress without charging new day", () => {
            var e = new SimulationEngine(GatheringWorld(29)); e.Submit(Gather()); e.Step();
            Check(e.Snapshot().Clock.Tick == 30 && AP(e.Snapshot()) == 30 && e.Snapshot().Scheduled.Count == 1,"Day reset or progress lost");
            var resumed = new SimulationEngine(WorldCodec.Clone(e.Snapshot())); e.Step(); resumed.Step();
            Check(AP(e.Snapshot()) == 30 && e.Snapshot().Scheduled.Count == 0,"Cross-day recharge");
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()),"Resume changed work");
        }),
        ("Time rejection duplicates and exhausted AP cannot spend twice", () => {
            var w = GatheringWorld(); w.Time!.Budgets = w.Time.Budgets.Select(b=> b.ActorId=="player" ? b with {Remaining=1}:b).ToList();
            var e=new SimulationEngine(w); e.Submit(Gather()); Check(!e.Step().Single().Accepted && AP(e.Snapshot())==1,"Insufficient AP charged");
            e.Submit(Gather()); Check(!e.Step().Single().Accepted && e.Snapshot().Time!.Charges.Count==0,"Duplicate charged");
            for(int n=2;n<30;n++)e.Step(); Check(AP(e.Snapshot())==30,"Idle day did not reset AP");
        }),
        ("Time player combat freezes world and replays five ticks once", () => {
            var w=FightWorld(); var e=new SimulationEngine(w); e.Submit(Fight(w)); Check(e.Step().Single().Accepted,"Battle rejected");
            Check(e.Snapshot().Clock.Tick==0 && AP(e.Snapshot())==25,"Battle start advanced world");
            w=e.Snapshot(); w.BoardBattle!.PlayerAutoCombat=true; e=new(w);
            var resumed=new SimulationEngine(WorldCodec.Clone(w));
            e.Step(true); resumed.Step(true);
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Battle save continuation differs");
            Check(e.Snapshot().BoardBattle!.Phase==BoardBattlePhase.Finished && e.Snapshot().Clock.Tick==5,"Catchup not exactly five ticks");
            Check(e.Snapshot().Time!.BattleTime.Single().RemainingTicks==0,"Catchup receipt missing");
            e.Step(); Check(e.Snapshot().Clock.Tick==6,"Battle replay repeated");
        }),
        ("Time NPC combat settles directly without adding global battle ticks", () => {
            var w=FightWorld("npc:00"); var e=new SimulationEngine(w); e.Submit(Fight(w,"npc:00"));
            Check(e.Step().Single().Accepted,"NPC battle rejected");
            Check(e.Snapshot().Clock.Tick==1 && e.Snapshot().BoardBattle!.Phase==BoardBattlePhase.Finished,"NPC battle froze or added global ticks");
            Check(AP(e.Snapshot(),"npc:00")==25 && e.Snapshot().Time!.BattleTime.Single().RemainingTicks==0,"NPC battle charge wrong");
        }),
        ("Time defender with zero AP can fight and catchup crosses a day once", () => {
            var w=FightWorld("npc:00",28);
            w.Characters=w.Characters.Select(c=>c.Id=="player" ? c with {Health=100, BaseAttack=100, Hostile=false} :
                c.Id=="npc:00" ? c with {Health=40,BaseAttack=0,Hostile=true}:c).ToList();
            w.Time!.Budgets=w.Time.Budgets.Select(b=>b.ActorId=="player" ? b with {Remaining=0}:b).ToList();
            var e=new SimulationEngine(w);
            e.Submit(new("defense", "npc:00", "player", "combat",1,0,DecisionSource.Npc,ActionKind.Engage));
            Check(e.Step().Single().Accepted && e.Snapshot().Clock.Tick==28 && AP(e.Snapshot())==0,"Free defense blocked");
            w=e.Snapshot(); w.BoardBattle!.PlayerAutoCombat=true; e=new(w); e.Step(true);
            Check(e.Snapshot().Clock.Tick==33 && AP(e.Snapshot())==30,"Cross-day defense replay wrong");
            Check(!e.Snapshot().Time!.Charges.Any(c=>c.ActorId=="player"),"Defender charged");
            Check(e.Snapshot().Events.Count(v=>v.Kind=="DayStarted")==1,"Duplicate daily reset");
        }),
        ("Time rest costs one AP per recovered tick and battle freezes all work", () => {
            var w=FightWorld(); w.Characters=w.Characters.Select(c=>c.Id=="player" ? c with {Health=60}:c).ToList();
            var e=new SimulationEngine(w); e.Submit(new("rest", "player","player","stamina",1,0,DecisionSource.Player,ActionKind.Rest));
            e.Step(); e.Step(); Check(AP(e.Snapshot())==28 && e.Snapshot().Characters.Single(c=>c.Id=="player").Health==80,"Rest cost/recovery mismatch");
            w=FightWorld(); var ally=w.Characters.First(c=>c.Id!="player" && !c.Hostile);
            w.Characters=w.Characters.Select(c=>c.Id==ally.Id ? c with {Health=50,Stamina=new(50,Resting:true)}:c).ToList();
            e=new(w); e.Submit(Fight(w)); e.Step(); e.Step(false);
            Check(e.Snapshot().Clock.Tick==0 && e.Snapshot().Characters.Single(c=>c.Id==ally.Id).Health==50,"World recovery ran during battle");
            Check(e.Snapshot().Time!.Charges.Count==1,"Frozen world spent rest AP");
        }),
        ("Time NPC direct battle matches ordinary board round calculation", () => {
            var w=FightWorld("npc:00"); var i=Fight(w,"npc:00"); var fast=new SimulationEngine(w); fast.Submit(i); fast.Step();
            w.Time=null; w.BoardRules=new(100); w.BoardBattle=new() {Id=i.Id,Fighters=[
                new(i.ActorId,BattleSide.Left,new(0,0),10,1),new(i.TargetId,BattleSide.Right,new(0,0),10,1)]};
            var slow=new SimulationEngine(w);
            while(slow.Snapshot().BoardBattle!.Phase==BoardBattlePhase.Active)slow.Step(true);
            Check(slow.Snapshot().BoardBattle!.Round==fast.Snapshot().BoardBattle!.Round,"Round outcome differs");
            foreach(var id in new[]{i.ActorId,i.TargetId})Check(slow.Snapshot().Characters.Single(c=>c.Id==id).Health==fast.Snapshot().Characters.Single(c=>c.Id==id).Health,"NPC fast combat differs");
        }),
        ("Time new scenario converts periods to days and preserves legacy mode", () => {
            var old=PlayerAutoScheduleScenario.Create(); var w=TimeScenario.Create();
            Check(w.SectPayroll!.IntervalTicks==old.SectPayroll!.IntervalTicks*30,"Payroll conversion");
            Check(w.TestProcurement!.IntervalTicks==old.TestProcurement!.IntervalTicks*30,"Procurement conversion");
            Check(w.SectDuty!.ReviewIntervalTicks==old.SectDuty!.ReviewIntervalTicks*30,"Review conversion");
            Check(MissionScenario.Publish(w).Offer!.DeadlineTick==210,"Seven day deadline");
            Check(old.Time is null && NewGameFactory.CreateLike(w,"新行者").Time is not null,"Mode preservation");
        }),
        ("Time dungeon party pays once per volunteer and retains wolf economy loot", () => {
            var w=World(); w.Dungeon=w.Dungeon! with {EncounterChance=1,WolfChance=1};
            var ids=new[]{"player","npc:00","npc:01"};
            w.Characters=w.Characters.Select(c=>ids.Contains(c.Id) ? c with {Health=100,BaseAttack=100,OrganizationId="sect",Hostile=false,
                DemoLocationId="dungeon",HexPosition=w.HexMap!.Tiles.Single(t=>t.PlaceId=="dungeon").Coord}:c).ToList();
            w.Parties.Add(new("test-party","player",ids.ToList()));
            var e=new SimulationEngine(w); e.Submit(new("explore", "player","dungeon","combat",1,0,DecisionSource.Player,ActionKind.ExploreDungeon));
            Check(e.Step().Single().Accepted,"Party exploration rejected");
            Check(e.Snapshot().Clock.Tick==1 && AP(e.Snapshot())==24 && AP(e.Snapshot(),"npc:00")==25,"Party AP charge wrong");
            w=e.Snapshot(); w.BoardBattle!.PlayerAutoCombat=true; e=new(w); e.Step(true);
            Check(e.Snapshot().Clock.Tick==6,"Party multiplied shared catchup");
            Check(e.Snapshot().Time!.Charges.Count(c=>c.Cost==5)==3,"Battle charges duplicated");
            Check(e.Snapshot().Ledger.Any(l=>l.ResourceId=="beast-hide" && ids.Contains(l.To) && l.Reason=="PartyLoot"),"Wolf loot lost");
        }),
        ("Time autonomous simulation saves and resumes deterministically", () => {
            var e=new SimulationEngine(TimeScenario.Create()); for(int n=0;n<12;n++)e.Step(true);
            var resumed=new SimulationEngine(WorldCodec.Clone(e.Snapshot()));
            for(int n=0;n<35;n++){e.Step(true);resumed.Step(true);}
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Autonomous resume differs");
            Check(e.Snapshot().Time!.Charges.Any(c=>c.ActorId=="player"),"No autonomous player charge");
        })
    };
}
