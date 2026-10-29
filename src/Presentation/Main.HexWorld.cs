using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class Main
{
    private void CreateHexWorld()
    {
        try
        {
            var replacement = NewTestWorld(HexWorldScenario.Create(playerName.Text));
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true); paused = true;
            developer.ResetHistory(); actors.Clear(); foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "已建立六角測試世界：固定初始領地，玩家為青雲宗宗主；原存檔未改寫。"; Render();
        }
        catch (Exception error) { notice.Text = "建立六角世界失敗：" + error.Message; }
    }
}
