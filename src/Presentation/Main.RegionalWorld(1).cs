using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private LineEdit regionalSeed=null!;
    private void CreateRegionalWorld()
    {
        try
        {
            if(!ulong.TryParse(regionalSeed.Text,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out ulong seed))throw new ArgumentException("Seed須為非負整數");
            var fresh=RegionalWorldScenario.Create(playerName.Text,seed);var replacement=new SimulationEngine(fresh);
            engine=replacement;pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);paused=true;developer.ResetHistory();actors.Clear();
            foreach(var actor in fresh.Characters)actors.AddItem(actor.Name);
            var tabs=(TabContainer)hexMap.GetParent();tabs.CurrentTab=tabs.GetTabIdxFromControl(hexMap);Render();
            notice.Text=$"R6a新世界：{fresh.HexMap!.Regional!.Regions.Count}地區／{fresh.HexMap.Tiles.Count}格，Seed {seed}；由地圖選擇地區與目的地，沿用旅行AP。";
        }
        catch(Exception error){notice.Text="R6a建立失敗，保留目前世界："+error.Message;}
    }
    private void RegionalWorldSmokeTest()
    {
        try
        {
            CreateRegionalWorld();var initial=engine.Snapshot();var topology=initial.HexMap?.Regional??throw new Exception("R6a world not created");
            var destination=topology.Regions[1];hexMap.ChooseRegion(destination.Id);hexMap.Travel();Render();
            var arrived=engine.Snapshot();var player=arrived.Characters.Single(a=>a.Id==arrived.ControlledCharacterId);
            if(player.RegionId!=destination.Id||player.HexPosition!=destination.Town||!paused)throw new Exception("R6a map travel did not arrive/pause");
            hexMap.ChooseSite("region:"+destination.Id+":settlement");hexMap.EnterSelectedSite();
            SaveSqlite();string path=lastSave??throw new Exception("R6a save missing");var saved=engine.Snapshot();LoadFromPath(path);
            if(WorldCodec.Digest(saved)!=WorldCodec.Digest(engine.Snapshot()))throw new Exception("R6a UI save changed world");
            string digest=WorldCodec.Digest(engine.Snapshot());regionalSeed.Text="invalid";CreateRegionalWorld();
            if(WorldCodec.Digest(engine.Snapshot())!=digest||!notice.Text.Contains("建立失敗"))throw new Exception("Invalid seed reset world");
            GD.Print("REGIONAL_WORLD_UI_SMOKE_PASS: region map selection, real action-driven travel, local scene, SQLite, invalid seed isolation");GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr("REGIONAL_WORLD_UI_SMOKE_FAIL: "+error);GetTree().Quit(1);}
    }
}
