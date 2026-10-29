using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;
namespace Cultivation.Presentation;
public partial class Main
{
    private void LifeSkillsSmokeTest()
    {
        try
        {
            CreateLifeSkillWorld();var w=engine.Snapshot();w.PlayerAutoSchedule=false;engine=new(w);Render();
            if(w.LifeSystem is null||!lifeSkills.DisplayText.Contains("製藥Lv.0"))throw new InvalidOperationException("Life scenario missing");
            long initialOintment=Inventory.Balance(w,"player","ointment");
            lifeSkills.SelectRecipe("ointment");lifeSkills.Command(ActionKind.CraftLifeItem);for(int n=0;n<3;n++)engine.Step();Render();
            if(Inventory.Balance(engine.Snapshot(),"player","ointment")!=initialOintment+1)throw new InvalidOperationException("Pharmacy UI failed");
            lifeSkills.SelectRecipe("iron-sword");lifeSkills.Command(ActionKind.CraftLifeItem);for(int n=0;n<4;n++)engine.Step();Render();
            var gear=engine.Snapshot().LifeSystem!.Equipment.Single();lifeSkills.SelectEquipment(gear.Id);lifeSkills.Command(ActionKind.EquipCraftedItem);engine.Step();Render();
            if(engine.Snapshot().Characters.Single(c=>c.Id=="player").CraftedWeaponId!=gear.Id)throw new InvalidOperationException("Equip UI failed");
            w=engine.Snapshot();w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {Health=0,SeverelyInjured=true}:c).ToList();engine=new(w);Render();
            lifeSkills.Command(ActionKind.TreatSevereInjury);for(int n=0;n<5;n++)engine.Step();Render();
            if(engine.Snapshot().Characters.Single(c=>c.Id=="player") is not {Health:20,SeverelyInjured:false})throw new InvalidOperationException("Severe medical UI failed");
            string digest=WorldCodec.Digest(engine.Snapshot());Save();Load();Render();
            if(WorldCodec.Digest(engine.Snapshot())!=digest||!characterAttributes.DisplayText.Contains("製藥：Lv.0｜熟練 10")||!characterJournal.DisplayText.Contains("開始觀察"))throw new InvalidOperationException("Life save or attributes failed");
            GD.Print("LIFE_SKILLS_UI_SMOKE_PASS: scenario, severe treatment, pharmacy, smithing, quality equipment, equip, attributes, journal, save/load");GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr("LIFE_SKILLS_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
}
