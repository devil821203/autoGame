using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class DemoMapPanel : Control
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private readonly (string Id, string Name, Vector2 Point)[] places =
        [("town", "青雲鎮", new(140, 210)), ("sect", "青雲宗", new(440, 100)), ("grove", "藥園", new(700, 220)), ("dungeon", "打怪副本", new(440, 300)), ("alchemy", "煉丹房", new(700, 350))];
    public override void _Ready()
    {
        CustomMinimumSize = new(850, 480);
        foreach (var place in places)
        {
            var button = new Button { Text = "前往" + place.Name, Position = place.Point + new Vector2(-50, 32) };
            button.Pressed += () => Travel(place.Id); AddChild(button);
        }
    }
    public void Travel(string target)
    {
        var w = ReadWorld();
        Requested?.Invoke(new("ui-travel:" + w.NextSequence, w.ControlledCharacterId, target, "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
    }
    public override void _Draw()
    {
        if (ReadWorld is null) return;
        var w = ReadWorld(); var font = ThemeDB.FallbackFont;
        if (w.HexMap is not null)
        {
            DrawString(font, new(20, 30), "目前為六角世界；請使用「六角世界」頁查看逐格旅行與領地。", fontSize: 18);
            return;
        }
        DrawString(font, new(20, 30), w.DemoMapEnabled ? "單地區 Demo｜鎮↔各據點3 Tick，宗門↔藥圃5 Tick（正式地圖待確認）" : "請按「階段4 地圖Demo」建立情境", fontSize: 18);
        for (int i = 0; i < places.Length; i++)
            for (int j = i + 1; j < places.Length; j++) DrawLine(places[i].Point, places[j].Point, Colors.DarkSlateGray, 3);
        foreach (var p in places)
        {
            DrawCircle(p.Point, 24, Colors.SeaGreen);
            var locals = w.Characters.Where(c => c.RegionId == "valley" && c.DemoLocationId == p.Id &&
                !w.Scheduled.Any(t => t.Intent.ActorId == c.Id && t.Intent.Kind == ActionKind.Travel)).ToArray();
            DrawString(font, p.Point + new Vector2(-38, -35), $"{p.Name} ({locals.Length}人)", fontSize: 18);
            if (locals.Any(c => c.Id == w.ControlledCharacterId)) DrawCircle(p.Point, 9, Colors.Gold);
        }
        foreach (var trip in w.Scheduled.Where(t => t.Intent.Kind == ActionKind.Travel))
        {
            var actor = w.Characters.Single(c => c.Id == trip.Intent.ActorId);
            var from = places.First(p => p.Id == actor.DemoLocationId).Point;
            var to = places.First(p => p.Id == trip.Intent.TargetId).Point;
            double remaining = trip.DueTick - w.Clock.Tick;
            float progress = (float)Math.Clamp(1 - remaining / DemoMap.TravelTicks(actor.DemoLocationId, trip.Intent.TargetId, w.MapRules), 0, 1);
            DrawCircle(from.Lerp(to, progress), actor.Id == w.ControlledCharacterId ? 9 : 4,
                actor.Id == w.ControlledCharacterId ? Colors.Gold : Colors.LightSkyBlue);
        }
        var task = w.Scheduled.FirstOrDefault(t => t.Intent.ActorId == w.ControlledCharacterId && t.Intent.Kind == ActionKind.Travel);
        var player = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        var nearby = task is null ? w.Characters.Where(c => c.Id != player.Id && c.RegionId == player.RegionId && c.DemoLocationId == player.DemoLocationId &&
            !w.Scheduled.Any(t => t.Intent.ActorId == c.Id && t.Intent.Kind == ActionKind.Travel)).ToArray() : [];
        DrawString(font, new(20, 425), $"玩家：{player.Name}｜同據點 {nearby.Length} 人：" + string.Join("、", nearby.Take(6).Select(c => c.Name)), fontSize: 16);
        DrawString(font, new(20, 455), task is null ? "金色為玩家、藍色為旅行NPC；副本須啟用宗門經濟測試情境。" : $"旅行中 → {task.Intent.TargetId}，剩餘 {task.DueTick - w.Clock.Tick} Tick", fontSize: 18);
        if (w.TestProcurement is not null)
        {
            DrawCircle(new(700, 90), 20, Colors.DarkOrange); DrawLine(new(700, 90), new(465, 100), Colors.DarkOrange, 2);
            DrawString(font, new(590, 55), "測試收購節點（自動向宗門收購）", fontSize: 16);
        }
    }
}
