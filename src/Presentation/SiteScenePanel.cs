using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class SiteScenePanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action? BackToMap;
    public event Action<string>? OpenService;
    public string? ActiveSiteId { get; private set; }
    public int UnlockedSlots { get; private set; }
    private SceneSite? bound;
    private Label summary = null!;
    private Label selection = null!;
    private Control holder = null!;
    private SiteCourtyard? courtyard;
    private HFlowContainer services = null!;
    public override void _Ready()
    {
        var row = new HFlowContainer(); AddChild(row);
        Add(row, "返回六角世界", () => BackToMap?.Invoke());
        summary = new Label { Text = "請由世界地塊選單選擇場景，抵達入口後進入。", AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(summary);
        selection = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(selection);
        services = new HFlowContainer(); AddChild(services);
        holder = new Control { CustomMinimumSize = new(850, 670), MouseFilter = MouseFilterEnum.Ignore }; AddChild(holder);
    }
    private static void Add(Container row, string name, Action action)
    { var button = new Button { Text = name }; row.AddChild(button); button.Pressed += action; }
    public bool Enter(string id)
    {
        var world = ReadWorld(); var site = world.HexMap?.Sites.SingleOrDefault(s => s.Id == id);
        if (site is null || !SceneSites.CanEnter(world, site)) return false;
        ActiveSiteId = id; Refresh(world); return true;
    }
    public void Refresh(WorldState world)
    {
        var site = world.HexMap?.Sites.SingleOrDefault(s => s.Id == ActiveSiteId);
        if (site is null || !SceneSites.CanEnter(world, site))
        {
            if (courtyard is not null) { holder.RemoveChild(courtyard); courtyard.QueueFree(); courtyard = null; }
            foreach (var child in services.GetChildren()) { services.RemoveChild(child); child.QueueFree(); }
            bound = null; ActiveSiteId = null; UnlockedSlots = 0;
            summary.Text = "尚未進入場景，或已離開入口；請從世界地塊選單重新進入。"; selection.Text = ""; return;
        }
        if (bound is null || bound.Id != site.Id || bound.MainLevel != site.MainLevel || !bound.Slots.SequenceEqual(site.Slots) || bound.Name != site.Name || bound.MainBuilding != site.MainBuilding || bound.Kind != site.Kind || bound.OrganizationId != site.OrganizationId)
        {
            if (courtyard is not null) { holder.RemoveChild(courtyard); courtyard.QueueFree(); }
            courtyard = GD.Load<PackedScene>($"res://Scenes/Areas/{site.Kind}Site.tscn").Instantiate<SiteCourtyard>();
            holder.AddChild(courtyard); courtyard.Bind(site); courtyard.Selected += id => selection.Text = id == "main"
                ? $"{site.MainBuilding} Lv.{site.MainLevel}｜此主建築只控制本場景建築格；升級操作尚未開放。"
                : $"{site.Name}／建築格 {id}：尚未接入內部施工，不扣款、不新增產出。";
            courtyard.ExitRequested += () => BackToMap?.Invoke();
            foreach (var child in services.GetChildren()) { services.RemoveChild(child); child.QueueFree(); }
            if (site.OrganizationId == "sect")
            {
                Add(services, "宗門任務", () => OpenService?.Invoke("missions"));
                Add(services, "宗門設施與兌換", () => OpenService?.Invoke("exchange"));
            }
            if (site.Id == "settlement:qingyun") Add(services, "青雲鎮市場", () => OpenService?.Invoke("market"));
            bound = site; selection.Text = "點主建築查看等級，點可用空地查看建築格。";
        }
        UnlockedSlots = site.Slots.Count(s => s.RequiredLevel <= site.MainLevel);
        var owner = world.Organizations.SingleOrDefault(o => o.Id == site.OrganizationId);
        summary.Text = $"{site.Name}｜{SiteCourtyard.KindName(site.Kind)}｜主建築 Lv.{site.MainLevel}｜可用格 {UnlockedSlots}/{site.Slots.Count}\n" +
            (owner is null ? "聚落主建築與場景資料獨立；人口、歸屬與政治結算尚未接入。" :
                $"組織：{owner.Name}｜組織錢財 {Inventory.Balance(world, owner.Id, "spirit-stone")}（沿用原帳戶）");
    }
}
