using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class ScheduledTransferLifecycleTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static ActionIntent Transfer(string id,string actor="player",string target="npc:03",long duration=3)=>new(id,actor,target,"iron-material",1,duration,actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.Transfer);
    private static WorldState World(){var w=LifeEventScenario.Create();w.Ledger.Add(new(w.NextSequence++,0,"transfer-fixture","$genesis","player","iron-material",2,"Genesis"));return w;}
    private static WorldState Start(string id,out SimulationEngine engine){engine=new SimulationEngine(World());engine.Submit(Transfer(id));engine.AdvanceTo(0);return engine.Snapshot();}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 deferred transfer fails without late resources and retains paid AP",()=>{
            var w=World();var e=new SimulationEngine(w);e.Submit(Transfer("deferred-failure",duration:0));e.AdvanceTo(0);w=e.Snapshot();
            var a=w.Time!.ActionRuntime!.Actions["deferred-failure"];long available=Inventory.Balance(w,"player","iron-material");
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"deplete-fixture","player","sect","iron-material",available,"FixtureTransfer"));e=new SimulationEngine(w);e.AdvanceTo(a.DueAt);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[a.Id].Phase==ActionPhase.Failed&&w.Time.Charges.Count(c=>c.ActionId==a.Id)==1&&!w.Ledger.Any(l=>l.ActionId==a.Id&&l.Reason=="Transfer"),"Failure transferred goods or changed AP");
            WorldIntegrity.Validate(WorldCodec.Clone(w));
        }),
        ("R2 deferred transfer survives JSON and SQLite without early debit",()=>{
            var e=new SimulationEngine(World());e.Submit(Transfer("deferred-save",duration:0));e.AdvanceTo(0);var w=e.Snapshot();var a=w.Time!.ActionRuntime!.Actions["deferred-save"];
            Check(!w.Ledger.Any(l=>l.ActionId==a.Id&&l.Reason=="Transfer"),"Transfer occurred early");string dir=Path.Combine(Path.GetTempPath(),"r2-deferred-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var original=new SimulationEngine(w);var loaded=new SimulationEngine(store.Load(path));original.AdvanceTo(a.DueAt);loaded.AdvanceTo(a.DueAt);Check(WorldCodec.Digest(original.Snapshot())==WorldCodec.Digest(loaded.Snapshot()),"Deferred save continuation differs");}}
            finally{foreach(string path in Directory.GetFiles(dir))File.Delete(path);Directory.Delete(dir);}
        }),
        ("R2 historical scheduled transfer remains in old mode",()=>{
            var w=RegionalWorldScenario.Create();w.Ledger.Add(new(w.NextSequence++,0,"legacy-transfer-fixture","$genesis","player","iron-material",1,"Genesis"));var e=new SimulationEngine(w);
            e.Submit(Transfer("legacy-transfer"));e.AdvanceTo(0);w=e.Snapshot();Check(!w.Time!.ActionRuntime!.Actions.ContainsKey("legacy-transfer"),"Historical mode changed");
            var task=w.Scheduled.Single(t=>t.Intent.Id=="legacy-transfer");e.AdvanceTo(task.DueTick*DiscreteTime.SecondsPerStep(w));Check(e.Snapshot().Ledger.Count(x=>x.ActionId=="legacy-transfer"&&x.Reason=="Completed")==1,"Historical transfer stopped");
        }),
        ("R2 crafted equipment retains identity and durability in transit",()=>{
            var e=new SimulationEngine(LifeEventScenario.Create());var craft=new ActionIntent("transfer-craft","player","player","iron-sword",1,0,DecisionSource.Player,ActionKind.CraftLifeItem);
            var result=e.ExecutePlayerActivity(craft);Check(result.Accepted,"Craft setup failed: "+result.Reason);var w=e.Snapshot();var gear=w.LifeSystem!.Equipment.Single(g=>g.ActionId==craft.Id);var durability=EquipmentCondition.Remaining(w,gear.Id);
            var i=new ActionIntent("transfer-gear","player","npc:03",gear.Id,1,3,DecisionSource.Player,ActionKind.Transfer);e.Submit(i);e.AdvanceTo(e.WorldTimeSeconds!.Value);w=e.Snapshot();
            Check(Inventory.Balance(w,"$escrow:transfer-gear",gear.Id)==1&&EquipmentCondition.Remaining(w,gear.Id)==durability,"Gear lost in escrow");long due=w.Scheduled.Single(t=>t.Intent.Id==i.Id).DueAtSeconds!.Value;
            string path=Path.Combine(Path.GetTempPath(),"r2-gear-"+Guid.NewGuid().ToString("N")+".json");try{var store=new JsonWorldStore();store.Save(path,w);var resumed=new SimulationEngine(store.Load(path));e.AdvanceTo(due);resumed.AdvanceTo(due);w=e.Snapshot();Check(WorldCodec.Digest(w)==WorldCodec.Digest(resumed.Snapshot()),"Gear continuation differs");}finally{File.Delete(path);}
            Check(Inventory.Balance(w,"npc:03",gear.Id)==1&&EquipmentCondition.Remaining(w,gear.Id)==durability&&w.LifeSystem!.Equipment.Single(g=>g.Id==gear.Id).Quality==gear.Quality,"Gear changed after delivery");
        }),
        ("R2 legacy weapon retains identity and durability in transit",()=>{
            var w=LifeEventScenario.Create();string item=w.Advancement!.WeaponId;if(Inventory.Balance(w,"player",item)==0)w.Ledger.Add(new(w.NextSequence++,0,"weapon-fixture","$genesis","player",item,1,"Genesis"));
            var e=new SimulationEngine(w);var equip=new ActionIntent("legacy-equip","player","player",item,1,0,DecisionSource.Player,ActionKind.EquipWeapon);Check(e.ExecutePlayerActivity(equip).Accepted,"Equip setup failed");
            string gear=e.Snapshot().Characters.Single(c=>c.Id=="player").EquippedWeaponId!;Check(e.ExecutePlayerActivity(equip with {Id="legacy-unequip",Kind=ActionKind.UnequipWeapon}).Accepted,"Unequip setup failed");
            w=e.Snapshot();var durability=EquipmentCondition.Remaining(w,gear);var i=new ActionIntent("legacy-gear","player","npc:03",gear,1,3,DecisionSource.Player,ActionKind.Transfer);e.Submit(i);e.AdvanceTo(e.WorldTimeSeconds!.Value);w=e.Snapshot();
            Check(Inventory.Balance(w,"$escrow:legacy-gear",gear)==1,"Weapon missing during escrow");long due=w.Scheduled.Single(t=>t.Intent.Id==i.Id).DueAtSeconds!.Value;e.AdvanceTo(due);w=e.Snapshot();Check(Inventory.Balance(w,"npc:03",gear)==1&&EquipmentCondition.Remaining(w,gear)==durability,"Weapon changed after delivery");
        }),
        ("R2 NPC and player transfers share lifecycle",()=>{
            var w=World();w.Ledger.Add(new(w.NextSequence++,0,"npc-transfer-fixture","$genesis","npc:03","iron-material",1,"Genesis"));var e=new SimulationEngine(w);
            e.Submit(Transfer("player-transfer"));e.Submit(Transfer("npc-transfer","npc:03","player"));e.AdvanceTo(0);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions.ContainsKey("player-transfer")&&w.Time.ActionRuntime.Actions.ContainsKey("npc-transfer"),"Missing transfer action");long due=w.Scheduled.Where(t=>t.Intent.Id is "player-transfer" or "npc-transfer").Max(t=>t.DueAtSeconds!.Value);e.AdvanceTo(due);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions["player-transfer"].Phase==ActionPhase.Completed&&w.Time.ActionRuntime.Actions["npc-transfer"].Phase==ActionPhase.Completed,"Transfers not completed");
        }),
        ("R2 transfer reserves sender assets and delivers exactly once",()=>{
            var w=Start("transfer",out var e);var a=w.Time!.ActionRuntime!.Actions["transfer"];var r=w.Time.ActionRuntime.Reservations["transfer:transfer-resource"];
            Check(a.Phase==ActionPhase.Running&&r.Remaining==1&&Inventory.Balance(w,"$escrow:transfer","iron-material")==1,"Missing escrow");
            long due=w.Scheduled.Single(t=>t.Intent.Id=="transfer").DueAtSeconds!.Value;e.AdvanceTo(due);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions["transfer"].Phase==ActionPhase.Completed&&w.Time.ActionRuntime.Reservations[r.Id].Consumed==1&&w.Ledger.Count(l=>l.ActionId=="transfer"&&l.Reason=="Completed")==1,"Wrong completion");
            e.Submit(Transfer("transfer"));e.AdvanceTo(due);Check(e.Snapshot().Ledger.Count(l=>l.ActionId=="transfer"&&l.Reason=="Completed")==1,"Duplicate transfer");
        }),
        ("R2 transfer resumes through JSON and SQLite",()=>{
            var w=Start("persist",out _);long due=w.Scheduled.Single(t=>t.Intent.Id=="persist").DueAtSeconds!.Value;
            string dir=Path.Combine(Path.GetTempPath(),"r2-transfer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(path));a.AdvanceTo(due);b.AdvanceTo(due);Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Continuation differs");}}
            finally{foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
        }),
        ("R2 transfer rejects missing reservation or altered target",()=>{
            var w=Start("forged",out _);
            foreach(bool missing in new[]{true,false}){var copy=WorldCodec.Clone(w);if(missing)copy.Time!.ActionRuntime!.Reservations.Remove("forged:transfer-resource");else{int n=copy.Scheduled.FindIndex(t=>t.Intent.Id=="forged");copy.Scheduled[n]=copy.Scheduled[n] with {Intent=copy.Scheduled[n].Intent with {TargetId="npc:04"}};}
                bool rejected=false;try{WorldIntegrity.Validate(copy);}catch(InvalidDataException){rejected=true;}Check(rejected,"Forged transfer accepted");}
        }),
        ("R2 zero duration transfer preserves existing behavior",()=>{
            var e=new SimulationEngine(World());e.Submit(Transfer("instant",duration:0));e.AdvanceTo(0);var w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions["instant"].Phase==ActionPhase.Running&&w.Time.Deferred.Any(t=>t.Intent.Id=="instant")&&!w.Ledger.Any(l=>l.ActionId=="instant"&&l.Reason=="Transfer")&&!w.Time.ActionRuntime.Reservations.ContainsKey("instant:transfer-resource"),"Deferred transfer reserved early or is missing action state");
            e.AdvanceTo(600);w=e.Snapshot();Check(w.Ledger.Count(l=>l.ActionId=="instant"&&l.Reason=="Transfer")==1&&w.Time!.ActionRuntime!.Actions["instant"].Phase==ActionPhase.Completed,"Deferred transfer did not complete");
        })
    };
}
