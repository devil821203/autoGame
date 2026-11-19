using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class PlayerControlTests
{
    private const string Other="npc:method-trainee";
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static ActionIntent Train(string id,string actor="player",DecisionSource source=DecisionSource.Player,long count=1)=>new(id,actor,actor,"method:QingyunFist",count,0,source,ActionKind.TrainMortalAttribute);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R6b scenario grants existing characters without copying money or characters",()=>{
            var a=RegionalWorldScenario.Create();var b=PlayerControlScenario.Create();
            Check(a.Characters.Count==b.Characters.Count&&JsonSerializer.Serialize(a.Ledger)==JsonSerializer.Serialize(b.Ledger)&&a.RandomState==b.RandomState,"Grant created assets or characters");
            Check(b.Time!.PlayerControl!.Grants.Count==2&&PlayerControl.CanSelect(b,"player")&&PlayerControl.CanSelect(b,Other)&&!PlayerControl.HasGrant(b,"npc:03"),"Incorrect initial control eligibility");WorldIntegrity.Validate(b);
        }),
        ("R6b switch requires pause eligibility and preserves all assets AP RNG time",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());var before=e.Snapshot();string digest=WorldCodec.Digest(before);
            Check(!e.SwitchControlledCharacter(Other,false).Accepted&&!e.SwitchControlledCharacter("npc:03",true).Accepted&&WorldCodec.Digest(e.Snapshot())==digest,"Rejected switch mutated state");
            e.SetPlayerAutonomy(true);before=e.Snapshot();Check(e.SwitchControlledCharacter(Other,true).Accepted,"Qualified switch rejected");var w=e.Snapshot();
            Check(w.ControlledCharacterId==Other&&!w.PlayerAutoSchedule&&w.Clock==before.Clock&&w.RandomState==before.RandomState&&JsonSerializer.Serialize(w.Characters)==JsonSerializer.Serialize(before.Characters)&&JsonSerializer.Serialize(w.Ledger)==JsonSerializer.Serialize(before.Ledger)&&JsonSerializer.Serialize(w.Time!.Charges)==JsonSerializer.Serialize(before.Time!.Charges),"Switch moved state or charged cost");
            Check(ActionValidator.Validate(w,Train("old")) is not null&&ActionValidator.Validate(w,Train("new",Other)) is null,"Control boundary not updated");
        }),
        ("R6b pending former player commands are rejected after handoff",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());e.Submit(Train("old-pending"));Check(e.SwitchControlledCharacter(Other,true).Accepted,"Switch rejected");e.AdvanceTo(600);
            var w=e.Snapshot();Check(!w.Time!.Charges.Any(c=>c.ActionId=="old-pending")&&w.Events.Any(x=>x.ActionId=="old-pending"&&x.Kind=="Rejected"),"Pending command retained old authority");
        }),
        ("R6b started training remains with original character and AI resumes",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());e.Submit(Train("original-batch",count:6));e.AdvanceTo(600);var before=e.Snapshot();
            Check(e.SwitchControlledCharacter(Other,true).Accepted,"Busy original cannot hand off");var switched=e.Snapshot();
            Check(switched.MortalTrainingWork.Single().Intent.ActorId=="player"&&switched.MortalTrainingWork.Single().RemainingIterations==before.MortalTrainingWork.Single().RemainingIterations,"Work transferred/reset");
            e.AdvanceTo(3600);var w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions["original-batch"].Phase==ActionPhase.Completed&&w.Methods!.Learners["player"]["QingyunFist"]==6&&w.Methods.Learners[Other]["QingyunFist"]==0,"Original reward changed owner");
            e.AdvanceTo(4200,true);w=e.Snapshot();
            // Existing FIFO budget may first drain the fresh world's startup requests.
            for(int n=0;n<=w.Characters.Count/w.Rules.DecisionBudget+1&&!w.Time!.Charges.Any(c=>c.ActorId=="player"&&c.ActionId!="original-batch");n++){e.Step(true);w=e.Snapshot();}
            Check(w.Time!.Charges.Any(c=>c.ActorId=="player"&&c.ActionId!="original-batch"),"Old character never resumed AI activity: "+JsonSerializer.Serialize(w.Time.EventDecisions!.Traces.GetValueOrDefault("player")));
        }),
        ("R6b selecting busy target preserves accepted NPC work and blocks new commands",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());e.Submit(Train("npc-started",Other,DecisionSource.Npc,2));e.AdvanceTo(0);Check(e.SwitchControlledCharacter(Other,true).Accepted,"Busy target cannot be selected");
            Check(!e.ExecutePlayerActivity(Train("double-work",Other)).Accepted,"Started overlapping work");e.AdvanceTo(1200);
            Check(e.Snapshot().Time!.ActionRuntime!.Actions["npc-started"].Phase==ActionPhase.Completed&&e.Snapshot().Methods!.Learners[Other]["QingyunFist"]==2,"Accepted NPC work rejected after switch");
        }),
        ("R6b accepted deferred medicine still completes for former controller",()=>{
            var w=PlayerControlScenario.Create();w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Health=50}:a).ToList();w.Ledger.Add(new(w.NextSequence++,0,"control-medicine-fixture","$genesis","player","ointment",1,"Genesis"));
            var e=new SimulationEngine(w);long before=Inventory.Balance(w,"player","ointment");var use=new ActionIntent("accepted-medicine","player","player","ointment",1,0,DecisionSource.Player,ActionKind.UseMedicine);
            e.Submit(use);e.AdvanceTo(0);Check(e.Snapshot().Time!.Deferred.Count==1,"No deferred medicine fixture");Check(e.SwitchControlledCharacter(Other,true).Accepted,"Switch rejected");e.AdvanceTo(600);w=e.Snapshot();
            Check(Inventory.Balance(w,"player","ointment")==before-1&&w.Characters.Single(a=>a.Id=="player").Health>50&&w.Time!.Deferred.Count==0&&!w.Events.Any(x=>x.ActionId==use.Id&&x.Kind=="TimedCompletionRejected"),"Accepted completion lost permission");
        }),
        ("R6b revoke is idempotent stops time and permits manual eligible replacement",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());e.SetPlayerAutonomy(true);Check(e.RevokeControlGrant("player","fixture revoke"),"Revoke failed");string digest=WorldCodec.Digest(e.Snapshot());
            Check(!e.RevokeControlGrant("player","again")&&WorldCodec.Digest(e.Snapshot())==digest,"Repeated revoke changed audit");
            var stopped=e.AdvanceTo(3600,true);Check(stopped.Reason==AdvanceStopReason.ControlRequired&&stopped.WorldTime==0&&e.RequiresControlSelection&&!e.Snapshot().PlayerAutoSchedule,"Revoked controller advanced time");
            Check(!e.ExecutePlayerActivity(Train("revoked-action")).Accepted,"Revoked player action accepted");Check(e.SwitchControlledCharacter(Other,true).Accepted&&!e.RequiresControlSelection,"Manual replacement failed");
        }),
        ("R6b no eligible characters remains paused with retained observer identity",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());e.RevokeControlGrant("player","none");e.RevokeControlGrant(Other,"none");e.Step(true);var w=e.Snapshot();
            Check(e.RequiresControlSelection&&w.ControlledCharacterId=="player"&&w.Clock.Tick==0&&!w.Characters.Any(a=>PlayerControl.CanSelect(w,a.Id)),"Invalid no-control state");
            Check(!e.SwitchControlledCharacter(Other,true).Accepted,"Selected revoked character");try{e.SetPlayerAutonomy(true);throw new Exception("Enabled auto without controller");}catch(InvalidOperationException){}WorldIntegrity.Validate(w);
        }),
        ("R6b zero health outside combat requests selection without inventing death",()=>{
            var w=PlayerControlScenario.Create();w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Health=0}:a).ToList();var e=new SimulationEngine(w);
            Check(e.AdvanceTo(600).Reason==AdvanceStopReason.ControlRequired&&e.SwitchControlledCharacter(Other,true).Accepted&&e.Snapshot().Characters.Any(a=>a.Id=="player"&&a.Health==0),"Incapacitation control handling failed");
        }),
        ("R6b active battle forbids switching either combatant",()=>{
            var w=PlayerControlScenario.Create();var player=w.Characters.Single(a=>a.Id=="player");w.Characters=w.Characters.Select(a=>a.Id==Other?a with {Hostile=true,DemoLocationId=player.DemoLocationId,HexPosition=player.HexPosition}:a).ToList();
            var e=new SimulationEngine(w);e.Submit(new("control-battle",Other,"player","combat",1,0,DecisionSource.Npc,ActionKind.Engage));e.AdvanceTo(0);var before=e.Snapshot();Check(before.BoardBattle?.Phase==BoardBattlePhase.Active,"No battle fixture");
            Check(!e.SwitchControlledCharacter(Other,true).Accepted&&WorldCodec.Digest(before)==WorldCodec.Digest(e.Snapshot()),"Switched active battle");
        }),
        ("R6b JSON SQLite retain grants revocation work and switch history",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());e.Submit(Train("persisted-work",count:3));e.AdvanceTo(600);e.SwitchControlledCharacter(Other,true);e.RevokeControlGrant("player","persist revoked");var snapshot=e.Snapshot();
            foreach(string extension in new[]{"json","sqlite"}){
                string path=Path.Combine(Path.GetTempPath(),"r6b-"+Guid.NewGuid()+"."+extension);
                try{var store=WorldStores.ForPath(path);store.Save(path,snapshot);var copy=store.Load(path);Check(WorldCodec.Digest(snapshot)==WorldCodec.Digest(copy),"Grant snapshot changed");var a=new SimulationEngine(WorldCodec.Clone(snapshot));var b=new SimulationEngine(copy);a.AdvanceTo(1800);b.AdvanceTo(1800);Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Controlled work continuation differs");
                    if(extension=="json"){var node=JsonNode.Parse(File.ReadAllText(path))!;Check(node["SchemaVersion"]!.GetValue<int>()==10,"Missing control format guard");node["SchemaVersion"]=9;File.WriteAllText(path,node.ToJsonString());try{store.Load(path);throw new Exception("Old format hid grants");}catch(InvalidDataException){}}
                }finally{if(File.Exists(path))File.Delete(path);}
            }
        }),
        ("R6b forged controlled ID or source history is rejected",()=>{
            var w=PlayerControlScenario.Create();w.ControlledCharacterId=Other;try{WorldIntegrity.Validate(w);throw new Exception("Forged control accepted");}catch(InvalidDataException){}
            w=PlayerControlScenario.Create();var grant=w.Time!.PlayerControl!.Grants[Other];w.Time.PlayerControl.Grants[Other]=grant with {EligibilityReason="sect-member"};try{WorldIntegrity.Validate(w);throw new Exception("Relationship granted control");}catch(InvalidDataException){}
        }),
        ("R6b new game resets qualification history but legacy worlds remain legacy",()=>{
            var e=new SimulationEngine(PlayerControlScenario.Create());e.SwitchControlledCharacter(Other,true);e.RevokeControlGrant("player","reset");var fresh=NewGameFactory.CreateLike(e.Snapshot(),"新角色");
            Check(fresh.ControlledCharacterId=="player"&&fresh.Time!.PlayerControl!.Changes.Count==2&&fresh.Time.PlayerControl.Grants.Values.All(g=>g.RevokedAt is null)&&fresh.Time.Charges.Count==0,"New game copied revoked grants/work");
            var old=RegionalWorldScenario.Create();Check(old.Time!.PlayerControl is null&&NewGameFactory.CreateLike(old,"舊模式").Time!.PlayerControl is null,"Legacy auto-converted");
        })
    };
}
