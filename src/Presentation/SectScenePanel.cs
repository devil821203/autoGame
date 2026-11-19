using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class SectScenePanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    public event Action<string>? OpenPanel;
    private Label summary = null!;
    private Button enter = null!;
    public SectCourtyard Courtyard { get; private set; } = null!;
    public override void _Ready()
    {
        var bar = new HBoxContainer(); AddChild(bar);
        enter = new Button { Text = "前往青雲宗" }; bar.AddChild(enter);
        enter.Pressed += () => Travel("sect");
        bar.AddChild(new Label { Text = "點擊建物開啟操作；金色為玩家，藍色為同據點角色" });
        summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(summary);
        var canvas = new Control { CustomMinimumSize = new(850, 490), MouseFilter = MouseFilterEnum.Ignore }; AddChild(canvas);
        Courtyard = GD.Load<PackedScene>("res://Scenes/Areas/QingyunSect.tscn").Instantiate<SectCourtyard>();
        canvas.AddChild(Courtyard);
        Courtyard.Activated += id =>
        {
            if (id is "town" or "grove" or "alchemy") Travel(id);
            else OpenPanel?.Invoke(id);
        };
    }
    public void Travel(string target)
    {
        var w = ReadWorld();
        Requested?.Invoke(new("ui-sect-scene:" + w.NextSequence, w.ControlledCharacterId, target, "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
    }
    public void Refresh(WorldState w)
    {
        Courtyard.Refresh(w);
        var player = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        var trip = w.Scheduled.FirstOrDefault(t => t.Intent.ActorId == player.Id && t.Intent.Kind == ActionKind.Travel);
        enter.Disabled = !w.DemoMapEnabled || trip is not null || player.DemoLocationId == "sect";
        summary.Text = !w.DemoMapEnabled ? "請先建立「宗門經濟／副本測試」或階段4 Demo 情境。" :
            trip is not null ? $"旅行中 → {trip.Intent.TargetId}，剩餘 {trip.DueTick - w.Clock.Tick} Tick；抵達前無法使用設施。" :
            !Courtyard.PlayerPresent ? $"目前位於 {player.DemoLocationId}；請先前往青雲宗，抵達後顯示現場角色與開放互動。" :
            $"Tick {w.Clock.Tick}｜{player.Name}｜錢財 {Inventory.Balance(w, player.Id, "spirit-stone")}｜貢獻 {Inventory.Balance(w, player.Id, "contribution")}｜現場 {Courtyard.VisibleActorCount} 人";
    }
}
