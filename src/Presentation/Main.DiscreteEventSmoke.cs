using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private bool PauseOnEventStop()
    {
        if (engine.WorldTimeSeconds is null || engine.LastAdvance?.Reason is not
            (AdvanceStopReason.ImportantEvent or AdvanceStopReason.EventBudget or AdvanceStopReason.SameTimeBudget or AdvanceStopReason.ControlRequired)) return false;
        paused = true;
        notice.Text = "秒制推進已停止：" + engine.LastAdvance.Reason + "，" + engine.LastAdvance.Detail;
        return true;
    }
    private void DiscreteEventSmokeTest()
    {
        try
        {
            CreateDiscreteEventWorld();
            engine.ScheduleWorldSignal(1020, "seventeen", "17分鐘重要訊號", blocking: true);
            engine.ScheduleWorldSignal(1800, "thirty", "30分鐘訊號");
            engine.Submit(new("des-ui-batch", "player", "player", "method:QingyunFist", 6, 0, DecisionSource.Player, ActionKind.TrainMortalAttribute));
            var stop = engine.AdvanceTo(3600);
            if (stop.Reason != AdvanceStopReason.ImportantEvent || stop.WorldTime != 1020 || engine.WorldTimeSeconds != 1020)
                throw new Exception("Important event did not stop at exact second");
            engine = new(WorldCodec.Clone(engine.Snapshot()));
            engine.AdvanceTo(3600); Render();
            if (engine.Snapshot().Methods!.Learners["player"]["QingyunFist"] != 6 || !status.Text.Contains("事件時間 3600秒"))
                throw new Exception("Batch or second clock UI wrong");
            CreateDiscreteEventWorld();
            engine.ScheduleWorldSignal(1020, "pump-stop", "停止自動快進", blocking: true);
            engine.SetPlayerAutonomy(true); paused = false;
            AdvanceSimulation(1);
            if (!paused || engine.WorldTimeSeconds != 1020) throw new Exception("Frame pump ignored important stop");
            GD.Print("DISCRETE_EVENTS_UI_SMOKE_PASS: second clock, exact blocker, real batch, snapshot resume");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr("DISCRETE_EVENTS_UI_SMOKE_FAIL: " + e); GetTree().Quit(1); }
    }
}
