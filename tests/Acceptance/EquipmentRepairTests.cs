using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class EquipmentRepairTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static string Gear(WorldState w,string actor="player")=>w.EquipmentDurability!.LegacyWeapons.First(g=>Inventory.Balance(w,actor,g.Id)==1).Id;
    private static ActionIntent Repair(WorldState w,string id="repair-test",string actor="player",string? smith=null)=>new(id,actor,smith??actor,Gear(w,actor),1,0,actor==w.ControlledCharacterId?DecisionSource.Player:DecisionSource.Npc,ActionKind.RepairEquipment);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R8 self repair reserves material and equipment until exact completion",()=>{
            var w=EquipmentRepairScenario.Create();string gear=Gear(w);long material=Inventory.Balance(w,"player","iron-material");var e=new SimulationEngine(w);
            e.Submit(Repair(w));e.AdvanceTo(0);var started=e.Snapshot();
            Check(started.Time!.EquipmentRepair!.Work.Count==1&&EquipmentRepairActions.Locked(started,gear)&&Inventory.Balance(started,"player","iron-material")==material-1,"Missing reservation");
            Check(ActionValidator.Validate(started,new("try-transfer","player","npc:03",gear,1,0,DecisionSource.Player)) is not null,"Locked gear transferable");
            Check(ActionValidator.Validate(started,new("try-equip","player","player",w.Advancement!.WeaponId,1,0,DecisionSource.Player,ActionKind.EquipWeapon)) is not null,"Locked legacy instance selectable");
            e.AdvanceTo(1799);Check(EquipmentCondition.Remaining(e.Snapshot(),gear)==20,"Repaired early");e.AdvanceTo(1800);var final=e.Snapshot();
            Check(EquipmentCondition.Remaining(final,gear)==40&&final.Time!.EquipmentRepair!.Receipts.Count==1&&!EquipmentRepairActions.Locked(final,gear),"Repair completion mismatch");
            Check(final.Time!.Charges.Single(c=>c.ActionId=="repair-test").Cost==3,"Incorrect AP");e.AdvanceTo(1800);Check(e.Snapshot().Time!.EquipmentRepair!.Receipts.Count==1,"Duplicate completion");
        }),
        ("R8 commissioned repair pays same-location smith only after successful completion",()=>{
            var w=EquipmentRepairScenario.Create();var owner=w.Characters.Single(a=>a.Id=="player");int n=w.Characters.FindIndex(a=>a.TestProfession==TestProfession.Smith);var smith=w.Characters[n];
            w.Characters[n]=smith with{RegionId=owner.RegionId,DemoLocationId=owner.DemoLocationId,HexPosition=owner.HexPosition,Health=100};
            long start=Inventory.Balance(w,smith.Id,"spirit-stone");var e=new SimulationEngine(w);e.Submit(Repair(w,smith:smith.Id));e.AdvanceTo(0);
            Check(Inventory.Balance(e.Snapshot(),smith.Id,"spirit-stone")==start&&EquipmentRepairActions.Busy(e.Snapshot(),smith.Id),"Fee paid early or provider not busy");
            e.AdvanceTo(1800);Check(Inventory.Balance(e.Snapshot(),smith.Id,"spirit-stone")==start+5,"Smith not paid");
        }),
        ("R8 interruption refunds escrow but retains paid AP and invalidates old event",()=>{
            var w=EquipmentRepairScenario.Create();long stock=Inventory.Balance(w,"player","iron-material");var e=new SimulationEngine(w);e.Submit(Repair(w));e.AdvanceTo(600);e.InterruptEquipmentRepair("player");
            var stopped=e.Snapshot();Check(Inventory.Balance(stopped,"player","iron-material")==stock&&stopped.Time!.EquipmentRepair!.Receipts.Single().Completed==false,"Refund failed");
            Check(stopped.Time!.Charges.Single(c=>c.ActionId=="repair-test").Cost==3,"AP refunded");e.AdvanceTo(1800);Check(EquipmentCondition.Remaining(e.Snapshot(),Gear(w))==20,"Stale completion applied");
        }),
        ("R8 interrupted commission refunds fee and prevents simultaneous smith work",()=>{
            var w=EquipmentRepairScenario.Create();var owner=w.Characters.Single(a=>a.Id=="player");int n=w.Characters.FindIndex(a=>a.TestProfession==TestProfession.Smith);var smith=w.Characters[n];
            w.Characters[n]=smith with{RegionId=owner.RegionId,DemoLocationId=owner.DemoLocationId,HexPosition=owner.HexPosition,Health=100};
            long money=Inventory.Balance(w,"player","spirit-stone"),stock=Inventory.Balance(w,"player","iron-material");var e=new SimulationEngine(w);e.Submit(Repair(w,smith:smith.Id));e.AdvanceTo(0);
            var started=e.Snapshot();Check(ActionValidator.Validate(started,new("provider-craft",smith.Id,smith.Id,"iron-sword",1,0,DecisionSource.Npc,ActionKind.CraftLifeItem)) is not null,"Smith started concurrent work");
            e.InterruptEquipmentRepair(smith.Id);var after=e.Snapshot();Check(Inventory.Balance(after,"player","spirit-stone")==money&&Inventory.Balance(after,"player","iron-material")==stock&&!EquipmentRepairActions.Busy(after,smith.Id),"Commission not fully refunded");
        }),
        ("R8 attack interrupts repair through battle integration",()=>{
            var w=EquipmentRepairScenario.Create();var owner=w.Characters.Single(a=>a.Id=="player");int n=w.Characters.FindIndex(a=>a.Id=="npc:00");w.Characters[n]=w.Characters[n] with{Hostile=true,Health=100,RegionId=owner.RegionId,DemoLocationId=owner.DemoLocationId,HexPosition=owner.HexPosition};
            var e=new SimulationEngine(w);e.Submit(Repair(w));e.AdvanceTo(0);e.Submit(new("repair-ambush","npc:00","player","combat",1,0,DecisionSource.Npc,ActionKind.Engage));e.AdvanceTo(0);
            var result=e.Snapshot();Check(result.Time!.EquipmentRepair!.Receipts.Single().Completed==false&&!EquipmentRepairActions.Locked(result,Gear(w))&&result.BoardBattle is not null,"Battle did not interrupt and release repair");
        }),
        ("R8 rejects missing materials nonowners equipped items and full durability",()=>{
            var w=EquipmentRepairScenario.Create();var i=Repair(w);Check(ActionValidator.Validate(w,i with{ActorId="npc:04",Source=DecisionSource.Npc}) is not null,"Nonowner accepted");
            int n=w.Characters.FindIndex(c=>c.Id=="player");w.Characters[n]=w.Characters[n] with{EquippedWeaponId=Gear(w)};Check(ActionValidator.Validate(w,i) is not null,"Equipped item accepted");
            w=EquipmentRepairScenario.Create();var e=new SimulationEngine(w);
            for(int j=0;j<4;j++){var result=e.ExecutePlayerActivity(Repair(e.Snapshot(),"repair-"+j));Check(result.Accepted,result.ToString());}
            Check(EquipmentCondition.Remaining(e.Snapshot(),Gear(w))==100&&ActionValidator.Validate(e.Snapshot(),Repair(e.Snapshot(),"full")) is not null,"Durability cap failed");
        }),
        ("R8 SQLite JSON preserve pending repair and deterministic continuation",()=>{
            var e=new SimulationEngine(EquipmentRepairScenario.Create());e.Submit(Repair(e.Snapshot()));e.AdvanceTo(600);var w=e.Snapshot();
            string dir=Path.Combine(Path.GetTempPath(),"r8-repair-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{
                foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                    string path=Path.Combine(dir,store.GetType().Name);store.Save(path,w);var loaded=store.Load(path);Check(WorldCodec.Digest(w)==WorldCodec.Digest(loaded),"Snapshot changed");
                    var a=new SimulationEngine(w);var b=new SimulationEngine(loaded);a.AdvanceTo(1800);b.AdvanceTo(1800);Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Continuation diverged");File.Delete(path);
                }
            }finally{foreach(string path in Directory.GetFiles(dir))File.Delete(path);Directory.Delete(dir);}
        }),
        ("R8 NPC autonomously repairs finite stock and new game resets repair receipts",()=>{
            var e=new SimulationEngine(EquipmentRepairScenario.Create());for(int step=0;step<30&&!e.Snapshot().Time!.EquipmentRepair!.Receipts.Any(r=>r.OwnerId=="npc:03"&&r.Completed);step++)e.Step(true);
            var w=e.Snapshot();Check(w.Time!.EquipmentRepair!.Receipts.Any(r=>r.OwnerId=="npc:03"&&r.Completed),"NPC never repaired");
            var fresh=NewGameFactory.CreateLike(w,"新局");Check(fresh.Time!.EquipmentRepair!.Receipts.Count==0&&EquipmentCondition.Remaining(fresh,Gear(fresh))==20,"New game retained repair state");
            Check(PlayerControlScenario.Create().Time!.EquipmentRepair is null,"Repair leaked to old mode");
        }),
        ("R8 broken crafted armor and weapon regain bonuses without replacing identity or quality",()=>{
            var w=PlayerControlScenario.Create();w.Time!.EquipmentRepair=new(){Rules=GameConfiguration.Find().Read<RepairRules>("items/repair.json")};
            w.EquipmentDurability!.Rules=w.EquipmentDurability.Rules with{Maximum=1};
            foreach(var recipe in w.LifeSystem!.Recipes.Where(r=>r.Slot is not null))foreach(var material in recipe.Inputs)
                w.Ledger.Add(new(w.NextSequence++,0,"repair-crafted-fixture","$genesis","player",material.Key,material.Value+w.Time.EquipmentRepair.Rules.MaterialQuantity,"Genesis"));
            var e=new SimulationEngine(w);
            void Act(ActionKind kind,string resource){var s=e.Snapshot();var intent=new ActionIntent("armor-setup:"+s.NextSequence,"player","player",resource,1,0,DecisionSource.Player,kind);Check(ActionValidator.Validate(s,intent) is null,"Setup rejected "+kind+":"+ActionValidator.Validate(s,intent));e.Submit(intent);e.AdvanceTo(e.WorldTimeSeconds!.Value+(kind==ActionKind.CraftLifeItem?2400L:600L));}
            Act(ActionKind.CraftLifeItem,"leather-armor");Act(ActionKind.CraftLifeItem,"iron-sword");w=e.Snapshot();
            var armor=w.LifeSystem!.Equipment.Single(g=>g.DefinitionId=="leather-armor");var weapon=w.LifeSystem.Equipment.Single(g=>g.DefinitionId=="iron-sword");
            Act(ActionKind.EquipCraftedItem,armor.Id);Act(ActionKind.EquipCraftedItem,weapon.Id);w=e.Snapshot();
            w.Characters=w.Characters.Select(a=>a.Id is "player" or "npc:00"?a with{BaseAttack=1,BaseDefense=100}:a).ToList();
            w.BoardRules=new(100);w.BoardBattle=new(){Id="repair-damage",Fighters=[new("player",BattleSide.Left,new(0,0),10,1),new("npc:00",BattleSide.Right,new(0,0),9,1)]};e=new(w);
            foreach(var (actor,target) in new[]{("player","npc:00"),("npc:00","player")})e.Submit(new("break:"+actor,actor,"repair-damage","board-turn",1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.BoardTurn,BoardTurn:new("repair-damage",1,AttackTargetId:target)));
            e.Step();w=e.Snapshot();Check(EquipmentCondition.Remaining(w,armor.Id)==0&&EquipmentCondition.Remaining(w,weapon.Id)==0&&w.Characters.Single(a=>a.Id=="player").EquipmentDefenseBonus==0,"Fixture did not break gear through combat");
            w.BoardBattle=null;w.BoardRules=null;e=new(w);Act(ActionKind.UnequipCraftedItem,armor.Id);Act(ActionKind.UnequipCraftedItem,weapon.Id);
            foreach(var gear in new[]{armor,weapon}){var s=e.Snapshot();var result=e.ExecutePlayerActivity(new("restore:"+gear.Id,"player","player",gear.Id,1,0,DecisionSource.Player,ActionKind.RepairEquipment));Check(result.Accepted,result.ToString());Act(ActionKind.EquipCraftedItem,gear.Id);}
            w=e.Snapshot();Check(w.LifeSystem!.Equipment.Contains(armor)&&w.LifeSystem.Equipment.Contains(weapon)&&w.Characters.Single(a=>a.Id=="player").EquipmentDefenseBonus>0&&w.Characters.Single(a=>a.Id=="player").EquipmentAttackBonus>0,"Repair failed to restore original gear effects");
        }),
        ("R8 forged durability or repair receipt is rejected",()=>{
            var w=EquipmentRepairScenario.Create();w.EquipmentDurability!.Remaining[Gear(w)]=99;bool rejected=false;try{WorldIntegrity.Validate(w);}catch(InvalidDataException){rejected=true;}Check(rejected,"Forged durability accepted");
        })
    };
}
