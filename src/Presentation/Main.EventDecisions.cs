using Cultivation.Simulation;
using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private void CreateEventDecisionWorld()
    {
        CreateActionLifecycleWorld();var world=engine.Snapshot();EventDecisions.EnableFresh(world);
        engine=new(world);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);paused=true;Render();
        notice.Text="R3事件決策測試：完成／休息／通知喚醒、去重與閒置重試；市場觀察先限制本地公開資訊，其餘觀察仍待逐項審查。";
    }
    private void EventDecisionSmokeTest()
    {
        try
        {
            CreateEventDecisionWorld();engine.SetPlayerAutonomy(true);engine.AdvanceTo(600,true);Render();
            var world=engine.Snapshot();
            if(world.Time!.EventDecisions!.Evaluations==0||!status.Text.Contains("R3待決策"))throw new Exception("Missing R3 evaluations/UI");
            engine=new(WorldCodec.Clone(world));engine.Step(true);Render();WorldIntegrity.Validate(engine.Snapshot());
            GD.Print("EVENT_DECISIONS_UI_SMOKE_PASS: explicit mode, targeted evaluation, counters, snapshot continuation");GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr("EVENT_DECISIONS_UI_SMOKE_FAIL: "+error);GetTree().Quit(1);}
    }
}
