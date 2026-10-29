using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;
namespace Cultivation.Presentation;
public partial class Main
{
    private void MedicineOnlySmokeTest()
    {
        try
        {
            CreateLifeSkillWorld();var w=engine.Snapshot();
            if(!w.MedicineOnly||w.Items.Any(i=>i.Id=="pill")||w.Ledger.Any(l=>l.ResourceId=="pill")||pillActions.Any(b=>!b.Disabled)||!status.Text.Contains("原耗丹行動已暫停"))throw new InvalidOperationException("New UI profile not applied");
            SubmitAction(ActionKind.Cultivate,"player","pill");if(engine.Step().Single().Accepted)throw new InvalidOperationException("Cultivation not paused");Render();
            if(engine.Snapshot().ProcurementQuotes.Single(q=>q.ResourceId=="herb").UnitPrice!=4||!lifeSkills.DisplayText.Contains("煉丹：停用"))throw new InvalidOperationException("Herb quote or disabled status wrong");
            string digest=WorldCodec.Digest(engine.Snapshot());Save();Load();Render();
            if(!engine.Snapshot().MedicineOnly||WorldCodec.Digest(engine.Snapshot())!=digest)throw new InvalidOperationException("Medicine profile not saved");
            engine=new(TimeScenario.Create());Render();digest=WorldCodec.Digest(engine.Snapshot());Save();Load();Render();
            if(engine.Snapshot().MedicineOnly||!engine.Snapshot().Items.Any(i=>i.Id=="pill")||WorldCodec.Digest(engine.Snapshot())!=digest)throw new InvalidOperationException("Legacy save was converted");
            CreateTimeWorld();if(!engine.Snapshot().MedicineOnly)throw new InvalidOperationException("New time world retained legacy rules");
            GD.Print("MEDICINE_ONLY_UI_SMOKE_PASS: every new profile hook, no pills, disabled actions, herb price4, save/load, legacy preservation");GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr("MEDICINE_ONLY_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
}
