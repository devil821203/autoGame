using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private void CreateActionLifecycleWorld()
    {
        CreateDiscreteEventWorld();var world=engine.Snapshot();ActionLifecycle.EnableFresh(world);
        engine=new(world);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);paused=true;Render();
        notice.Text="R2行動／預留測試：共用生命週期、修練／生活／旅行轉接；舊檔保留原模式，未啟用新設施容量玩法。";
    }
    private void ActionLifecycleSmokeTest()
    {
        try
        {
            CreateActionLifecycleWorld();
            engine.Submit(new("r2-ui","player","player","method:QingyunFist",6,0,DecisionSource.Player,ActionKind.TrainMortalAttribute));
            engine.AdvanceTo(600);Render();var world=engine.Snapshot();
            var action=world.Time!.ActionRuntime!.Actions["r2-ui"];
            if(action.Phase!=ActionPhase.Running||action.CompletedSegments!=1||!status.Text.Contains("R2行動 1"))throw new Exception("R2 live action UI wrong");
            engine=new(WorldCodec.Clone(world));engine.AdvanceTo(3600);Render();
            world=engine.Snapshot();
            if(world.Time!.ActionRuntime!.Actions["r2-ui"].Phase!=ActionPhase.Completed||!MortalCultivationPanel.Describe(world,world.Characters.Single(a=>a.Id=="player")).Contains("R2"))throw new Exception("R2 completion/query wrong");
            GD.Print("ACTION_LIFECYCLE_UI_SMOKE_PASS: explicit mode, real checkpoint, AP, snapshot continuation, terminal query");GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr("ACTION_LIFECYCLE_UI_SMOKE_FAIL: "+error);GetTree().Quit(1);}
    }
}
