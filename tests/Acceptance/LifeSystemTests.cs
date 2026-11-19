using Cultivation.Domain;
using Cultivation.Simulation;

internal static class LifeSystemTests
{
    static void Check(bool ok,string msg){if(!ok)throw new InvalidOperationException(msg);}
    static WorldState World(){var w=LifeSkillScenario.Create();w.PlayerAutoSchedule=false;return w;}
    static ActionIntent I(string id,ActionKind kind,string item,string target="player",string actor="player")=>new(id,actor,target,item,1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,kind);
    static void Steps(SimulationEngine e,int n){for(int k=0;k<n;k++)e.Step(false);}
    static void Begin(SimulationEngine e,ActionIntent i){e.Submit(i);var r=e.Step(false);Check(r.Single(x=>x.IntentId==i.Id).Accepted,"Rejected "+i.Kind+": "+r.Single(x=>x.IntentId==i.Id).Reason);}
    static void Stock(WorldState w,string owner,string item,long qty)=>w.Ledger.Add(new(w.NextSequence++,0,"test-stock:"+w.NextSequence,"$genesis",owner,item,qty,"Genesis"));
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Life pharmacy reserves herbs and completes exactly on third tick once",()=>{
            var w=World();long herbs=Inventory.Balance(w,"player","herb");var e=new SimulationEngine(w);
            Begin(e,I("ointment",ActionKind.CraftLifeItem,"ointment"));
            Check(Inventory.Balance(e.Snapshot(),"player","herb")==herbs-2&&Inventory.Balance(e.Snapshot(),"player","ointment")==0,"Reservation or early production");
            e.Submit(I("busy",ActionKind.CraftLifeItem,"ointment"));Check(!e.Step().Single().Accepted,"Busy craft accepted");
            e.Step();Check(Inventory.Balance(e.Snapshot(),"player","ointment")==1&&e.Snapshot().LifeSystem!.Work.Count==0,"Completion timing");
            e.Submit(I("ointment",ActionKind.CraftLifeItem,"ointment"));e.Step();
            Check(Inventory.Balance(e.Snapshot(),"player","ointment")==1&&e.Snapshot().LifeSystem!.Receipts.Count==1,"Duplicate crafting");
            Check(e.Snapshot().Characters.Single(c=>c.Id=="player").LifeSkills!.PharmacyExperience==10,"Pharmacy skill missing");
        }),
        ("Life medicine heals other present actors with level bonus and rejects invalid patients",()=>{
            var w=World();var p=w.Characters.Single(c=>c.Id=="player");
            w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {LifeSkills=new(100)}:c.Id=="npc:00"?c with {Health=40,DemoLocationId=p.DemoLocationId,HexPosition=p.HexPosition}:c).ToList();
            var e=new SimulationEngine(w);Begin(e,I("medical",ActionKind.MedicalTreatment,"herb","npc:00"));e.Step();
            Check(e.Snapshot().Characters.Single(c=>c.Id=="npc:00").Health==61,"Medical bonus missing");
            Check(e.Snapshot().Characters.Single(c=>c.Id=="player").LifeSkills!.MedicineExperience==110,"Medical experience missing");
            Check(ActionValidator.Validate(e.Snapshot(),I("full",ActionKind.MedicalTreatment,"herb")) is not null,"Full health treatment allowed");
            Check(ActionValidator.Validate(e.Snapshot(),I("spoof",ActionKind.MedicalTreatment,"herb","player","npc:00") with {Source=DecisionSource.Player}) is not null,"Cross-control accepted");
        }),
        ("Life independent severe injury course consumes confirmed materials and enables recovery",()=>{
            var w=World();Stock(w,"player","ointment",1);w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {Health=0,SeverelyInjured=true}:c).ToList();
            long herbs=Inventory.Balance(w,"player","herb");var e=new SimulationEngine(w);
            Begin(e,I("severe",ActionKind.TreatSevereInjury,"severe-injury"));
            Check(e.Snapshot().Characters.Single(c=>c.Id=="player").SeverelyInjured,"Injury removed early");
            var resumed=new SimulationEngine(e.Snapshot());for(int n=0;n<4;n++){e.Step();resumed.Step();}
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Severe course resume diverged");
            var a=e.Snapshot().Characters.Single(c=>c.Id=="player");
            Check(!a.SeverelyInjured&&a.Health==20&&Inventory.Balance(e.Snapshot(),"player","herb")==herbs-3&&Inventory.Balance(e.Snapshot(),"player","ointment")==0,"Severe course output/cost");
            Check(e.Snapshot().Time!.Budgets.Single(b=>b.ActorId=="player").Remaining==25,"Severe course AP");
        }),
        ("Life normal healing and rest never clear severe injury",()=>{
            var w=World();w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {Health=50,SeverelyInjured=true}:c).ToList();Stock(w,"player","ointment",1);
            var e=new SimulationEngine(w);Begin(e,I("normal",ActionKind.MedicalTreatment,"herb"));e.Step();
            Check(e.Snapshot().Characters.Single(c=>c.Id=="player") is {Health:70,SeverelyInjured:true},"Ordinary medicine removed severe injury");
            Begin(e,I("ointment-use",ActionKind.UseMedicine,"ointment"));Check(e.Snapshot().Characters.Single(c=>c.Id=="player") is {Health:100,SeverelyInjured:true},"Ointment removed severe injury");
        }),
        ("Life forged quality equipment changes core stats and requires unequip before transfer",()=>{
            var w=World();w.LifeSystem!.Options=w.LifeSystem.Options with {FineChance=0,SuperiorChance=1,QualityChancePerLevel=0};var e=new SimulationEngine(w);
            Begin(e,I("sword",ActionKind.CraftLifeItem,"iron-sword"));Steps(e,3);var g=e.Snapshot().LifeSystem!.Equipment.Single();
            Check(g.Quality==EquipmentQuality.Superior&&g.Attack==9,"Quality stats incorrect");
            var before=CharacterProgression.Stats(e.Snapshot().Characters.Single(c=>c.Id=="player"),w.Advancement);
            Begin(e,I("equip",ActionKind.EquipCraftedItem,g.Id));var a=e.Snapshot().Characters.Single(c=>c.Id=="player");
            Check(CharacterProgression.Stats(a,w.Advancement).Attack==before.Attack+9,"Equipment not applied to core stats");
            Check(ActionValidator.Validate(e.Snapshot(),I("transfer",ActionKind.Transfer,g.Id,"npc:00")) is not null,"Equipped weapon transferred");
            Begin(e,I("unequip",ActionKind.UnequipCraftedItem,g.Id));Begin(e,I("transfer-after",ActionKind.Transfer,g.Id,"npc:00"));
            Check(Inventory.Balance(e.Snapshot(),"npc:00",g.Id)==1&&Inventory.Balance(e.Snapshot(),"player",g.Id)==0,"Equipment duplicated");
            Begin(e,I("armor",ActionKind.CraftLifeItem,"leather-armor"));Steps(e,3);var armor=e.Snapshot().LifeSystem!.Equipment.Single(x=>x.Slot=="armor");
            Begin(e,I("armor-equip",ActionKind.EquipCraftedItem,armor.Id));
            Check(e.Snapshot().Characters.Single(c=>c.Id=="player").EquippedArmorId==armor.Id,"Armor slot missing");
        }),
        ("Life medicine effects freeze with world and continue remaining ticks after combat once",()=>{
            var w=World();Stock(w,"player","bleeding-powder",2);w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {Health=20}:c).ToList();
            w.BoardRules=new(100);w.BoardBattle=new(){Id="medicine-board",Fighters=[new("player",BattleSide.Left,new(0,0),10,1),new("npc:00",BattleSide.Right,new(0,0),10,1)]};
            var e=new SimulationEngine(w);Begin(e,I("powder",ActionKind.UseCombatMedicine,"bleeding-powder"));
            Check(e.Snapshot().Clock.Tick==0&&e.Snapshot().Characters.Single(c=>c.Id=="player").Health==30,"First round medicine timing");
            e.Submit(I("stack",ActionKind.UseCombatMedicine,"bleeding-powder"));Check(!e.Step().Single().Accepted,"Medicine stacking allowed");
            Check(Inventory.Balance(e.Snapshot(),"player","bleeding-powder")==1,"Rejected stack consumed medicine");
            w=e.Snapshot();w.Characters=w.Characters.Select(c=>c.Id=="npc:00"?c with {Health=0}:c).ToList();e=new(w);var resumed=new SimulationEngine(w);
            e.Step();resumed.Step();Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Effect resume duplicated");
            Check(e.Snapshot().Characters.Single(c=>c.Id=="player").Health==70&&e.Snapshot().Events.Count(v=>v.Kind=="MedicineEffectTick")==5&&e.Snapshot().LifeSystem!.Effects.Count==0,"Medicine did not total five ticks");
            Check(e.Snapshot().Clock.Tick==5,"World catchup changed by medicine rounds");
            Check(e.Snapshot().Characters.Single(c=>c.Id=="npc:00").SeverelyInjured,"Battle incapacity not marked severe");
        }),
        ("Life NPC independently chooses available medical work using shared rules",()=>{
            var w=World();var player=w.Characters.Single(c=>c.Id=="player");Stock(w,"npc:00","herb",2);
            w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {Health=40}:c.Id=="npc:00"?c with {Health=100,SocialWeight=100,DemoLocationId=player.DemoLocationId,HexPosition=player.HexPosition}:c with {SocialWeight=0}).ToList();
            var e=new SimulationEngine(w);e.Step(true);e.Step(true);
            Check(e.Snapshot().Events.Any(v=>v.Kind=="MedicalTreatmentCompleted"&&v.Detail.StartsWith("npc:00->player:")),"NPC never chose medicine");
            Check(e.Snapshot().Characters.Single(c=>c.Id=="npc:00").LifeSkills!.MedicineExperience>0,"NPC medicine bypassed skill accounting");
        }),        ("Life advanced quality chance increases while preserving deterministic saves",()=>{
            var e=new SimulationEngine(World());Begin(e,I("ongoing",ActionKind.CraftLifeItem,"leather-armor"));var resumed=new SimulationEngine(e.Snapshot());
            Steps(e,3);Steps(resumed,3);Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Quality rerolled on load");
            int lowSuperior=0,highSuperior=0;
            var seedSource=new SeededRandom(12345);
            for(int seed=1;seed<=12;seed++)
            {
                seedSource.NextUInt32();
                foreach(bool high in new[]{false,true})
                {
                    var trial=World();trial.RandomState=seedSource.State;
                    if(high)trial.Characters=trial.Characters.Select(c=>c.Id=="player"?c with {LifeSkills=new(0,0,10000)}:c).ToList();
                    var run=new SimulationEngine(trial);Begin(run,I("quality",ActionKind.CraftLifeItem,"iron-sword"));Steps(run,3);
                    if(run.Snapshot().LifeSystem!.Equipment.Single().Quality==EquipmentQuality.Superior){if(high)highSuperior++;else lowSuperior++;}
                }
            }
            Check(highSuperior>lowSuperior,"Higher smithing did not improve high-quality chance in paired seeds");
            var noResources=World();noResources.Ledger.RemoveAll(l=>l.To=="player"&&l.ResourceId=="iron-material");
            Check(ActionValidator.Validate(noResources,I("noiron",ActionKind.CraftLifeItem,"iron-sword")) is not null,"Missing finite iron accepted");
        })
    };
}
