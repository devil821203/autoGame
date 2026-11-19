using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private void CreateLifeEventWorld()
    {
        var w=LifeEventScenario.Create(playerName.Text);engine=new(w);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);paused=true;
        developer.ResetHistory();actors.Clear();foreach(var a in w.Characters)actors.AddItem(a.Name);Render();notice.Text="工作、旅行、恢復、再生、發薪與徵集任務到期由秒事件處理；請開新局，聚落／考核等週期與同秒依賴仍待轉接。";
    }
    private void MissionExpirySmoke()
    {
        try{
            CreateLifeEventWorld();var w=engine.Snapshot();int n=w.Characters.FindIndex(a=>a.Id=="player");var a=w.Characters[n];
            w.Characters[n]=a with{DemoLocationId="sect",HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect")};engine=new(w);
            SubmitTracked(new("ui-mission-expiry","player","sect","herb",1,0,DecisionSource.Player,ActionKind.PublishMission,Offer:new("ui-expiry",1,4,8,4)));
            engine.AdvanceTo(4800);Render();w=engine.Snapshot();
            if(w.Missions.Single(m=>m.Id=="ui-expiry").Status!=MissionStatus.Expired||w.Ledger.Count(l=>l.ActionId=="expire:ui-expiry"&&l.Quantity==4)!=1)throw new Exception(notice.Text);
            GD.Print("MISSION_EXPIRY_UI_SMOKE_PASS: latest world, publish, exact expiry, single refund, UI refresh");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("MISSION_EXPIRY_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
    private void SocialExpirySmoke()
    {
        try{
            CreateLifeEventWorld();var w=engine.Snapshot();w.SocialRules=w.SocialRules! with{OfferLifetimeTicks=3};engine=new(w);
            engine.AdvanceTo(1020);SubmitTracked(new("ui-social-expiry","player","npc:00","social",1,0,DecisionSource.Player,ActionKind.InviteCooperation));
            engine.AdvanceTo(3419);Render();if(engine.Snapshot().SocialOffers.Single(o=>o.Id=="ui-social-expiry").Status!=SocialOfferStatus.Pending)throw new Exception("Early social expiry");
            engine.AdvanceTo(3420);Render();if(engine.Snapshot().SocialOffers.Single(o=>o.Id=="ui-social-expiry").Status!=SocialOfferStatus.Expired)throw new Exception("Missing social expiry");
            GD.Print("SOCIAL_EXPIRY_UI_SMOKE_PASS: off-grid invitation, exact lifetime, expiry, UI refresh");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("SOCIAL_EXPIRY_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
    private void LifeEventSmoke()
    {
        try{
            CreateLifeEventWorld();engine.AdvanceTo(1020);SubmitTracked(new("ui-precise-life","player","player","ointment",1,0,DecisionSource.Player,ActionKind.CraftLifeItem));Render();
            if(engine.WorldTimeSeconds!=2820||!engine.Snapshot().LifeSystem!.Receipts.Any(r=>r.ActionId=="ui-precise-life"))throw new Exception(notice.Text);
            GD.Print("LIFE_EVENTS_UI_SMOKE_PASS: scenario, off-grid crafting, exact interactive stop, refresh");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("LIFE_EVENTS_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
    private void ScheduledEventSmoke()
    {
        try{
            CreateLifeEventWorld();var w=engine.Snapshot();var at=HexNavigation.Position(w.HexMap!,w.Characters.Single(a=>a.Id=="player"));
            var tile=w.HexMap!.Tiles.First(t=>at.Neighbors().Contains(t.Coord)&&HexNavigation.Passable(t)&&RegionGeometry.Allows(w.HexMap,at,t.Coord)&&HexNavigation.Cost(w.HexMap,t)==1);
            engine.AdvanceTo(1020);SubmitTracked(new("ui-travel-seconds","player",tile.Coord.ToString(),"travel",1,0,DecisionSource.Player,ActionKind.Travel));Render();
            if(engine.WorldTimeSeconds!=1620||engine.Snapshot().Characters.Single(a=>a.Id=="player").HexPosition!=tile.Coord)throw new Exception(notice.Text);
            GD.Print("SCHEDULED_EVENTS_UI_SMOKE_PASS: scenario, off-grid travel, exact interactive stop, map refresh");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("SCHEDULED_EVENTS_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
    private void RecoveryEventSmoke()
    {
        try{
            CreateLifeEventWorld();var w=engine.Snapshot();int n=w.Characters.FindIndex(a=>a.Id=="player");var a=w.Characters[n];
            w.Characters[n]=a with{Health=80,DemoLocationId="sect",HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect"),Stamina=a.Stamina! with{Current=50,HasBed=false}};
            int b=w.Time!.Budgets.FindIndex(x=>x.ActorId=="player");if(b<0)w.Time.Budgets.Add(new("player",0,28));else w.Time.Budgets[b]=new("player",0,28);
            engine=new(w);engine.AdvanceTo(1020);SubmitTracked(new("ui-seconds-rest","player","player","stamina",1,0,DecisionSource.Player,ActionKind.Rest));Render();
            var final=engine.Snapshot();a=final.Characters.Single(a=>a.Id=="player");
            if(engine.WorldTimeSeconds!=2220||a.Health!=100||a.Stamina!.Current!=100||a.Stamina.Resting)throw new Exception(notice.Text);
            GD.Print("RECOVERY_EVENTS_UI_SMOKE_PASS: scenario, off-grid rest, exact final pulse, UI refresh");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("RECOVERY_EVENTS_UI_SMOKE_FAIL: "+e);GetTree().Quit(1);}
    }
}
