using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class RecoveryEventTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static ActionIntent Rest(string id="seconds-rest")=>new(id,"player","player","stamina",1,0,DecisionSource.Player,ActionKind.Rest);
    private static WorldState RestWorld(){
        var w=LifeEventScenario.Create();int n=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[n]=w.Characters[n] with{Health=80,DemoLocationId="sect",HexPosition=RegionGeometry.Resolve(w.HexMap!,w.Characters[n],"sect"),Stamina=w.Characters[n].Stamina! with{Current=50,HasBed=false}};
        int budget=w.Time!.Budgets.FindIndex(b=>b.ActorId=="player");if(budget<0)w.Time.Budgets.Add(new("player",0,28));else w.Time.Budgets[budget]=new("player",0,28);return w;
    }
    private static WorldState PowderWorld(){
        var w=LifeEventScenario.Create();int n=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[n]=w.Characters[n] with{Health=20};
        w.Ledger.Add(new(w.NextSequence++,0,"seconds-powder-stock","$genesis","player","bleeding-powder",1,"Genesis"));return w;
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Recovery seconds rest has no partial-interval health stamina or AP gains and ends when full",()=>{
            var e=new SimulationEngine(RestWorld());e.AdvanceTo(1020);e.Submit(Rest());e.AdvanceTo(1619);var w=e.Snapshot();var a=w.Characters.Single(a=>a.Id=="player");
            Check(a.Health==80&&a.Stamina!.Current==50&&w.Time!.Budgets.Single(b=>b.ActorId=="player").Remaining==28,"Early rest gain");
            e.AdvanceTo(1620);w=e.Snapshot();a=w.Characters.Single(a=>a.Id=="player");Check(a.Health==90&&a.Stamina!.Current==80&&w.Time!.Budgets.Single(b=>b.ActorId=="player").Remaining==29,"Rest pulse mismatch");
            e.AdvanceTo(2220);w=e.Snapshot();a=w.Characters.Single(a=>a.Id=="player");Check(a.Health==100&&a.Stamina!.Current==100&&!a.Stamina.Resting&&w.Time!.RestDueSeconds.Count==0&&w.Time.Budgets.Single(b=>b.ActorId=="player").Remaining==30,"Rest did not end");
            int count=w.Events.Count(v=>v.Kind=="RestRecoveryResolved");e.AdvanceTo(4000);Check(e.Snapshot().Events.Count(v=>v.Kind=="RestRecoveryResolved")==count,"Rest continued after full");
        }),
        ("Recovery seconds interactive rest stops at the exact final recovery pulse",()=>{
            var e=new SimulationEngine(RestWorld());e.AdvanceTo(1020);var result=e.ExecutePlayerActivity(Rest());Check(result.Accepted&&e.WorldTimeSeconds==2220,result.ToString()+":"+e.WorldTimeSeconds);
        }),
        ("Recovery seconds idle does not consume AP or spontaneously restore health or stamina",()=>{
            var e=new SimulationEngine(RestWorld());e.AdvanceTo(6000);var w=e.Snapshot();var a=w.Characters.Single(a=>a.Id=="player");
            Check(a.Health==80&&a.Stamina!.Current==50&&w.Time!.Budgets.Single(b=>b.ActorId=="player").Remaining==28&&!w.Events.Any(v=>v.Kind=="RestRecoveryResolved"),"Idle caused rest effects");
        }),
        ("Recovery seconds exhaustion waits a full interval then uses unconscious recovery until awake",()=>{
            var w=LifeEventScenario.Create();int n=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[n]=w.Characters[n] with{Stamina=w.Characters[n].Stamina! with{Current=1}};var e=new SimulationEngine(w);e.AdvanceTo(1020);
            var train=new ActionIntent("exhaust-at-seconds","player","player","Strength",1,0,DecisionSource.Player,ActionKind.TrainMortalAttribute);e.Submit(train);e.AdvanceTo(1620);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Stamina!.Unconscious,"Training did not exhaust");
            e.AdvanceTo(2219);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Stamina!.Current==0,"Early unconscious recovery");e.AdvanceTo(2220);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Stamina!.Current==10,"Wrong unconscious recovery");
            e.AdvanceTo(4020);var a=e.Snapshot().Characters.Single(a=>a.Id=="player");Check(!a.Stamina!.Unconscious&&a.Stamina.Current==40,"Awakening threshold changed");
        }),
        ("Recovery seconds world medicine begins after use and delivers exactly five full intervals",()=>{
            var e=new SimulationEngine(PowderWorld());e.AdvanceTo(1020);e.Submit(new("seconds-powder","player","player","bleeding-powder",1,0,DecisionSource.Player,ActionKind.UseMedicine));e.AdvanceTo(1620);
            Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Health==20&&e.Snapshot().LifeSystem!.Effects.Single().NextAtSeconds==2220,"Medicine applied early");
            e.AdvanceTo(2219);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Health==20,"Partial interval healed");e.AdvanceTo(2220);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Health==30,"First medicine pulse missing");
            e.AdvanceTo(5000);var w=e.Snapshot();Check(w.Characters.Single(a=>a.Id=="player").Health==70&&w.LifeSystem!.Effects.Count==0&&w.Events.Count(v=>v.Kind=="MedicineEffectTick")==5,"Medicine total or count changed");
        }),
        ("Recovery seconds combat medicine preserves round pulses then switches remaining doses to world seconds",()=>{
            var w=PowderWorld();w.BoardRules=new(100);w.BoardBattle=new(){Id="seconds-medicine-battle",Fighters=[new("player",BattleSide.Left,new(0,0),10,1),new("npc:00",BattleSide.Right,new(0,0),9,1)]};var e=new SimulationEngine(w);
            e.Submit(new("combat-seconds-powder","player","player","bleeding-powder",1,0,DecisionSource.Player,ActionKind.UseCombatMedicine));e.Step(false);
            Check(e.WorldTimeSeconds==0&&e.Snapshot().Characters.Single(a=>a.Id=="player").Health==30&&e.Snapshot().LifeSystem!.Effects.Single().NextAtSeconds is null,"Combat medicine became world time");
            e.AdvanceTo(5000);Check(e.WorldTimeSeconds==0,"Player battle failed to freeze world");e.Step(false);w=e.Snapshot();int n=w.Characters.FindIndex(a=>a.Id=="npc:00");w.Characters[n]=w.Characters[n] with{Health=0};e=new(w);e.Step(false);
            var effect=e.Snapshot().LifeSystem!.Effects.Single();Check(effect.BattleId is null&&effect.NextAtSeconds==600,"Battle to world effect deadline wrong");
            int battleHealth=e.Snapshot().Characters.Single(a=>a.Id=="player").Health;e.AdvanceTo(599);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Health==battleHealth,"Post-battle healed early");e.AdvanceTo(effect.Remaining*600L);w=e.Snapshot();Check(w.Characters.Single(a=>a.Id=="player").Health==70&&w.Events.Count(v=>v.Kind=="MedicineEffectTick")==5&&w.LifeSystem!.Effects.Count==0,"Post-battle doses duplicated");
        }),
        ("Recovery seconds latest save retains rest deadline without repeating AP or health rewards",()=>{
            var e=new SimulationEngine(RestWorld());e.AdvanceTo(1020);e.Submit(Rest());e.AdvanceTo(1800);var w=e.Snapshot();
            string dir=Path.Combine(Path.GetTempPath(),"recovery-seconds-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(path));a.AdvanceTo(5000);b.AdvanceTo(5000);Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Recovery save continuation changed");File.Delete(path);
            }}finally{foreach(string path in Directory.GetFiles(dir))File.Delete(path);Directory.Delete(dir);}
        }),
        ("Recovery seconds preserve outdoor ordinary and home-bed recovery modifiers",()=>{
            foreach(var place in new[]{("grove",false),("sect",false),("sect",true)}){
                var w=RestWorld();int n=w.Characters.FindIndex(a=>a.Id=="player");var actor=w.Characters[n];w.Characters[n]=actor with{DemoLocationId=place.Item1,HexPosition=RegionGeometry.Resolve(w.HexMap!,actor,place.Item1),Stamina=actor.Stamina! with{Current=10,HasBed=place.Item2,HomeLocationId=place.Item1}};
                int gain=StaminaCalculator.Recovery(w.Characters[n],w.StaminaRules!);var e=new SimulationEngine(w);e.AdvanceTo(1020);e.Submit(Rest());e.AdvanceTo(1620);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Stamina!.Current==10+gain,"Recovery environment changed");
            }
        }),
        ("Recovery seconds do not revive an incapacitated former controlled actor",()=>{
            var e=new SimulationEngine(PowderWorld());e.AdvanceTo(1020);e.Submit(new("no-revive-powder","player","player","bleeding-powder",1,0,DecisionSource.Player,ActionKind.UseMedicine));e.AdvanceTo(1620);
            Check(e.SwitchControlledCharacter("npc:method-trainee",true).Accepted,"Control handoff fixture failed");var w=e.Snapshot();int n=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[n]=w.Characters[n] with{Health=0};e=new(w);e.AdvanceTo(5000);
            Check(e.Snapshot().Characters.Single(a=>a.Id=="player").Health==0&&e.Snapshot().LifeSystem!.Effects.Count==0,"Incapacitated actor revived");
        }),
        ("Recovery seconds NPCs can rest and resume their own work",()=>{
            var w=LifeEventScenario.Create();int n=w.Characters.FindIndex(a=>a.Id=="npc:03");w.Characters[n]=w.Characters[n] with{Stamina=w.Characters[n].Stamina! with{Current=1}};var e=new SimulationEngine(w);e.AdvanceTo(5000,true);w=e.Snapshot();
            Check(w.Events.Any(v=>v.Kind=="RestRecovered"&&v.Detail.StartsWith("npc:03:",StringComparison.Ordinal))&&w.Characters.Single(a=>a.Id=="npc:03").Stamina!.Current>1,"NPC never recovered");
        })
    };
}
