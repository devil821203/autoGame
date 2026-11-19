using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private HFlowContainer controlRow=null!;
    private OptionButton controlChoices=null!;
    private Label controlSummary=null!;
    private Button switchControl=null!,revokeControl=null!;
    private string[] controlIds=[];
    private void CreatePlayerControlControls(VBoxContainer panel)
    {
        controlRow=new HFlowContainer();panel.AddChild(controlRow);controlRow.AddChild(new Label{Text="R6b可控資格（與角色觀察清單分開）"});
        controlChoices=new OptionButton();controlRow.AddChild(controlChoices);controlChoices.ItemSelected+=_=>RefreshPlayerControlControls(engine.Snapshot());
        switchControl=AddButton(controlRow,"切換控制角色",SwitchSelectedControl);revokeControl=AddButton(controlRow,"撤銷所選資格（測試）",RevokeSelectedControl);
        controlSummary=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart};panel.AddChild(controlSummary);
    }
    private void CreatePlayerControlWorld()
    {
        try
        {
            if(!ulong.TryParse(regionalSeed.Text,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out ulong seed))throw new ArgumentException("Seed須為非負整數");
            var w=PlayerControlScenario.Create(playerName.Text,seed);engine=new(w);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);paused=true;
            developer.ResetHistory();actors.Clear();foreach(var a in w.Characters)actors.AddItem(a.Name);Render();
            notice.Text="R6b新情境：config指定兩名既有角色的資格；免費暫停切換，工作與資產留在原角色。撤銷按鈕僅供此模式驗收。";
        }
        catch(Exception error){notice.Text="R6b建立失敗，保留目前世界："+error.Message;}
    }
    private void RefreshPlayerControlControls(WorldState w)
    {
        var control=w.Time?.PlayerControl;controlRow.Visible=controlSummary.Visible=control is not null;if(control is null)return;
        string[] ids=control.Grants.Keys.OrderBy(id=>id,StringComparer.Ordinal).ToArray();
        if(!controlIds.SequenceEqual(ids)){controlChoices.Clear();controlIds=ids;foreach(string id in ids)controlChoices.AddItem(id);}
        for(int n=0;n<ids.Length;n++){
            var a=w.Characters.Single(a=>a.Id==ids[n]);var g=control.Grants[a.Id];
            controlChoices.SetItemText(n,a.Name+"｜"+(g.RevokedAt is not null?"已撤銷":a.Health==0?"失能":"有資格")+(a.Id==w.ControlledCharacterId?"（目前選定）":""));
        }
        string? selected=controlChoices.Selected>=0?ids[controlChoices.Selected]:null;
        switchControl.Disabled=!paused||selected is null||!PlayerControl.CanSelect(w,selected)||selected==w.ControlledCharacterId||PlayerControl.InCombat(w,w.ControlledCharacterId)||PlayerControl.InCombat(w,selected);
        revokeControl.Disabled=!paused||selected is null||control.Grants[selected].RevokedAt is not null;
        int eligible=w.Characters.Count(a=>PlayerControl.CanSelect(w,a.Id));
        controlSummary.Text=engine.RequiresControlSelection?(eligible==0?"無可控角色：世界已暫停。可使用上方讀檔／開新存檔。":"目前角色無法控制：世界已暫停，請手動選擇有效角色。"):
            "目前控制："+w.Characters.Single(a=>a.Id==w.ControlledCharacterId).Name+"｜切換不耗時間／AP；既有工作照常保留，切換後自主排程關閉。";
        if(selected is not null){var g=control.Grants[selected];controlSummary.Text+=$"\n資格來源 {g.EligibilityReason}｜授予 {g.GrantedAt}秒｜撤銷 {(g.RevokedAt?.ToString()??"無")}｜{g.RevocationReason??""}";}
    }
    private void SwitchSelectedControl()
    {
        if(controlChoices.Selected<0)return;string id=controlIds[controlChoices.Selected];var result=engine.SwitchControlledCharacter(id,paused);
        if(result.Accepted){paused=true;var w=engine.Snapshot();actors.Select(w.Characters.FindIndex(a=>a.Id==id));}
        notice.Text=result.Reason;Render();
    }
    private void RevokeSelectedControl()
    {
        if(!paused||controlChoices.Selected<0)return;
        engine.RevokeControlGrant(controlIds[controlChoices.Selected],"R6b介面驗收撤銷");if(engine.RequiresControlSelection)paused=true;Render();
    }
    private void SelectControl(string id){int index=Array.IndexOf(controlIds,id);if(index<0)throw new InvalidOperationException("Control choice missing");controlChoices.Select(index);RefreshPlayerControlControls(engine.Snapshot());}
    private void PlayerControlSmokeTest()
    {
        try
        {
            CreatePlayerControlWorld();var original=engine.Snapshot();if(original.Time?.PlayerControl is null)throw new Exception("R6b mode missing");
            int observer=original.Characters.FindIndex(a=>a.Id=="npc:03");actors.Select(observer);Render();if(engine.Snapshot().ControlledCharacterId!="player")throw new Exception("Observer granted control");
            string other=original.Time.PlayerControl.Rules.InitialCharacters.Single(id=>id!="player");SelectControl(other);SwitchSelectedControl();var switched=engine.Snapshot();
            if(switched.ControlledCharacterId!=other||!paused||switched.PlayerAutoSchedule||switched.Clock!=original.Clock||!status.Text.Contains("R6b目前控制"))throw new Exception("Switch UI wrong");
            SelectControl("player");RevokeSelectedControl();SelectControl(other);RevokeSelectedControl();
            var none=engine.Snapshot();AdvanceSimulation(10);StepTicks(1);TogglePause();
            if(!paused||!engine.RequiresControlSelection||!controlSummary.Text.Contains("無可控角色")||engine.Snapshot().Clock!=none.Clock)throw new Exception("No-control UI advanced time");
            SaveSqlite();string path=lastSave??throw new Exception("No saved control state");LoadFromPath(path);Render();if(!engine.RequiresControlSelection||!controlSummary.Text.Contains("無可控角色"))throw new Exception("Load lost revoked control");
            StartNewSave();Render();if(engine.RequiresControlSelection||engine.Snapshot().Time!.PlayerControl!.Grants.Values.Any(g=>g.RevokedAt is not null))throw new Exception("New game kept revocations");
            GD.Print("PLAYER_CONTROL_UI_SMOKE_PASS: observer isolation, qualified switch, revocation, no-control pause, SQLite, fresh reset");GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr("PLAYER_CONTROL_UI_SMOKE_FAIL: "+error);GetTree().Quit(1);}
    }
}
