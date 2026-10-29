using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class HexMapPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    public event Action? EnterSect;
    public event Action<string>? EnterSite;
    private OptionButton siteChoices = null!;
    private Button siteEnter = null!;
    private List<string> siteIds = [];
    public HexCoord? Selection { get; private set; }
    private HexMapCanvas canvas = null!;
    private Label details = null!;
    private Label reason = null!;
    private Button travel = null!;
    private Button enter = null!;
    private Button build = null!;
    private Button cancel = null!;
    private OptionButton definitions = null!;
    private List<string> definitionIds = [];
    public HexMapCanvas Canvas => canvas;
    public override void _Ready()
    {
        AddChild(new Label { Text = "六角世界｜左鍵選地塊、滾輪縮放、中鍵拖曳｜青／橙／紅：宗門／幫派／山寨" });
        var row = new HFlowContainer(); AddChild(row);
        var follow = new CheckBox { Text = "視角跟隨主角色（關閉後可中鍵拖曳）", ButtonPressed = true }; row.AddChild(follow);
        follow.Toggled += enabled => { canvas.FollowPlayer = enabled; Refresh(ReadWorld()); };
        travel = Add(row, "旅行至選取地塊", Travel);
        enter = Add(row, "舊版青雲宗操作場景", () => EnterSect?.Invoke());
        siteChoices = new OptionButton(); row.AddChild(siteChoices);
        siteChoices.ItemSelected += _ => Refresh(ReadWorld());
        siteEnter = Add(row, "進入選取場景", EnterSelectedSite);
        definitions = new OptionButton(); row.AddChild(definitions); definitions.ItemSelected += _ => Refresh(ReadWorld());
        build = Add(row, "宗門興建", Build); cancel = Add(row, "取消選取地塊施工", Cancel);
        details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(details);
        reason = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(reason);
        canvas = new HexMapCanvas(); AddChild(canvas); canvas.Selected += Select;
    }
    private static Button Add(Container row, string text, Action action)
    { var button = new Button { Text = text }; row.AddChild(button); button.Pressed += action; return button; }
    public void Select(HexCoord coord) { Selection = coord; Refresh(ReadWorld()); }
    public void EnterSelectedSite()
    {
        var world = ReadWorld();
        if (siteChoices.Selected < 0 || siteChoices.Selected >= siteIds.Count) return;
        var site = world.HexMap?.Sites.SingleOrDefault(s => s.Id == siteIds[siteChoices.Selected]);
        if (site is not null && SceneSites.CanEnter(world, site)) EnterSite?.Invoke(site.Id);
    }
    public void ChooseSite(string id)
    {
        var world = ReadWorld(); var site = world.HexMap?.Sites.SingleOrDefault(s => s.Id == id);
        if (site is null) return;
        Select(site.Anchor); int index = siteIds.IndexOf(id);
        if (index >= 0) { siteChoices.Select(index); Refresh(world); }
    }
    private ActionIntent Intent(ActionKind kind, string target, long quantity = 1, string? definition = null)
    {
        var w = ReadWorld(); var actor = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        return new("ui-hex:" + w.NextSequence, actor.Id, target, kind == ActionKind.Travel ? "travel" : "spirit-stone", quantity, 0,
            DecisionSource.Player, kind, MissionId: definition, OrganizationId: kind == ActionKind.Travel ? null : actor.OrganizationId);
    }
    private string Target(WorldState w) => w.HexMap!.Tiles.Single(t => t.Coord == Selection).PlaceId ?? Selection!.Value.ToString();
    public void Travel() { if (Selection is not null && ReadWorld().HexMap is not null) Requested?.Invoke(Intent(ActionKind.Travel, Target(ReadWorld()))); }
    public void Build()
    {
        var w = ReadWorld(); if (Selection is null || w.HexMap is null || definitions.Selected < 0) return;
        var d = w.HexMap.BuildingDefinitions.Single(d => d.Id == definitionIds[definitions.Selected]);
        Requested?.Invoke(Intent(ActionKind.ConstructBuilding, Selection.Value.ToString(), d.Cost, d.Id));
    }
    public void Cancel()
    {
        var w = ReadWorld(); var b = w.HexMap?.Buildings.FirstOrDefault(b => b.Coord == Selection && b.Status is BuildingStatus.Constructing or BuildingStatus.Suspended);
        if (b is not null) Requested?.Invoke(Intent(ActionKind.CancelBuilding, b.Id));
    }
    public void Refresh(WorldState w)
    {
        var map = w.HexMap;
        if (map is null)
        {
            details.Text = "請按上方「六角世界／領地測試」建立新世界（請先保存目前進度）。";
            travel.Disabled = enter.Disabled = build.Disabled = cancel.Disabled = siteEnter.Disabled = true;
            siteChoices.Clear(); siteIds.Clear(); reason.Text = ""; canvas.Refresh(w, null); return;
        }
        var labels = map.BuildingDefinitions.Select(d => $"{d.Name} {d.Cost}錢財／{GameTimeDisplay.Duration(w,d.Ticks)}").ToArray();
        if (!definitionIds.SequenceEqual(map.BuildingDefinitions.Select(d => d.Id)) || definitions.ItemCount != labels.Length ||
            Enumerable.Range(0, labels.Length).Any(index => definitions.GetItemText(index) != labels[index]))
        {
            definitions.Clear(); definitionIds = map.BuildingDefinitions.Select(d => d.Id).ToList();
            foreach (var d in map.BuildingDefinitions) definitions.AddItem($"{d.Name} {d.Cost}錢財／{GameTimeDisplay.Duration(w,d.Ticks)}");
        }
        if (Selection is null || !map.Tiles.Any(t => t.Coord == Selection)) Selection = HexNavigation.Position(map, w.Characters.Single(c => c.Id == w.ControlledCharacterId));
        var tile = map.Tiles.Single(t => t.Coord == Selection);
        var sites = SceneSites.At(w, tile.Coord).ToArray();
        if (!siteIds.SequenceEqual(sites.Select(s => s.Id)))
        {
            siteChoices.Clear(); siteIds = sites.Select(s => s.Id).ToList();
            foreach (var site in sites) siteChoices.AddItem(site.Name);
        }
        siteEnter.Disabled = siteChoices.Selected < 0 || !SceneSites.CanEnter(w, sites[siteChoices.Selected]);
        string TerrainName(HexTerrain terrain) => terrain switch { HexTerrain.Lake => "湖泊", HexTerrain.Desert => "沙漠", _ => "平原" };
        string StatusName(BuildingStatus status) => status switch { BuildingStatus.Constructing => "施工中", BuildingStatus.Complete => "已完工", BuildingStatus.Suspended => "停工", _ => "已取消" };
        string Org(string? id) => id is null ? "無" : w.Organizations.Single(o => o.Id == id).Name;
        var actor = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        var trip = map.Journeys.SingleOrDefault(j => j.ActorId == actor.Id);
        var buildings = map.Buildings.Where(b => b.Coord == tile.Coord && b.Status != BuildingStatus.Cancelled).ToArray();
        details.Text = $"選取 ({tile.Coord.Q}, {tile.Coord.R})｜{TerrainName(tile.Terrain)}{(tile.Mountain ? "／山脈" : "")}{(tile.Forest ? "／森林" : "")}｜{tile.Settlement ?? tile.PlaceId ?? "野外"}\n" +
            $"組織領地：{Org(tile.LocalOrganizationId)}｜玩家位置 {HexNavigation.Position(map, actor)}" +
            (trip is null ? "" : $"｜旅行剩餘 {trip.Remaining.Count} 格") + "\n建築：" +
            string.Join("、", buildings.Select(b => map.BuildingDefinitions.Single(d => d.Id == b.DefinitionId).Name + " " + StatusName(b.Status)));
        string? travelError = ActionValidator.Validate(w, Intent(ActionKind.Travel, Target(w)));
        travel.Disabled = travelError is not null;
        enter.Disabled = actor.DemoLocationId != "sect" || trip is not null || tile.PlaceId != "sect";
        string? buildError = "無建築設定";
        if (definitions.Selected >= 0)
        {
            var d = map.BuildingDefinitions[definitions.Selected];
            buildError = ActionValidator.Validate(w, Intent(ActionKind.ConstructBuilding, tile.Coord.ToString(), d.Cost, d.Id));
        }
        build.Disabled = buildError is not null;
        var current = buildings.FirstOrDefault(b => b.Status is BuildingStatus.Constructing or BuildingStatus.Suspended);
        cancel.Disabled = current is null || ActionValidator.Validate(w, Intent(ActionKind.CancelBuilding, current.Id)) is not null;
        reason.Text = $"旅行：{travelError ?? "可前往"}｜建造：{buildError ?? "可興建，從組織庫存保留錢財"}";
        canvas.Refresh(w, Selection);
    }
}
