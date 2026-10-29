using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class PerformancePanel : RichTextLabel
{
    public void Refresh(WorldState world, TickDiagnostics? metrics)
    {
        string fps = DisplayServer.GetName() == "headless" ? "不適用（無畫面）" : Engine.GetFramesPerSecond().ToString();
        Text = $"FPS：{fps}\nAI LOD：{(world.Performance is null ? "原情境每次評估" : $"近{world.Performance.NearbyIntervalTicks}／同區{world.Performance.RegionalIntervalTicks}／遠{world.Performance.RemoteIntervalTicks} Tick")}\n" +
            $"NPC {world.Characters.Count - 1}｜決策預算 {world.Rules.DecisionBudget}｜已記錄LOD角色 {world.AiUpdates.Count}\n";
        if (metrics is null) { Text += "尚未推進Tick，沒有耗時樣本。"; return; }
        Text += $"\nTick {metrics.Tick}｜總耗時 {metrics.TotalMilliseconds:F2} ms\n複製 {metrics.CloneMilliseconds:F2} ms｜AI {metrics.AiMilliseconds:F2} ms｜驗證 {metrics.ValidationMilliseconds:F2} ms\n" +
            $"本Tick決策 {metrics.Decisions}｜排程／戰鬥佇列 {metrics.PendingEvents}｜交易 {metrics.Trades}\n本Tick配置 {metrics.AllocatedBytes / 1024.0:F1} KiB｜Managed記憶體 {metrics.ManagedMemoryBytes / 1048576.0:F1} MiB\n" +
            "\n耗時與記憶體是當次實測，不寫入存檔；無畫面測試沒有渲染FPS。\n分級測試指令及原始報告見階段5效能驗收文件。";
    }
}
