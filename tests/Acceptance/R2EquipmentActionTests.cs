using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class R2EquipmentActionTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static (SimulationEngine, string) Crafted(){
        var e=new SimulationEngine(LifeEventScenario.Create());var craft=new ActionIntent("r2-craft-setup","player","player","iron-sword",1,0,DecisionSource.Player,ActionKind.CraftLifeItem);
        var result=e.ExecutePlayerActivity(craft);Check(result.Accepted,"Craft setup failed: "+result.Reason);return (e,e.Snapshot().LifeSystem!.Equipment.Single(g=>g.ActionId==craft.Id).Id);
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 crafted equipment changes complete once and survive storage",()=>{
            var (e,gear)=Crafted();var equip=new ActionIntent("r2-equip","player","player",gear,1,0,DecisionSource.Player,ActionKind.EquipCraftedItem);
            e.Submit(equip);e.AdvanceTo(e.WorldTimeSeconds!.Value);var w=e.Snapshot();var action=w.Time!.ActionRuntime!.Actions[equip.Id];Check(action.Phase==ActionPhase.Running&&w.Characters.Single(c=>c.Id=="player").CraftedWeaponId is null,"Equipped before deadline");
            string dir=Path.Combine(Path.GetTempPath(),"r2-equip-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){string p=Path.Combine(dir,store.GetType().Name);store.Save(p,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(p));a.AdvanceTo(action.DueAt);b.AdvanceTo(action.DueAt);var done=b.Snapshot();Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(done)&&done.Time!.ActionRuntime!.Actions[equip.Id].Phase==ActionPhase.Completed&&done.Characters.Single(c=>c.Id=="player").CraftedWeaponId==gear,"Equipment continuation failed");}}
            finally{foreach(string p in Directory.GetFiles(dir))File.Delete(p);Directory.Delete(dir);}
            e.AdvanceTo(action.DueAt);var unequip=equip with {Id="r2-unequip",Kind=ActionKind.UnequipCraftedItem};e.Submit(unequip);e.AdvanceTo(e.WorldTimeSeconds!.Value);w=e.Snapshot();long due=w.Time!.ActionRuntime!.Actions[unequip.Id].DueAt;e.AdvanceTo(due);w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions[unequip.Id].Phase==ActionPhase.Completed&&w.Characters.Single(c=>c.Id=="player").CraftedWeaponId is null,"Unequip failed");
        }),
        ("R2 legacy weapon equip and unequip use shared action",()=>{
            var w=LifeEventScenario.Create();string item=w.Advancement!.WeaponId;if(Inventory.Balance(w,"player",item)==0)w.Ledger.Add(new(w.NextSequence++,0,"legacy-test","$genesis","player",item,1,"Genesis"));var e=new SimulationEngine(w);
            var equip=new ActionIntent("r2-legacy-equip","player","player",item,1,0,DecisionSource.Player,ActionKind.EquipWeapon);e.Submit(equip);e.AdvanceTo(0);w=e.Snapshot();long due=w.Time!.ActionRuntime!.Actions[equip.Id].DueAt;e.AdvanceTo(due);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[equip.Id].Phase==ActionPhase.Completed&&w.Characters.Single(c=>c.Id=="player").EquippedWeaponId is not null,"Equip failed");
            var unequip=equip with {Id="r2-legacy-unequip",Kind=ActionKind.UnequipWeapon};e.Submit(unequip);e.AdvanceTo(due);w=e.Snapshot();e.AdvanceTo(w.Time!.ActionRuntime!.Actions[unequip.Id].DueAt);w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions[unequip.Id].Phase==ActionPhase.Completed&&w.Characters.Single(c=>c.Id=="player").EquippedWeaponId is null,"Unequip failed");
        }),
        ("R2 equipment change fails if item ownership changes",()=>{
            var (e,gear)=Crafted();var i=new ActionIntent("r2-equip-fail","player","player",gear,1,0,DecisionSource.Player,ActionKind.EquipCraftedItem);e.Submit(i);e.AdvanceTo(e.WorldTimeSeconds!.Value);var w=e.Snapshot();var a=w.Time!.ActionRuntime!.Actions[i.Id];
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"equipment-fixture","player","sect",gear,1,"FixtureTransfer"));e=new SimulationEngine(w);e.AdvanceTo(a.DueAt);w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Failed&&w.Characters.Single(c=>c.Id=="player").CraftedWeaponId is null&&w.Time.Charges.Count(c=>c.ActionId==i.Id)==1,"Failed equip changed gear or AP");
        })
    };
}
