using Godot;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class Main
{
    private CheckButton? autonomousPlayerToggle;
    private void CreateCurrentScenarioControls(VBoxContainer panel)
    {
        var settings = TestScenarioCatalog.Read();
        var row = AddButtonGroup(panel, "目前測試情境（重新建立會重設目前世界，請先存檔）");
        playerName = new LineEdit { Text = "行者", PlaceholderText = "新角色名稱", MaxLength = 24 }; row.AddChild(playerName);
        var choice = new OptionButton(); row.AddChild(choice);
        foreach (var entry in settings.Scenarios) choice.AddItem(entry.Name);
        choice.Select(Array.FindIndex(settings.Scenarios, s => s.Id == settings.DefaultId));
        var description = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(description);
        void Describe() => description.Text = settings.Scenarios[choice.Selected].Description;
        choice.ItemSelected += _ => Describe(); Describe();
        var autoPlayer = new CheckButton { Text = "主角自主排程（持續推進）" }; autonomousPlayerToggle = autoPlayer; row.AddChild(autoPlayer);
        autoPlayer.Toggled += enabled => { engine.SetPlayerAutonomy(enabled); paused = !enabled; };
        AddButton(row, "重新建立選定情境", () => RebuildCurrentScenario(settings.Scenarios[choice.Selected].Id));
        AddButton(row, "聚落管理／查詢", OpenSettlementManagement);
    }
    private void RebuildCurrentScenario(string id)
    {
        try {
            var replacement = new SimulationEngine(TestScenarioCatalog.Create(id, playerName.Text));
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true); paused = true;
            developer.ResetHistory(); actors.Clear();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            var target = id switch { "methods" => (Control)mortalCultivation, "settlement" => hexMap, _ => boardBattlePanel };
            var tabs = (TabContainer)target.GetParent(); tabs.CurrentTab = tabs.GetTabIdxFromControl(target);
            notice.Text = "已依目前config重新建立「" + TestScenarioCatalog.Read().Scenarios.Single(s => s.Id == id).Name + "」，世界暫停；舊存檔保留。";
            Render();
        } catch (Exception e) { notice.Text = "重新建立情境失敗：" + e.Message; }
    }
    private async void ScenarioCatalogSmokeTest()
    {
        try {
            foreach (var entry in TestScenarioCatalog.Read().Scenarios) {
                RebuildCurrentScenario(entry.Id); var w = engine.Snapshot();
                if (w.Clock.Tick != 0 || !paused || !notice.Text.StartsWith("已依目前config")) throw new Exception("Scenario rebuild failed: " + entry.Id);
                if (entry.Id == "methods" && w.Methods is null || entry.Id == "settlement" && w.SettlementOrganizationDemo is null || entry.Id == "battle" && w.BoardBattle is null)
                    throw new Exception("Wrong scenario: " + entry.Id);
                var clone = WorldCodec.Clone(w);
                if (WorldCodec.Digest(clone) != WorldCodec.Digest(w)) throw new Exception("Snapshot changed");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            characterAttributes.Hide(); characterJournal.Hide();
            GD.Print("SCENARIO_CATALOG_UI_SMOKE_PASS"); GetTree().Quit();
        } catch (Exception e) { GD.PrintErr(e); GetTree().Quit(1); }
    }
}
