using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private void CreateContentWorld()
    {
        CreateEventDecisionWorld();var world=engine.Snapshot();ContentProfile.EnableFresh(world);
        engine=new(world);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);paused=true;Render();
        notice.Text="R4內容／SQLite測試：新局保存內容版本與設定雜湊，可另存SQLite或JSON；舊檔不轉換。";
    }
    private void SaveSqlite()
    {
        try
        {
            string path=ProjectSettings.GlobalizePath("user://saves/world-"+Guid.NewGuid().ToString("N")+".sqlite");
            WorldStores.ForPath(path).Save(path,engine.Snapshot());lastSave=path;notice.Text="已儲存SQLite："+path;
        }
        catch(Exception error){notice.Text="SQLite存檔失敗，原存檔保留："+error.Message;}
    }
    private void SqliteSmokeTest()
    {
        try
        {
            CreateContentWorld();engine.Submit(new("r4-ui","player","player","method:QingyunFist",6,0,DecisionSource.Player,ActionKind.TrainMortalAttribute));
            engine.AdvanceTo(600);var before=engine.Snapshot();SaveSqlite();
            string path=lastSave??throw new Exception("SQLite save missing");
            if(!path.EndsWith(".sqlite",StringComparison.Ordinal))throw new Exception("Not a SQLite save");
            LoadFromPath(path);Render();
            if(WorldCodec.Digest(before)!=WorldCodec.Digest(engine.Snapshot()))throw new Exception("SQLite UI snapshot mismatch");
            engine.AdvanceTo(3600);Render();
            if(engine.Snapshot().Time!.ActionRuntime!.Actions["r4-ui"].Phase!=ActionPhase.Completed)throw new Exception("SQLite checkpoint did not resume");
            var valid=engine.Snapshot();string invalid=path+".bad.sqlite";System.IO.File.WriteAllText(invalid,"broken");LoadFromPath(invalid);
            if(WorldCodec.Digest(valid)!=WorldCodec.Digest(engine.Snapshot())||!notice.Text.Contains("讀檔失敗"))throw new Exception("Failed load replaced current world");
            GD.Print("SQLITE_UI_SMOKE_PASS: SQLite "+SqliteWorldStore.ProviderVersion+", active checkpoint, picker loader, resume, invalid file preserves world");GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr("SQLITE_UI_SMOKE_FAIL: "+error);GetTree().Quit(1);}
    }
}
