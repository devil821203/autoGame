using Cultivation.Domain;
using Cultivation.Simulation;

internal static class MedicineOnlyTests
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static WorldState World(){var w=MedicineOnlyProfile.ApplyFresh(LifeSkillScenario.Create());w.PlayerAutoSchedule=false;return w;}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Medicine-only new profiles remove pills across every playable test scenario",()=>{
            Func<WorldState>[] factories=[()=>DemoWorld.Create(),()=>AutonomousWorld.Create(),()=>PlayerDemoScenario.Create("藥師"),()=>MissionScenario.Create(),()=>AdvancementScenario.Create(),()=>CombatScenario.Create(),()=>SocialScenario.Create(),()=>SectEconomyScenario.Create(),()=>HexWorldScenario.Create(),()=>GrowthScenario.Create(),()=>PlayerAutoScheduleScenario.Create(),()=>TimeScenario.Create(),()=>LifeSkillScenario.Create(),()=>BoardBattleScenario.Create(),()=>ScaleScenario.Create(100)];
            foreach(var f in factories){var raw=f();var pills=raw.Ledger.Where(l=>l.ResourceId=="pill").Sum(l=>l.Quantity);var w=MedicineOnlyProfile.ApplyFresh(raw);
                Check(w.MedicineOnly&&!w.AlchemyEnabled&&!w.Items.Any(i=>i.Id=="pill")&&!w.Ledger.Any(l=>l.ResourceId=="pill")&&!w.MarketListings.Any(l=>l.ResourceId=="pill"),"Fresh test retained pills");
                Check(w.Ledger.Where(l=>l.ResourceId=="ointment").Sum(l=>l.Quantity)>=pills,"Initial medicine replaced incorrectly");
                Check(WorldCodec.Deserialize(WorldCodec.Serialize(w)).MedicineOnly,"Profile flag lost in save");}
        }),
        ("Medicine-only forbids former pill actions and leaves historical save snapshots intact",()=>{
            var w=World();foreach(var kind in new[]{ActionKind.Brew,ActionKind.SectBrew,ActionKind.Cultivate,ActionKind.Breakthrough,ActionKind.Heal,ActionKind.CombatHeal,ActionKind.PowerStrike}){
                var i=new ActionIntent("forbidden:"+kind,"player","player",kind is ActionKind.Brew or ActionKind.SectBrew?"herb":"pill",1,0,DecisionSource.Player,kind);
                Check(ActionValidator.Validate(w,i) is not null,"Forbidden action accepted "+kind);}
            var old=TimeScenario.Create();string digest=WorldCodec.Digest(old);var restored=WorldCodec.Deserialize(WorldCodec.Serialize(old));
            Check(!restored.MedicineOnly&&restored.AlchemyEnabled&&WorldCodec.Digest(old)==digest,"Legacy snapshot changed");
            var fresh=NewGameFactory.CreateLike(w,"新藥師");Check(fresh.MedicineOnly&&!fresh.Items.Any(i=>i.Id=="pill"),"New save re-enabled pills");
        }),
        ("Medicine-only buys and uses ointment and pays herbs through sect mission",()=>{
            var w=World();w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {Health=40}:c).ToList();var e=new SimulationEngine(w);
            long before=Inventory.Balance(w,"player","ointment");
            e.Submit(new("buy-med","player","market","ointment",1,0,DecisionSource.Player,ActionKind.Buy));Check(e.Step().Single().Accepted,"Medicine purchase rejected");
            Check(Inventory.Balance(e.Snapshot(),"player","ointment")==before+1,"Medicine purchase inventory wrong");
            e.Submit(new("use-med","player","player","ointment",1,0,DecisionSource.Player,ActionKind.UseMedicine));Check(e.Step().Single().Accepted,"Medicine use rejected");
            Check(e.Snapshot().Characters.Single(c=>c.Id=="player").Health==80,"Ointment heal wrong");
            w=World();w.SectDuty=w.SectDuty! with {MissionIntervalTicks=2};e=new(w);e.Step();e.Step();
            Check(e.Snapshot().Missions.Any(m=>m.ResourceId=="herb"&&m.UnitPrice==4)&&!e.Snapshot().Missions.Any(m=>m.ResourceId=="pill"),"Sect still solicits pills");
        }),
        ("Medicine-only bandits drop medicine instead of pills with valid loot ledger",()=>{
            var w=World();w.Dungeon=w.Dungeon! with {EncounterChance=1,WolfChance=0,BanditPillChance=1};
            w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {DemoLocationId="dungeon",HexPosition=w.HexMap!.Tiles.Single(t=>t.PlaceId=="dungeon").Coord,BaseAttack=100}:c).ToList();
            var e=new SimulationEngine(w);e.Submit(new("medicine-encounter","player","dungeon","combat",1,0,DecisionSource.Player,ActionKind.ExploreDungeon));Check(e.Step().Single().Accepted,"Explore rejected");
            w=e.Snapshot();w.BoardBattle!.PlayerAutoCombat=true;e=new(w);e.Step(true);
            Check(e.Snapshot().Ledger.Any(l=>l.ResourceId=="ointment"&&l.Reason=="DungeonSpawnLoot")&&!e.Snapshot().Ledger.Any(l=>l.ResourceId=="pill"),"Bandit produced pills");
            Check(e.Snapshot().Loot.Any(l=>l.ResourceId=="ointment"),"Medicine loot lost");
        }),
        ("Medicine-only legacy tick profiles complete medicine work without granting daily AP",()=>{
            var w=MedicineOnlyProfile.ApplyFresh(PlayerDemoScenario.Create("藥師"));w.PlayerAutoSchedule=false;w.Ledger.Add(new(w.NextSequence++,0,"test-herbs","$genesis","player","herb",2,"Genesis"));var e=new SimulationEngine(w);
            e.Submit(new("old-tick-med","player","player","ointment",1,0,DecisionSource.Player,ActionKind.CraftLifeItem));Check(e.Step().Single().Accepted,"Legacy tick pharmacy rejected");
            for(int n=0;n<3;n++)e.Step();Check(e.Snapshot().LifeSystem!.Receipts.Any(r=>r.ActionId=="old-tick-med")&&e.Snapshot().Time is null,"Legacy clock changed or craft stalled");
        })
    };
}
