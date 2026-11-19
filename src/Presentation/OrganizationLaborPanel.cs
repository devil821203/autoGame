using Godot;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class OrganizationLaborPanel : VBoxContainer
{
    public Func<WorldState>? ReadWorld {get;set;}
    public event Action<ActionIntent>? Requested;
    private readonly OptionButton places=new(),owners=new(),buildings=new();
    private readonly SpinBox workers=new(){MinValue=0,MaxValue=int.MaxValue,Step=1};
    private readonly Label overview=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Label hint=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly List<string> placeIds=[],ownerIds=[],buildingIds=[];
    private readonly Dictionary<ActionKind,Button> commands=[];
    private bool rendering;
    public string DetailsText=>overview.Text;
    public override void _Ready()
    {
        AddChild(places);AddChild(owners);AddChild(overview);
        var recruits=new HBoxContainer();AddChild(recruits);
        Add(recruits,"投入人口資源（抽一次）",ActionKind.InvestSettlementPopulation);
        Add(recruits,"招攬一名雜役",ActionKind.RecruitOrganizationLabor);Add(recruits,"招攬一名成員",ActionKind.RecruitOrganizationMember);
        var assign=new HBoxContainer();AddChild(assign);assign.AddChild(buildings);assign.AddChild(workers);
        Add(assign,"設定雜役數",ActionKind.SetOrganizationLabor);Add(assign,"升級所選建築",ActionKind.UpgradeSettlementBuilding);
        AddChild(new Label(){Text="宿舍在「聚落營運→建造與援助」興建，占組織建築配額；先建立分舵／總舵。調整居民僱員仍在原建築頁操作。",AutowrapMode=TextServer.AutowrapMode.WordSmart});
        AddChild(hint);
        places.ItemSelected+=_=>RefreshControls();owners.ItemSelected+=_=>RefreshControls();buildings.ItemSelected+=_=>RefreshControls();workers.ValueChanged+=_=>RefreshControls();
    }
    private void Add(HBoxContainer row,string name,ActionKind kind)
    {var b=new Button(){Text=name};commands[kind]=b;row.AddChild(b);b.Pressed+=()=>Command(kind);}
    private void RefreshControls(){if(!rendering&&ReadWorld?.Invoke() is { } w)Refresh(w);}
    public void ChooseOrganization(string id){int n=ownerIds.IndexOf(id);if(n<0)throw new ArgumentException("Unknown organization");owners.Select(n);RefreshControls();}
    public void ChooseBuilding(string id,int count){int n=buildingIds.IndexOf(id);if(n<0)throw new ArgumentException("Unknown building");buildings.Select(n);workers.Value=count;RefreshControls();}
    public void Command(ActionKind kind)
    {
        if(ReadWorld?.Invoke() is not { } w||Create(w,kind) is not { } i)return;
        string? error=ActionValidator.Validate(w,i);if(error is not null){hint.Text=error;return;}
        Requested?.Invoke(i);hint.Text="指令已排隊，推進Tick結算；完成事件及扣款請查看帳本。";
    }
    private ActionIntent? Create(WorldState w,ActionKind kind)
    {
        string? place=placeIds.ElementAtOrDefault(places.Selected),owner=ownerIds.ElementAtOrDefault(owners.Selected);
        if(place is null||owner is null)return null;
        string resource=kind==ActionKind.InvestSettlementPopulation?"population":kind is ActionKind.SetOrganizationLabor or ActionKind.UpgradeSettlementBuilding?buildingIds.ElementAtOrDefault(buildings.Selected)??"":"recruit";
        return new("labor-ui:"+w.NextSequence,w.ControlledCharacterId,place,resource,kind==ActionKind.SetOrganizationLabor?(long)workers.Value:1,0,DecisionSource.Player,kind,
            OrganizationId:kind==ActionKind.UpgradeSettlementBuilding?null:owner);
    }
    private static void Options(OptionButton button,List<string> ids,IEnumerable<(string Id,string Name)> next)
    {
        string? selected=ids.ElementAtOrDefault(button.Selected);var values=next.ToArray();
        if(!ids.SequenceEqual(values.Select(x=>x.Id))){button.Clear();ids.Clear();foreach(var x in values){ids.Add(x.Id);button.AddItem(x.Name);}}
        if(ids.Count>0)button.Select(Math.Max(0,selected is null?-1:ids.IndexOf(selected)));
    }
    public void Refresh(WorldState w)
    {
        rendering=true;
        try
        {
            Options(places,placeIds,w.Settlements.Select(s=>(s.Id,w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Name)));
            Options(owners,ownerIds,w.Organizations.Where(o=>o.LeaderId==w.ControlledCharacterId).Select(o=>(o.Id,o.Name)));
            var s=w.Settlements.SingleOrDefault(s=>s.Id==placeIds.ElementAtOrDefault(places.Selected));
            string? owner=ownerIds.ElementAtOrDefault(owners.Selected);
            Options(buildings,buildingIds,s?.Buildings.Where(b=>b.OwnerOrganizationId==owner&&(w.SettlementRules!.Outputs.ContainsKey(b.Kind)||b.Kind is SettlementBuildingKind.Dormitory or SettlementBuildingKind.SeniorDormitory))
                .Select(b=>(b.Id,$"{b.Kind} Lv.{b.Level}｜居民{b.EmployeeCount}＋雜役{b.LaborCount}/{b.EmployeeLimit}"))??[]);
            if(w.OrganizationLaborRules is not { } r||w.MemberRecruitmentRules is null||s is null||owner is null)
                overview.Text="尚未啟用組織人力，請新建聚落情境；舊存檔保留原規則。";
            else
            {
                var investment=w.PopulationInvestmentRules!;
                overview.Text=$"人口 {s.Population}/{s.PopulationCapacity}；每次投入 {string.Join("＋",investment.ResourcesPerGroup.Select(p=>$"{p.Value*investment.GroupsPerAttempt}{w.Items.Single(i=>i.Id==p.Key).Name}"))}，成功率{investment.SuccessProbability:P0}，成功人口＋{investment.PopulationPerSuccess}，組織影響力依原材料設定計算。\n"+
                    $"組織錢財 {Inventory.Balance(w,owner,"spirit-stone")}；成員{w.Characters.Count(c=>c.OrganizationId==owner)}/{OrganizationCapacity.MemberLimit(w,owner)}；雜役{OrganizationCapacity.LaborCount(w,owner)}/{OrganizationCapacity.LaborLimit(w,owner)}，已派工{OrganizationCapacity.Assigned(w,owner)}。\n"+
                    $"招雜役{r.RecruitmentCost}、每日每名俸祿{r.DailySalary}（未派工也支付）；招成員{w.MemberRecruitmentRules.Options.MemberCost}全數轉給成員，另有設定範圍的隨機初始錢財及物資。";
            }
            foreach(var (kind,button)in commands)
            {var i=Create(w,kind);var error=i is null?"請選擇聚落與本人管理的組織":ActionValidator.Validate(w,i);button.Disabled=error is not null;button.TooltipText=error??"可執行";}
        }
        finally{rendering=false;}
    }
}
