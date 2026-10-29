using Godot;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class Main
{
    private MortalCultivationPanel mortalCultivation = null!;
    private void CreateMortalCultivationWorld()
        => InitializeCultivationWorld(false);
    private void CreateMethodWorld() => InitializeCultivationWorld(true);
    private void InitializeCultivationWorld(bool methods)
    {
        try
        {
            engine = new(methods ? MethodTrainingScenario.Create(playerName.Text) : MortalCultivationScenario.Create(playerName.Text));
            pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true); paused = true;
            developer.ResetHistory(); actors.Clear(); foreach (var a in engine.Snapshot().Characters) actors.AddItem(a.Name);
            var tabs = (TabContainer)mortalCultivation.GetParent(); tabs.CurrentTab = tabs.GetTabIdxFromControl(mortalCultivation);
            notice.Text = methods ? "功法修練測試：在凡人修為頁選擇功法修練；NPC自主評估，熟練及三功分開結算。" : "階段8：修練／打坐1Tick、1AP、1體力、1基礎經驗；疲勞與心境保留小數。"; Render();
        }
        catch (Exception error) { notice.Text = "建立凡人修為情境失敗：" + error.Message; }
    }
    private async void MortalCultivationSmokeTest()
    {
        try
        {
            bool methodSmoke = OS.GetCmdlineUserArgs().Contains("--method-smoke");
            if (methodSmoke) CreateMethodWorld(); else CreateMortalCultivationWorld();
            var w = engine.Snapshot();
            if (w.MortalActionRules is null || !mortalCultivation.DetailsText.Contains("智力：10")) throw new Exception("Mortal panel/scenario missing");
            if (methodSmoke)
            {
                engine.Submit(new("method-ui-smoke", w.ControlledCharacterId, w.ControlledCharacterId, "method:QingyunFist", 1, 0,
                    DecisionSource.Player, ActionKind.TrainMortalAttribute));
                engine.Step(); Render(); w = engine.Snapshot();
                if (w.Methods!.Learners[w.ControlledCharacterId]["QingyunFist"] != 1 || !mortalCultivation.DetailsText.Contains("青雲拳法：熟練1"))
                    throw new Exception("Method panel training/proficiency missing");
            }
            else if (OS.GetCmdlineUserArgs().Contains("--mortal-long-smoke"))
            {
                for (int tick = 0; tick < 120; tick++) { engine.Step(true); Render(); }
                w = engine.Snapshot();
                if (w.Clock.Tick <= 67 || !w.PartyLoot.Any(l => l.ResourceId == w.RecoveryResourceId)) throw new Exception("Long run did not cover tick67 party medicine loot");
                GD.Print($"MORTAL_LONG_UI Tick={w.Clock.Tick} PartyMedicineLoot={w.PartyLoot.Count(l => l.ResourceId == w.RecoveryResourceId)}");
            }
            else
            {
                mortalCultivation.Request(false); engine.Step(); Render(); w = engine.Snapshot();
                if (w.Characters.Single(a => a.Id == w.ControlledCharacterId).MortalCultivation!.Experience.GetValueOrDefault(MortalAttribute.Intelligence) != 1) throw new Exception("Training UI command failed");
            }
            characterAttributes.Refresh(w, engine);
            string digest = WorldCodec.Digest(w); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new Exception("Mortal save/load changed state");
            characterAttributes.Hide(); characterJournal.Hide();
            var tree = GetTree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            GD.Print(methodSmoke ? "METHOD_TRAINING_UI_SMOKE_PASS" : "MORTAL_CULTIVATION_UI_SMOKE_PASS"); tree.Quit();
        }
        catch (Exception e) { GD.PrintErr("MORTAL_CULTIVATION_UI_SMOKE_FAIL: " + e); GetTree().Quit(1); }
    }
}
