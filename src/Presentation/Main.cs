using Godot;
using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class Main : Control
{
    private SimulationEngine engine = new(DemoWorld.Create());
    private TickPump pump = null!;
    private Label status = null!;
    private Label notice = null!;
    private RichTextLabel log = null!;
    private bool paused = true;
    private string? lastSave;

    public override void _Ready()
    {
        pump = new(engine, TimeSpan.FromMilliseconds(250));
        var panel = new VBoxContainer { Position = new Vector2(24, 24), Size = new Vector2(950, 640) };
        AddChild(panel);
        panel.AddChild(new Label { Text = "自主修仙世界｜階段 0 規則與帳本驗證" });
        var buttons = new HBoxContainer();
        panel.AddChild(buttons);
        AddButton(buttons, "暫停／繼續", () => paused = !paused);
        AddButton(buttons, "前進一 Tick", () => { if (paused) engine.Step(); });
        AddButton(buttons, "玩家轉移 10 靈石", SubmitPlayer);
        AddButton(buttons, "另存新檔", Save);
        AddButton(buttons, "讀取本次存檔", Load);
        status = new Label(); panel.AddChild(status);
        notice = new Label { Text = "初始暫停。轉移需兩個 Tick 完成；NPC 為流程測試來源。" }; panel.AddChild(notice);
        log = new RichTextLabel { CustomMinimumSize = new Vector2(900, 420) }; panel.AddChild(log);
        var snapshot = engine.Snapshot();
        var intent = new TransferProbe("sect").Decide(new("npc", 0, Inventory.Balance(snapshot, "npc", "spirit-stone")));
        if (intent is not null) engine.Submit(intent);
        Render();
    }

    private static void AddButton(HBoxContainer parent, string text, System.Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }

    public override void _Process(double delta)
    {
        if (!paused) pump.Advance(TimeSpan.FromSeconds(delta));
        Render();
    }

    private void SubmitPlayer()
    {
        // Sequence is persisted, so IDs remain unique after reloading a save.
        var snapshot = engine.Snapshot();
        engine.Submit(new("ui:" + snapshot.NextSequence, snapshot.ControlledCharacterId, "sect",
            "spirit-stone", 10, 2, DecisionSource.Player));
    }

    private void Save()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath("user://saves/world-" + Guid.NewGuid().ToString("N") + ".json");
            new JsonWorldStore().Save(path, engine.Snapshot());
            lastSave = path;
            notice.Text = "已儲存：" + path;
        }
        catch (Exception error) { notice.Text = "存檔失敗：" + error.Message; }
    }

    private void Load()
    {
        if (lastSave is null) { notice.Text = "請先另存新檔。"; return; }
        try
        {
            var restored = new SimulationEngine(new JsonWorldStore().Load(lastSave));
            engine = restored;
            pump = new(engine, TimeSpan.FromMilliseconds(250));
            paused = true;
            notice.Text = "已載入並暫停：" + lastSave;
        }
        catch (Exception error) { notice.Text = "讀檔失敗：" + error.Message; }
    }

    private void Render()
    {
        var snapshot = engine.Snapshot();
        status.Text = $"Tick {snapshot.Clock.Tick}｜{(paused ? "暫停" : "運作中")}｜待提交 {snapshot.Pending.Count}｜排程 {snapshot.Scheduled.Count}\n" +
            string.Join("　", snapshot.Characters.Select(c => $"{c.Name}: {Inventory.Balance(snapshot, c.Id, "spirit-stone")}")) +
            $"　青雲宗: {Inventory.Balance(snapshot, "sect", "spirit-stone")}";
        log.Text = "事件\n" + string.Join("\n", snapshot.Events.TakeLast(12).Select(e => $"[{e.Tick}] {e.ActionId}: {e.Kind} {e.Detail}")) +
            "\n\n帳本（含初始來源與保留帳戶）\n" + string.Join("\n", snapshot.Ledger.TakeLast(12).Select(e => $"[{e.Tick}] {e.From} → {e.To}: {e.Quantity} {e.ResourceId} ({e.Reason})"));
    }
}
