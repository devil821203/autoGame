using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class LifeSkillPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private Label info=null!,status=null!;
    private OptionButton patients=null!,recipes=null!,equipment=null!;
    private readonly List<string> patientIds=[],recipeIds=[],gearIds=[];
    private readonly List<(Button Button,ActionKind Kind)> controls=[];
    public string DisplayText=>info.Text+"\n"+status.Text;
    public override void _Ready()
    {
        AddChild(new Label {Text="醫術／製藥／鍛造｜先建立「生活技能測試」；玩家僅操作本人。普通製作不耗靈力。"});
        info=new Label {AutowrapMode=TextServer.AutowrapMode.WordSmart};AddChild(info);
        var medical=new HFlowContainer();AddChild(medical);patients=new OptionButton();medical.AddChild(patients);
        Add(medical,"藥草治療",ActionKind.MedicalTreatment);Add(medical,"重傷療程",ActionKind.TreatSevereInjury);
        var production=new HFlowContainer();AddChild(production);recipes=new OptionButton();production.AddChild(recipes);Add(production,"製作選中配方",ActionKind.CraftLifeItem);
        var medicines=new HFlowContainer();AddChild(medicines);Add(medicines,"使用藥膏（非戰鬥）",ActionKind.UseMedicine);
        var powder=new Button {Text="使用止血散（非戰鬥）"};medicines.AddChild(powder);powder.Pressed+=()=>Command(ActionKind.UseMedicine,"bleeding-powder");
        Add(medicines,"戰鬥使用止血散（取代本輪攻擊）",ActionKind.UseCombatMedicine);
        var gear=new HFlowContainer();AddChild(gear);equipment=new OptionButton();gear.AddChild(equipment);Add(gear,"穿戴",ActionKind.EquipCraftedItem);Add(gear,"卸下",ActionKind.UnequipCraftedItem);
        status=new Label {AutowrapMode=TextServer.AutowrapMode.WordSmart};AddChild(status);
        patients.ItemSelected+=_=>Refresh(ReadWorld());recipes.ItemSelected+=_=>Refresh(ReadWorld());equipment.ItemSelected+=_=>Refresh(ReadWorld());
    }
    private void Add(Container row,string name,ActionKind kind)
    {
        var b=new Button {Text=name};row.AddChild(b);controls.Add((b,kind));b.Pressed+=()=>Command(kind);
    }
    private ActionIntent Intent(WorldState w,ActionKind kind,string? item=null)
    {
        string target=kind is ActionKind.MedicalTreatment or ActionKind.TreatSevereInjury&&patients.Selected>=0&&patients.Selected<patientIds.Count?patientIds[patients.Selected]:w.ControlledCharacterId;
        string resource=item??(kind switch {ActionKind.MedicalTreatment=>"herb",ActionKind.TreatSevereInjury=>"severe-injury",ActionKind.CraftLifeItem=>recipes.Selected>=0&&recipes.Selected<recipeIds.Count?recipeIds[recipes.Selected]:"",
            ActionKind.UseMedicine=>"ointment",ActionKind.UseCombatMedicine=>"bleeding-powder",_=>equipment.Selected>=0&&equipment.Selected<gearIds.Count?gearIds[equipment.Selected]:""});
        return new("ui-life:"+w.NextSequence,w.ControlledCharacterId,target,resource,1,0,DecisionSource.Player,kind);
    }
    public void Command(ActionKind kind,string? item=null)=>Requested?.Invoke(Intent(ReadWorld(),kind,item));
    public void SelectRecipe(string id){int n=recipeIds.IndexOf(id);if(n>=0)recipes.Select(n);}
    public void SelectEquipment(string id){int n=gearIds.IndexOf(id);if(n>=0)equipment.Select(n);}
    private static void Bind(OptionButton box,List<string> ids,IEnumerable<(string Id,string Name)> entries)
    {
        var next=entries.ToArray();string? selected=box.Selected>=0&&box.Selected<ids.Count?ids[box.Selected]:null;
        if(ids.SequenceEqual(next.Select(e=>e.Id))){for(int row=0;row<next.Length;row++)box.SetItemText(row,next[row].Name);return;}
        ids.Clear();box.Clear();foreach(var e in next){ids.Add(e.Id);box.AddItem(e.Name);}int n=selected is null?-1:ids.IndexOf(selected);if(n>=0)box.Select(n);
    }
    public void Refresh(WorldState w)
    {
        if(info is null)return;
        if(w.LifeSystem is not {} life){info.Text="此情境尚未啟用生活技能，請建立生活技能測試。";foreach(var c in controls)c.Button.Disabled=true;return;}
        var a=w.Characters.Single(c=>c.Id==w.ControlledCharacterId);var s=a.LifeSkills??new();var r=w.LifeSkillRules!;
        info.Text=$"煉丹：{(w.AlchemyEnabled?"啟用":"停用")}｜{a.Name}｜生命{a.Health}｜重傷{(a.SeverelyInjured?"是":"否")}｜醫術Lv.{LifeSkillCalculator.Level(s,LifeSkillKind.Medicine,r)}／製藥Lv.{LifeSkillCalculator.Level(s,LifeSkillKind.Pharmacy,r)}／鍛造Lv.{LifeSkillCalculator.Level(s,LifeSkillKind.Smithing,r)}\n"+
            string.Join("｜",new[]{"herb","ointment","bleeding-powder","iron-material","beast-hide"}.Select(id=>$"{w.Items.Single(i=>i.Id==id).Name} {Inventory.Balance(w,a.Id,id)}"));
        Bind(patients,patientIds,new[]{a}.Concat(w.Characters.Where(c=>c.Id!=a.Id&&c.RegionId==a.RegionId&&c.DemoLocationId==a.DemoLocationId&&c.HexPosition==a.HexPosition)).Select(c=>(c.Id,c.Name)));
        Bind(recipes,recipeIds,life.Recipes.Select(d=>(d.Id,$"{w.Items.Single(i=>i.Id==d.Output).Name}／{GameTimeDisplay.Duration(w,d.Ticks)}／{string.Join("、",d.Inputs.Select(p=>$"{p.Key}×{p.Value}"))}")));
        Bind(equipment,gearIds,life.Equipment.Where(g=>Inventory.Balance(w,a.Id,g.Id)==1).Select(g=>(g.Id,$"{w.Items.Single(i=>i.Id==g.Id).Name} 攻+{g.Attack}防+{g.Defense}｜{EquipmentCondition.Description(w,g.Id)}")));
        foreach(var c in controls){string? error=ActionValidator.Validate(w,Intent(w,c.Kind));c.Button.Disabled=error is not null;c.Button.TooltipText=error??"可提交，材料與AP於開始時驗證";}
        status.Text="工作："+string.Join("；",life.Work.Where(t=>t.Intent.ActorId==a.Id).Select(t=>$"{t.Intent.Kind} 剩{GameTimeDisplay.Duration(w,t.DueTick-w.Clock.Tick)}"))+"\n藥效："+
            string.Join("；",life.Effects.Where(e=>e.ActorId==a.Id).Select(e=>$"每次{e.Healing}，剩{e.Remaining}次（{(e.BattleId is null?GameTimeDisplay.Duration(w,1):"戰鬥輪末")}）"));
    }
}
