using Godot;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class SettlementPanel : VBoxContainer
{
    public Func<WorldState>? ReadWorld { get; set; }
    public event Action<ActionIntent>? Requested;
    private readonly Label summary = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly OptionButton buildings = new();
    private readonly SpinBox employees = new() { MinValue = 0, Step = 1 };
    private readonly OptionButton items = new();
    private readonly OptionButton donors = new();
    private readonly OptionButton buildTypes = new();
    private readonly OptionButton buildTiles = new();
    private readonly Label buildCost = new();
    private readonly List<SettlementBuildingKind> buildKinds = [];
    private readonly List<HexCoord> buildPositions = [];
    private readonly SpinBox quantity = new() { MinValue = 1, MaxValue = int.MaxValue, Step = 1, Value = 1 };
    private readonly List<string> buildingIds = [];
    private readonly List<string> itemIds = [];
    private readonly List<string> donorIds = [];
    public string DetailsText => summary.Text;
    public override void _Ready()
    {
        BuildManagementUi();
    }
    private static void ButtonOn(HBoxContainer row, string text, Action command)
    { var b = new Button { Text = text }; row.AddChild(b); b.Pressed += () => command(); }
    private static string BuildingName(SettlementBuildingKind kind) => kind switch
    {
        SettlementBuildingKind.TownHall => "城鎮大廳", SettlementBuildingKind.Housing => "民居",
        SettlementBuildingKind.Farm => "農田", SettlementBuildingKind.Forestry => "林場",
        SettlementBuildingKind.Dormitory => "雜役宿舍", SettlementBuildingKind.SeniorDormitory => "高級宿舍",
        SettlementBuildingKind.Mine => "礦場", SettlementBuildingKind.Smithy => "鐵匠鋪",
        SettlementBuildingKind.Pharmacy => "藥局", _ => "商鋪"
    };
    public void ConfigureEmployees(string buildingId, int count)
    {
        int index=buildingIds.IndexOf(buildingId);
        if(index<0) throw new ArgumentException("Unknown settlement building");
        buildings.Select(index); employees.Value=count; Command(ActionKind.SetSettlementEmployees);
    }
    public void ChooseBuilding(string buildingId)
    {
        int n=buildingIds.IndexOf(buildingId);if(n<0)throw new ArgumentException("Unknown settlement building");
        buildings.Select(n);UpdateFromControls();
    }
    public void ChooseItem(string item)
    {
        int index=itemIds.IndexOf(item);
        if(index<0) throw new ArgumentException("Unknown settlement item");
        items.Select(index);
    }
    public void SetQuantity(long count) { quantity.Value=count; UpdateFromControls(); }
    public void ChooseBuild(SettlementBuildingKind kind,HexCoord position)
    {
        int type=buildKinds.IndexOf(kind), tile=buildPositions.IndexOf(position);
        if(type<0||tile<0) throw new ArgumentException("Unknown settlement construction choice");
        buildTypes.Select(type);buildTiles.Select(tile);
    }
    public void GiveToSponsor()
    {
        Command(ActionKind.Transfer);
    }
    public void Command(ActionKind kind)
    {
        if (ReadWorld?.Invoke() is not { SettlementRules: not null } w || Selection(w) is not { } s) return;
        var intent=CreateIntent(w,s,kind);
        var error=ActionValidator.Validate(w,intent);
        if(error is not null) { feedbackOrderId=null;feedback.Text="無法執行："+Explain(error); return; }
        if(pendingOrders.Count>=50)pendingOrders.Remove(pendingOrders.Keys.First());
        pendingOrders[intent.Id]=(s.Id,ActionName(kind));
        feedbackOrderId=intent.Id;
        feedback.Text="已排隊："+ActionName(kind)+"，請推進Tick。";
        Requested?.Invoke(intent);
    }
    private ActionIntent CreateIntent(WorldState w,SettlementState s,ActionKind kind)
    {
        string resource = kind==ActionKind.CreateOrganizationSite?siteKindIds.ElementAtOrDefault(siteKinds.Selected).ToString():
            kind==ActionKind.UpgradeOrganizationSite?SelectedOrganizationSite(w,s):
            kind == ActionKind.BuildSettlement ? buildKinds.ElementAtOrDefault(buildTypes.Selected).ToString() :
            kind is ActionKind.SetSettlementEmployees or ActionKind.UpgradeSettlementBuilding ? buildingIds.ElementAtOrDefault(buildings.Selected) ?? "" :
            kind == ActionKind.UpgradeSettlement ? "upgrade" : itemIds.ElementAtOrDefault(items.Selected) ?? "";
        long count = kind == ActionKind.SetSettlementEmployees ? (long)employees.Value : kind is ActionKind.UpgradeSettlement or ActionKind.BuildSettlement or ActionKind.CreateOrganizationSite or ActionKind.UpgradeOrganizationSite or ActionKind.UpgradeSettlementBuilding ? 1 : (long)quantity.Value;
        return new("settlement-ui:" + w.NextSequence, w.ControlledCharacterId,
            kind==ActionKind.Transfer ? donorIds.ElementAtOrDefault(donors.Selected)??"" : s.Id, resource, count, 0,
            DecisionSource.Player, kind, OrganizationId: kind is ActionKind.SupportSettlement or ActionKind.BuildSettlement or ActionKind.CreateOrganizationSite or ActionKind.UpgradeOrganizationSite ? donorIds.ElementAtOrDefault(donors.Selected) : null,
            SettlementBuildPosition:kind==ActionKind.BuildSettlement&&buildTiles.Selected>=0 ? buildPositions[buildTiles.Selected] : null,
            SettlementShopId:kind is ActionKind.SettlementBuy or ActionKind.SettlementSell?shopIds.ElementAtOrDefault(shops.Selected):null);
    }
    private void RefreshCore(WorldState w,object? identity=null)
    {
        ResetSession(identity);
        RefreshSettlements(w);
        if (w.SettlementRules is not { } r || Selection(w) is not { } s)
        { summary.Text = "尚未啟用聚落。請按上方「聚落營運測試」建立情境。"; buildCost.Text=""; managementTabs.Visible=false; sharedControls.Visible=false;
          foreach(var b in actionButtons.Values){b.Disabled=true;b.TooltipText="請先建立聚落情境。";} return; }
        managementTabs.Visible=true; sharedControls.Visible=true;
        var bs = s.Buildings.Where(b => w.SettlementBuildingRules!.Single(d => d.Kind == b.Kind).RequiresEmployees).ToList();
        if (!buildingIds.SequenceEqual(bs.Select(b => b.Id)))
        { buildings.Clear(); buildingIds.Clear(); foreach (var b in bs) { buildings.AddItem(BuildingName(b.Kind)+"｜"+b.Id); buildingIds.Add(b.Id); } }
        employees.MaxValue=bs.SingleOrDefault(b=>b.Id==buildingIds.ElementAtOrDefault(buildings.Selected))?.EmployeeLimit??r.EmployeeLimit;
        var resources = r.Prices.Select(p => p.Item).Append("spirit-stone").ToList();
        if (!itemIds.SequenceEqual(resources))
        { items.Clear(); itemIds.Clear(); foreach (var id in resources) { items.AddItem(w.Items.Single(i => i.Id == id).Name); itemIds.Add(id); } }
        var organizations = w.Organizations.Where(o => o.LeaderId == w.ControlledCharacterId && o.Id != s.OrganizationId).ToList();
        if (!donorIds.SequenceEqual(organizations.Select(o => o.Id)))
        { donors.Clear(); donorIds.Clear(); foreach (var o in organizations) { donors.AddItem(o.Name); donorIds.Add(o.Id); } }
        var types=w.SettlementConstructionRules?.Select(c=>c.Kind).ToList()??[];
        if(!buildKinds.SequenceEqual(types))
        { buildTypes.Clear();buildKinds.Clear();foreach(var k in types){buildTypes.AddItem(BuildingName(k));buildKinds.Add(k);} }
        if(!buildPositions.SequenceEqual(s.Territory))
        { buildTiles.Clear();buildPositions.Clear();foreach(var c in s.Territory){var tile=w.HexMap!.Tiles.Single(t=>t.Coord==c);buildTiles.AddItem(c+"｜"+(tile.Mountain?"山區":tile.Forest?"森林":"平地"));buildPositions.Add(c);} }
        var selected=w.SettlementConstructionRules?.SingleOrDefault(c=>c.Kind==buildKinds.ElementAtOrDefault(buildTypes.Selected));
        buildCost.Text=selected is null ? "此存檔未啟用新建築規則，請建立新聚落情境。" :
            $"建造成本：組織錢財{selected.Stones}＋木材{selected.Wood}；完工影響力{r.BuildingInfluence}，"+
            (w.SettlementOperationRules is null?"建築與收益歸聚落。":"建築與稅後產出歸出資組織；須先有分舵／總舵及空餘配額。");
        if(donors.Selected>=0)
            buildCost.Text+=$" 出資組織現有：錢財{Inventory.Balance(w,donorIds[donors.Selected],"spirit-stone")}／木材{Inventory.Balance(w,donorIds[donors.Selected],"wood")}";
        var lines = new List<string> {
            $"聚落 Lv.{s.Level}｜人口 {s.Population}/{s.PopulationCapacity}｜幸福度 {s.Happiness}｜僱員 {s.Buildings.Sum(b=>b.EmployeeCount)}/{s.WorkingPopulation}",
            $"聚落錢財 {Inventory.Balance(w,s.OrganizationId,"spirit-stone")}｜居民已領工資 {Inventory.Balance(w,s.ResidentsAccountId,"spirit-stone")}",
            string.Join("｜",r.Prices.Select(p=>$"{w.Items.Single(i=>i.Id==p.Item).Name} {Inventory.Balance(w,s.OrganizationId,p.Item)}（收{p.Buy}／售{p.Sell}）")) };
        lines.AddRange(s.Buildings.Select(b=>$"{BuildingName(b.Kind)}｜員工 {b.EmployeeCount}/{b.EmployeeLimit}｜位置 {b.Position}"));
        if (r.Levels.SingleOrDefault(l=>l.Level==s.Level+1) is { } level)
            lines.Add($"升級 Lv.{level.Level}：人口{level.RequiredPopulation}、錢財{level.Stones}、木材{level.Wood}；目前人口不會自動成長。");
        lines.Add("組織影響力：" + string.Join("、",s.Influence.Select(p=>$"{w.Organizations.Single(o=>o.Id==p.Key).Name} {p.Value}")));
        summary.Text = string.Join("\n",lines.Take(3));
        RefreshOrganizationSites(w,s);RefreshQueries(w,s);
    }
}
