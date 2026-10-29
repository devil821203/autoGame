using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class SiteCourtyard : Node2D
{
    private SceneSite? site;
    private readonly List<Node> controls = [];
    public event Action<string>? Selected;
    public event Action? ExitRequested;
    public void Bind(SceneSite value)
    {
        site = value;
        foreach (var node in controls) { RemoveChild(node); node.QueueFree(); } controls.Clear();
        AddButton(value.MainBuilding + $"  Lv.{value.MainLevel}", new(300, 55), new(250, 85), "main");
        AddButton("返回六角世界", new(330, 603), new(190, 35), "exit");
        for (int i = 0; i < value.Slots.Count; i++)
        {
            var slot = value.Slots[i]; bool unlocked = value.MainLevel >= slot.RequiredLevel;
            var button = AddButton(unlocked ? $"建築格 {slot.Id}\n可用空地" : $"建築格 {slot.Id}\n主建築Lv.{slot.RequiredLevel}解鎖",
                new(85 + i % 4 * 175, 190 + i / 4 * 65), new(155, 55), slot.Id);
            button.Modulate = unlocked ? Colors.White : new Color("8b9292");
            button.TooltipText = unlocked ? "目前為建築用地呈現，尚未開放內部施工" : "主建築等級不足，不能使用";
            button.Disabled = !unlocked;
        }
        QueueRedraw();
    }
    private Button AddButton(string text, Vector2 position, Vector2 size, string id)
    {
        var button = new Button { Text = text, Position = position, Size = size };
        AddChild(button); controls.Add(button);
        button.Pressed += () => { if (id == "exit") ExitRequested?.Invoke(); else Selected?.Invoke(id); }; return button;
    }
    public override void _Draw()
    {
        if (site is null) return;
        Color ground = site.Kind switch
        {
            SiteKind.Sect => new("49634e"), SiteKind.Gang => new("625342"),
            SiteKind.Stronghold => new("51473c"), SiteKind.Village => new("69754b"), SiteKind.Town => new("68716b"), _ => new("596571")
        };
        DrawRect(new(0, 0, 850, 670), ground.Darkened(.3f));
        DrawRect(new(25, 40, 800, 600), ground);
        DrawRect(new(400, 145, 50, 485), new Color("aaa087"));
        DrawRect(new(50, 150, 750, 18), new Color("aaa087"));
        DrawString(ThemeDB.FallbackFont, new(25, 29), site.Name + "｜" + KindName(site.Kind), fontSize: 21, modulate: Colors.White);
        for (int i = 0; i < 5; i++)
        {
            float x = 65 + i * 175;
            DrawRect(new(x - 4, 550, 8, 25), new Color("75543d"));
            DrawCircle(new(x, 547), 17, new Color("355743"));
        }
    }
    public static string KindName(SiteKind kind) => kind switch
    {
        SiteKind.Sect => "宗門", SiteKind.Gang => "幫派", SiteKind.Stronghold => "山寨",
        SiteKind.Village => "村莊", SiteKind.Town => "城鎮", _ => "城市"
    };
}
