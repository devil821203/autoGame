using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void CharacterAttributesSmokeTest()
    {
        try
        {
            CreateTimeWorld(); var w=engine.Snapshot(); w.PlayerAutoSchedule=false;
            w.Characters=w.Characters.Select(c=>c.Id=="player" ? c with {Health=70,BaseAttack=17,BaseDefense=9,WealthWeight=3.125}:c).ToList();
            engine=new(w); Render(); characterAttributes.PopupCentered();
            string text=characterAttributes.DisplayText;
            foreach(var label in new[]{"角色ID：player","生命：70","基礎攻擊：17","財富權重：3.125","體力狀態：100","目前生效數值","AP：30／30","目前庫存","精神力、靈力"})
                if(!text.Contains(label))throw new InvalidOperationException("Missing attribute: "+label);
            var stats=CharacterProgression.Stats(w.Characters.Single(c=>c.Id=="player"),w.Advancement);
            if(!text.Contains($"攻擊：{stats.Attack}｜防禦：{stats.Defense}"))throw new InvalidOperationException("Derived attributes differ from core");
            SubmitTracked(new("attribute-rest","player","player","stamina",1,0,DecisionSource.Player,ActionKind.Rest));
            if(!engine.Step().Single().Accepted)throw new InvalidOperationException("Rest rejected"); Render();
            if(!characterAttributes.DisplayText.Contains("生命：80")||!characterAttributes.DisplayText.Contains("AP：29／30"))throw new InvalidOperationException("Live state did not update");
            string digest=WorldCodec.Digest(engine.Snapshot());
            characterAttributes.SelectActor("npc:00"); Render();
            if(characterAttributes.SelectedActorId!="npc:00"||!characterAttributes.DisplayText.Contains("角色ID：npc:00")||engine.Snapshot().ControlledCharacterId!="player")throw new InvalidOperationException("Read-only NPC selection failed");
            characterAttributes.Hide(); Render(); characterAttributes.PopupCentered();
            if(characterAttributes.SelectedActorId!="npc:00"||WorldCodec.Digest(engine.Snapshot())!=digest)throw new InvalidOperationException("Observation changed world or lost selection");
            Save(); Load(); Render();
            if(characterAttributes.SelectedActorId!="player"||!characterAttributes.DisplayText.Contains("生命：80")||WorldCodec.Digest(engine.Snapshot())!=digest)throw new InvalidOperationException("Loaded attributes incorrect");
            engine=new(LifeSkillScenario.Create()); Render();
            foreach(var label in new[]{"醫術：Lv.0","製藥：Lv.0","鍛造：Lv.0","每級 100"})
                if(!characterAttributes.DisplayText.Contains(label))throw new InvalidOperationException("Missing life skill: "+label);
            CreatePlayerDemo();
            if(!characterAttributes.DisplayText.Contains("AP：此情境未啟用")||!characterAttributes.DisplayText.Contains("體力狀態：無／未啟用"))throw new InvalidOperationException("Legacy optional state displayed fake values");
                        characterAttributes.Hide();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print("CHARACTER_ATTRIBUTES_UI_SMOKE_PASS: complete current fields, derived stats, live HP/AP, read-only NPC selector, hidden/reopen, save/load, legacy unset values"); GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr("CHARACTER_ATTRIBUTES_UI_SMOKE_FAIL: "+error);GetTree().Quit(1);}
    }
}
