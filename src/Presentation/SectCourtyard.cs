using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

// A view of the existing logical location; no simulation state is changed here.
public partial class SectCourtyard : Node2D
{
    public event Action<string>? Activated;
    private WorldState? world;
    private readonly Dictionary<string, Button> buttons = new();
    public int VisibleActorCount { get; private set; }
    public bool PlayerPresent { get; private set; }
    public override void _Ready()
    {
        AddHotspot("missions", "任務告示牌", new(55, 115), new(190, 95));
        AddHotspot("exchange", "宗門兌換處", new(605, 115), new(190, 95));
        AddHotspot("governance", "宗門大殿", new(325, 65), new(200, 90));
        AddHotspot("grove", "前往藥園", new(30, 335), new(140, 55));
        AddHotspot("alchemy", "前往煉丹房", new(680, 335), new(140, 55));
        AddHotspot("town", "山門 → 青雲鎮", new(325, 425), new(200, 50));
    }
    private void AddHotspot(string id, string text, Vector2 position, Vector2 size)
    {
        var button = new Button { Text = text, Position = position, Size = size,
            TooltipText = id is "grove" or "alchemy" or "town" ? "提交旅行，抵達後才可使用目的地設施" : "開啟既有操作面板" };
        buttons.Add(id, button); AddChild(button);
        button.Pressed += () => Activate(id);
    }
    public void Activate(string id)
    {
        if (buttons.TryGetValue(id, out var button) && !button.Disabled) Activated?.Invoke(id);
    }
    public void Refresh(WorldState snapshot)
    {
        world = snapshot;
        var player = snapshot.Characters.Single(c => c.Id == snapshot.ControlledCharacterId);
        bool travelling = snapshot.Scheduled.Any(t => t.Intent.ActorId == player.Id && t.Intent.Kind == ActionKind.Travel);
        PlayerPresent = snapshot.DemoMapEnabled && player.DemoLocationId == "sect" && !travelling;
        bool local = PlayerPresent && player.RegionId == "valley";
        VisibleActorCount = local ? snapshot.Characters.Count(c => c.RegionId == player.RegionId && c.DemoLocationId == "sect" &&
            !snapshot.Scheduled.Any(t => t.Intent.ActorId == c.Id && t.Intent.Kind == ActionKind.Travel)) : 0;
        foreach (var button in buttons.Values) button.Disabled = !local;
        QueueRedraw();
    }
    public override void _Draw()
    {
        DrawRect(new(0, 0, 850, 490), new Color("203832"));
        DrawRect(new(25, 45, 800, 405), new Color("526052"));
        DrawRect(new(260, 190, 330, 220), new Color("999581"));
        DrawRect(new(398, 150, 54, 330), new Color("b4aa8a"));
        DrawRect(new(45, 40, 760, 10), new Color("7a8781"));
        DrawRect(new(20, 45, 12, 275), new Color("7a8781"));
        DrawRect(new(818, 45, 12, 275), new Color("7a8781"));
        DrawRect(new(275, 418, 28, 65), new Color("a67150"));
        DrawRect(new(547, 418, 28, 65), new Color("a67150"));
        foreach (var p in new[] { new Vector2(205, 275), new Vector2(645, 275), new Vector2(90, 270), new Vector2(760, 270) })
        {
            DrawRect(new(p.X - 5, p.Y, 10, 26), new Color("795b43"));
            DrawCircle(p, 23, new Color("325b42"));
        }
        DrawString(ThemeDB.FallbackFont, new(25, 29), "青雲宗 · 山門廣場", fontSize: 22, modulate: Colors.White);
        if (world is null || !PlayerPresent) return;
        var player = world.Characters.Single(c => c.Id == world.ControlledCharacterId);
        if (player.RegionId != "valley") return;
        var departing = world.Scheduled.Where(t => t.Intent.Kind == ActionKind.Travel).Select(t => t.Intent.ActorId).ToHashSet();
        var locals = world.Characters.Where(c => c.RegionId == player.RegionId && c.DemoLocationId == "sect" && !departing.Contains(c.Id)).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < locals.Length; i++)
        {
            var actor = locals[i];
            var point = new Vector2(285 + i % 7 * 46, 250 + i / 7 * 28);
            bool controlled = actor.Id == player.Id;
            DrawEllipseShadow(point);
            DrawRect(new(point.X - 7, point.Y - 6, 14, 17), controlled ? Colors.Gold : new Color("8eb7ce"));
            DrawCircle(point + new Vector2(0, -11), 6, new Color("efd5b1"));
            if (controlled)
            {
                DrawArc(point + new Vector2(0, -1), 18, 0, Mathf.Tau, 24, Colors.Gold, 2);
                DrawString(ThemeDB.FallbackFont, point + new Vector2(-15, -25), "玩家", fontSize: 13, modulate: Colors.Gold);
            }
        }
    }
    private void DrawEllipseShadow(Vector2 point) => DrawRect(new(point.X - 10, point.Y + 8, 20, 5), new Color(0, 0, 0, .25f));
}
