using Godot;
using Cultivation.Domain;

namespace Cultivation.Presentation;

public partial class Main
{
    private void ActionDrivenSmokeTest()
    {
        try {
            RebuildCurrentScenario("methods"); long start = engine.Snapshot().Clock.Tick;
            AdvanceSimulation(10);
            if (engine.Snapshot().Clock.Tick != start) throw new Exception("Idle player moved world time");
            SubmitTracked(new("action-ui-test", "player", "player", "method:QingyunFist", 1, 0, DecisionSource.Player, ActionKind.TrainMortalAttribute));
            if (engine.Snapshot().Clock.Tick != start + 1 || engine.Snapshot().Methods!.Learners["player"]["QingyunFist"] != 1) throw new Exception("Player action failed");
            AdvanceSimulation(10); if (engine.Snapshot().Clock.Tick != start + 1) throw new Exception("World did not stop");
            StepTicks(1); if (engine.Snapshot().Clock.Tick != start + 2) throw new Exception("Wait failed");
            autonomousPlayerToggle!.ButtonPressed = true; AdvanceSimulation(.25);
            long next = engine.Snapshot().Clock.Tick; if (next <= start + 2) throw new Exception("Optional player autonomy failed");
            autonomousPlayerToggle.ButtonPressed = false; AdvanceSimulation(10);
            if (engine.Snapshot().Clock.Tick != next) throw new Exception("Disabling autonomy did not stop");
            SubmitTracked(new("ap-rest-ui", "player", "player", "stamina", 1, 0, DecisionSource.Player, ActionKind.Rest));
            var restored = engine.Snapshot();
            if (restored.Time!.Budgets.Single(b => b.ActorId == "player").Remaining != restored.Time.Rules.DailyActionPoints ||
                !restored.Events.Any(e => e.Kind == "RestActionPointsRecovered" && e.Detail.StartsWith("player:"))) throw new Exception("Rest AP failed");
            Render(); if (!status.Text.Contains("分鐘")) throw new Exception("Minute time display missing");
            long batchStart = restored.Clock.Tick;
            decimal before = restored.Characters.Single(a => a.Id == "player").MortalCultivation!.Experience.GetValueOrDefault(MortalAttribute.Intelligence);
            mortalCultivation.Repetitions = 6; mortalCultivation.Request(false); Render();
            var batched = engine.Snapshot();
            if (batched.Clock.Tick != batchStart + 6 || batched.Time!.Budgets.Single(b => b.ActorId == "player").Remaining != 24 ||
                batched.Characters.Single(a => a.Id == "player").MortalCultivation!.Experience.GetValueOrDefault(MortalAttribute.Intelligence) != before + 6 ||
                !mortalCultivation.BatchText.Contains("60分鐘／6AP")) throw new Exception("Batch training control failed");
            GD.Print("ACTION_DRIVEN_UI_SMOKE_PASS"); GetTree().Quit();
        } catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }
}
