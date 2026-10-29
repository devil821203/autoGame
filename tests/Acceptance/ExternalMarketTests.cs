using Cultivation.Domain;
using Cultivation.Simulation;

internal static class ExternalMarketTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static WorldState Fixture()
    {
        var w=SettlementOrganizationScenario.Create();var s=w.Settlements.Single();var pos=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
        int n=w.Characters.FindIndex(c=>c.Id==w.ControlledCharacterId);
        w.Characters[n]=w.Characters[n] with{HexPosition=pos,DemoLocationId=pos.ToString()};
        foreach(var(item,amount)in new[]{("spirit-stone",10000L),("food",1000L)})
            w.Ledger.Add(new(w.NextSequence++,0,"external-market-test:"+item,"$genesis",w.ControlledCharacterId,item,amount,"Genesis"));
        return w;
    }
    private static ActionIntent Intent(WorldState w,string id,ActionKind kind,string item,long count,string? organization=null)=>
        new(id,w.ControlledCharacterId,w.ExternalMarket!.Rules.SettlementId,item,count,0,DecisionSource.Player,kind,OrganizationId:organization);
    private static WorldState Trade(SimulationEngine e,ActionIntent i)
    {
        Check(ActionValidator.Validate(e.Snapshot(),i) is null,"Trade should be valid: "+ActionValidator.Validate(e.Snapshot(),i));
        e.Submit(i);e.Step();e.Step();return e.Snapshot();
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("External market prices reuse configured baselines, exclude currency and round whole batches",()=>{
            var w=Fixture();var prices=w.ExternalMarket!.Rules.BasePrices;
            Check(prices["food"]==1&&prices["wood"]==2&&prices["mineral"]==3&&prices["herb"]==4&&prices["ointment"]==12&&prices["bleeding-powder"]==17&&prices["iron-sword"]==20&&prices["leather-armor"]==23,"Baseline derivation differs");
            Check(!prices.ContainsKey("pill")&&!prices.ContainsKey("contribution")&&!prices.ContainsKey("spirit-stone"),"Forbidden commodity returned");
            Check(ExternalMarketPricing.Total(w,"food",2,false)==1&&ExternalMarketPricing.Total(w,"food",5,false)==4&&ExternalMarketPricing.Total(w,"wood",2,true)==8,"Incorrect spread or rounding");
            Check(ActionValidator.Validate(w,Intent(w,"tiny",ActionKind.ExternalMarketSell,"food",1)) is not null,"Zero-price sale allowed");
        }),
        ("Unlimited market buys and supplies without treasury or stock, with replay protection and save continuation",()=>{
            var w=Fixture();var e=new SimulationEngine(w);long initial=Inventory.Balance(w,w.ControlledCharacterId,"spirit-stone");
            w=Trade(e,Intent(w,"sell-batch",ActionKind.ExternalMarketSell,"food",1000));
            Check(Inventory.Balance(w,w.ControlledCharacterId,"spirit-stone")==initial+800,"Incorrect sale payment");
            w=Trade(e,Intent(w,"buy-batch",ActionKind.ExternalMarketBuy,"wood",1000));
            Check(Inventory.Balance(w,w.ControlledCharacterId,"wood")==1000&&w.ExternalMarket!.Receipts.Count==2,"Infinite supply or receipts missing");
            e.Submit(Intent(w,"buy-batch",ActionKind.ExternalMarketBuy,"wood",1000));e.Step();
            Check(e.Snapshot().ExternalMarket!.Receipts.Count==2,"Duplicate order paid twice");
            var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            e.Step();resumed.Step();Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Market snapshot diverged");
        }),
        ("Market protects location, ownership, finite trader assets and rejects forged issuance",()=>{
            var w=Fixture();Check(ActionValidator.Validate(w,Intent(w,"wrong-owner",ActionKind.ExternalMarketBuy,"wood",1,"willow-gang")) is not null,"Player traded another organization treasury");
            Check(ActionValidator.Validate(w,Intent(w,"too-many",ActionKind.ExternalMarketSell,"food",1001)) is not null,"Oversold trader inventory");
            int n=w.Characters.FindIndex(c=>c.Id==w.ControlledCharacterId);w.Characters[n]=w.Characters[n] with{HexPosition=new HexCoord(1,1)};
            Check(ActionValidator.Validate(w,Intent(w,"remote",ActionKind.ExternalMarketBuy,"wood",1)) is not null,"Remote transaction bypassed arrival");
            w=Fixture();w.Ledger.Add(new(w.NextSequence++,0,"forged-external","$external-market",w.ControlledCharacterId,"food",1,"ExternalMarketSupplied"));
            try{_ = new SimulationEngine(w);}catch(InvalidDataException){return;}
            throw new Exception("Forged market issuance accepted");
        }),
        ("Market equipment is ordinary usable gear and sale preserves provenance",()=>{
            var w=Fixture();var e=new SimulationEngine(w);
            w=Trade(e,Intent(w,"external-sword",ActionKind.ExternalMarketBuy,"iron-sword",1));
            var gear=w.LifeSystem!.Equipment.Single(g=>g.ActionId=="external-sword");
            Check(gear.Quality==EquipmentQuality.Ordinary&&gear.Attack==w.LifeSystem.Recipes.Single(r=>r.Id=="iron-sword").Attack,"Purchased gear invalid");
            w=Trade(e,Intent(w,"resell-sword",ActionKind.ExternalMarketSell,gear.Id,1));
            Check(Inventory.Balance(w,w.ControlledCharacterId,gear.Id)==0&&w.ExternalMarket!.Receipts.Last().TotalPrice==16,"Equipment sale failed");
        }),
        ("NPC organization buys missing construction wood while old snapshots remain inactive",()=>{
            var w=SettlementOrganizationScenario.Create();w.LaborAutonomy=null;var p=w.SettlementOrganizationDemo!;
            p.InitialStock["wood"]=20;
            int n=w.Ledger.FindIndex(e=>e.From=="$genesis"&&e.To==p.Organization.Id&&e.ResourceId=="wood");
            w.Ledger[n]=w.Ledger[n] with{Quantity=20};
            var e=new SimulationEngine(w);for(int day=1;day<=5;day++)e.StepValidatedBatch(day*30,30,validateEveryTick:false);
            w=e.Snapshot();Check(w.ExternalMarket!.Receipts.Any(r=>r.OwnerId==p.Organization.Id&&r.Kind==ActionKind.ExternalMarketBuy&&r.ItemId=="wood"),"Missing autonomous material purchase");
            Check(w.SettlementConstruction.Count==3,"Material purchasing failed to unblock construction");
            w=Fixture();w.ExternalMarket=null;
            Check(ActionValidator.Validate(w,new("old-no-market",w.ControlledCharacterId,p.SettlementId,"wood",1,0,DecisionSource.Player,ActionKind.ExternalMarketBuy)) is not null,"Old world gained market");
        }),
        ("NPC organization automatically sells output and remains funded beyond prior day 41 stoppage",()=>{
            var e=new SimulationEngine(SettlementOrganizationScenario.Create());for(int day=1;day<=50;day++)e.StepValidatedBatch(day*30,30,validateEveryTick:false);
            var w=e.Snapshot();string org=w.SettlementOrganizationDemo!.Organization.Id;
            Check(w.ExternalMarket!.Receipts.Any(r=>r.OwnerId==org&&r.Kind==ActionKind.ExternalMarketSell),"No autonomous organization sale");
            Check(Inventory.Balance(w,org,"spirit-stone")>w.OrganizationDevelopment!.ReserveMoney,"Organization cashflow still exhausted");
            Check(w.Settlements.Single().Buildings.Count(b=>b.OwnerOrganizationId==org&&b.Level==2)==3,"Market prevented development");
        })
    };
}
