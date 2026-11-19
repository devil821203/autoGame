using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private HFlowContainer repairRow=null!;
    private OptionButton repairEquipment=null!,repairProviders=null!;
    private Label repairSummary=null!;
    private Button repairSubmit=null!;
    private string[] repairIds=[],repairProviderIds=[];
    private void CreateRepairControls(VBoxContainer panel)
    {
        repairRow=new();panel.AddChild(repairRow);repairRow.AddChild(new Label{Text="R8裝備修理"});
        repairEquipment=new();repairProviders=new();repairRow.AddChild(repairEquipment);repairRow.AddChild(repairProviders);
        repairSubmit=AddButton(repairRow,"送修（自修／委託）",()=>{
            if(repairIds.Length==0||repairProviderIds.Length==0){notice.Text="沒有可送修的裝備或角色";return;}
            var w=engine.Snapshot();SubmitTracked(new("player-repair:"+w.NextSequence,w.ControlledCharacterId,repairProviderIds[repairProviders.Selected],repairIds[repairEquipment.Selected],1,0,DecisionSource.Player,ActionKind.RepairEquipment));
        });
        repairSummary=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};panel.AddChild(repairSummary);
    }
    private void CreateRepairWorld()
    {
        try{
            var w=EquipmentRepairScenario.Create(playerName.Text);engine=new(w);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);paused=true;
            developer.ResetHistory();actors.Clear();foreach(var a in w.Characters)actors.AddItem(a.Name);Render();
            notice.Text="R8獨立情境：測試裝備附可追溯歷史磨損；選裝備及自修／鍛造師後送修。材料與費用來自本人庫存。";
        }catch(Exception e){notice.Text="R8建立失敗："+e.Message;}
    }
    private void RefreshRepair(WorldState w)
    {
        var state=w.Time?.EquipmentRepair;repairRow.Visible=repairSummary.Visible=state is not null;if(state is null)return;
        string owner=w.ControlledCharacterId;
        string[] ids=w.EquipmentDurability!.LegacyWeapons.Select(g=>g.Id).Concat(w.LifeSystem!.Equipment.Select(g=>g.Id)).Where(id=>Inventory.Balance(w,owner,id)==1).Order(StringComparer.Ordinal).ToArray();
        if(!repairIds.SequenceEqual(ids)){repairEquipment.Clear();repairIds=ids;foreach(string id in ids)repairEquipment.AddItem(id);}
        for(int n=0;n<ids.Length;n++)repairEquipment.SetItemText(n,w.Items.Single(i=>i.Id==ids[n]).Name+" "+EquipmentCondition.Description(w,ids[n]));
        string[] providers=w.Characters.Where(a=>a.Id==owner||a.TestProfession==TestProfession.Smith).Select(a=>a.Id).ToArray();
        if(!repairProviderIds.SequenceEqual(providers)){repairProviders.Clear();repairProviderIds=providers;foreach(string id in providers)repairProviders.AddItem(w.Characters.Single(a=>a.Id==id).Name+(id==owner?"（自修）":"（鍛造師，須同地）"));}
        var r=state.Rules;repairSummary.Text=$"修理{r.DurationMinutes}分鐘／{r.ActionPoints}AP，恢復{r.RestoredDurability}耐久；武器{r.MaterialQuantity}鐵料、防具{r.MaterialQuantity}獸皮，委託另付{r.ServiceFee}錢財。須先卸下裝備。\n"+
            string.Join("\n",state.Work.Select(t=>$"修理中：{t.Intent.ActorId}／{t.Intent.ResourceId}，完成時間{t.DueAt}秒"))+$"\n完成修理{state.Receipts.Count(t=>t.Completed)}次；中斷{state.Receipts.Count(t=>!t.Completed)}次。";
    }
    private void RepairSmokeTest()
    {
        try{
            CreateRepairWorld();if(engine.Snapshot().Time?.EquipmentRepair is null)throw new Exception(notice.Text);
            var w=engine.Snapshot();string id=w.EquipmentDurability!.LegacyWeapons.First(g=>Inventory.Balance(w,w.ControlledCharacterId,g.Id)==1).Id;
            int before=EquipmentCondition.Remaining(w,id)!.Value;
            repairEquipment.Select(Array.IndexOf(repairIds,id));repairProviders.Select(Array.IndexOf(repairProviderIds,w.ControlledCharacterId));repairSubmit.EmitSignal(Button.SignalName.Pressed);
            if(EquipmentCondition.Remaining(engine.Snapshot(),id)!=Math.Min(w.EquipmentDurability.Rules.Maximum,before+w.Time!.EquipmentRepair!.Rules.RestoredDurability))throw new Exception(notice.Text);
            Render();GD.Print("EQUIPMENT_REPAIR_UI_SMOKE_PASS: scenario, timed repair, durability query, refresh");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("EQUIPMENT_REPAIR_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
}
