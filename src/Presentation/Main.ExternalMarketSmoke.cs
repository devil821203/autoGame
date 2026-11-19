using Godot;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void ExternalMarketSmokeTest()
    {
        try
        {
            CreateVillageOrganizationWorld();var w=engine.Snapshot();var market=w.ExternalMarket!;
            var s=w.Settlements.Single(t=>t.Id==market.Rules.SettlementId);
            var pos=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
            int n=w.Characters.FindIndex(c=>c.Id==w.ControlledCharacterId);
            w.Characters[n]=w.Characters[n] with{HexPosition=pos,DemoLocationId=pos.ToString()};
            engine=new(w);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);Render();
            externalMarket.ChooseItem("food",2);
            if(!externalMarket.QuoteText.Contains("購買總價 4"))throw new Exception("External market batch quote missing");
            long stock=Inventory.Balance(w,w.ControlledCharacterId,"food");
            externalMarket.Purchase();StepTicks(2);w=engine.Snapshot();
            if(Inventory.Balance(w,w.ControlledCharacterId,"food")!=stock+2)throw new Exception("External market UI purchase failed");
            externalMarket.ChooseItem("food",2);externalMarket.Sell();StepTicks(2);w=engine.Snapshot();
            if(Inventory.Balance(w,w.ControlledCharacterId,"food")!=stock||w.ExternalMarket!.Receipts.Count(r=>r.ActorId==w.ControlledCharacterId)!=2)throw new Exception("External market UI sale failed");
            string digest=WorldCodec.Digest(w);Save();Load();Render();
            if(WorldCodec.Digest(engine.Snapshot())!=digest)throw new Exception("External market UI save/load diverged");
            settlementWindow.Hide();GD.Print("EXTERNAL_MARKET_UI_SMOKE_PASS: batch quotes, buy/sell buttons, stock, receipts, save/load");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
