using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SettlementOwnershipTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    // Historical 10% snapshot fixture retains its original fractional-tax regression expectations.
    private static WorldState Fixture(){var w=SettlementScenario.Create(); w.SettlementTaxRules=w.SettlementTaxRules! with{ProductionRate=.1m};w.SettlementDailyEconomy=null; w.SettlementRules=w.SettlementRules! with{DailyOutputOverrides=null};w.Ledger.Add(new(w.NextSequence++,0,"ownership-fixture","$genesis","sect","wood",200,"Genesis"));return w;}
    private static void Advance(SimulationEngine e,int ticks){for(int n=0;n<ticks;n++)e.Step();}
    private static void Act(SimulationEngine e,ActionKind kind,string resource,string? org=null,HexCoord? pos=null,long quantity=1)
    {
        var w=e.Snapshot();var i=new ActionIntent("owned-test:"+w.NextSequence,w.ControlledCharacterId,w.Settlements.Single().Id,resource,quantity,0,DecisionSource.Player,kind,OrganizationId:org,SettlementBuildPosition:pos);
        e.Submit(i);Check(e.Step().Single().Accepted&&!e.Snapshot().Events.Any(v=>v.ActionId==i.Id&&v.Kind=="TimedCompletionRejected"),"Owned building action failed: "+kind);
    }
    private static SettlementBuilding PrivateFarm(SimulationEngine e)
    {
        Act(e,ActionKind.CreateOrganizationSite,"Branch","sect");var w=e.Snapshot();var anchor=w.HexMap!.Sites.Single(t=>t.Id==w.Settlements.Single().SiteId).Anchor;
        Act(e,ActionKind.BuildSettlement,"Farm","sect",anchor);return e.Snapshot().Settlements.Single().Buildings.Last();
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Fresh settlement tax is thirty percent while saved historical tax snapshots remain unchanged",()=>{
            var fresh=SettlementScenario.Create();Check(fresh.SettlementTaxRules!.ProductionRate==.3m,"Fresh production tax differs from confirmed rate");
            var old=Fixture();var loaded=WorldCodec.Deserialize(WorldCodec.Serialize(old));
            Check(loaded.SettlementTaxRules!.ProductionRate==.1m,"Historical tax snapshot overwritten");
        }),
        ("Public resource buildings automatically run full capacity without employees or wages",()=>{
            var e=new SimulationEngine(Fixture());var w=e.Snapshot();var s=w.Settlements.Single();Advance(e,w.Time!.Rules.TicksPerDay);w=e.Snapshot();
            Check(Inventory.Balance(w,s.OrganizationId,"food")==w.SettlementRules!.EmployeeLimit*w.SettlementRules.DailyOutputPerEmployee,"Public farm not full capacity");
            Check(Inventory.Balance(w,s.OrganizationId,"wood")==w.SettlementRules.EmployeeLimit*w.SettlementRules.DailyOutputPerEmployee,"Public forestry not automatic");
            Check(Inventory.Balance(w,s.ResidentsAccountId,"spirit-stone")==0&&w.Settlements.Single().Buildings.All(b=>b.EmployeeCount==0),"Public building employed residents");
            Act(e,ActionKind.SettlementBuy,"food");Check(Inventory.Balance(e.Snapshot(),w.ControlledCharacterId,"food")==1,"Public shop demanded employees");
        }),
        ("Private output and wages belong to sponsoring organization and production tax carries decimals",()=>{
            var e=new SimulationEngine(Fixture());var farm=PrivateFarm(e);var w=e.Snapshot();var s=w.Settlements.Single();
            Check(farm.OwnerOrganizationId=="sect"&&farm.EmployeeCount==0,"Sponsor ownership missing");
            Act(e,ActionKind.SetSettlementEmployees,farm.Id,quantity:1);Advance(e,w.Time!.Rules.TicksPerDay*2);w=e.Snapshot();
            var receipts=w.SettlementDays.Where(d=>d.BuildingId==farm.Id).ToArray();
            Check(receipts.Length==2&&receipts.All(d=>d.Wages==1&&d.OwnerId=="sect"&&!d.Automatic),"Incorrect private labor");
            Check(Inventory.Balance(w,"sect","food")==4&&w.Settlements.Single().Buildings.Single(b=>b.Id==farm.Id).TaxRemainder==0.4m,"Fractional tax lost");
            e=new(WorldCodec.Clone(w));Advance(e,w.Time!.Rules.TicksPerDay*3);w=e.Snapshot();receipts=w.SettlementDays.Where(d=>d.BuildingId==farm.Id).ToArray();
            Check(receipts.Sum(d=>d.GrossQuantity)==10&&receipts.Sum(d=>d.TaxQuantity)==1&&Inventory.Balance(w,"sect","food")==9,"Net output or cumulative tax incorrect");
            Check(w.Settlements.Single().Buildings.Single(b=>b.Id==farm.Id).TaxRemainder==0,"Tax remainder did not settle");
            Check(w.Ledger.Any(l=>l.ResourceId=="food"&&l.To==s.OrganizationId&&l.Reason=="SettlementProductionTax"&&l.Quantity==1),"Tax did not reach village");
        }),
        ("Resource building upgrade charges its owner adds five capacity without multiplying worker yield",()=>{
            var e=new SimulationEngine(Fixture());var farm=PrivateFarm(e);var w=e.Snapshot();var r=w.SettlementOperationRules!;
            Act(e,ActionKind.UpgradeSettlementBuilding,farm.Id);w=e.Snapshot();var upgraded=w.Settlements.Single().Buildings.Single(b=>b.Id==farm.Id);
            Check(upgraded.Level==2&&upgraded.EmployeeLimit==w.SettlementRules!.EmployeeLimit+r.EmployeesPerLevel,"Private upgrade capacity");
            Check(w.Ledger.Any(l=>l.Reason=="SettlementBuildingUpgrade"&&l.From=="sect"&&l.ResourceId=="wood"&&l.Quantity==r.UpgradeWood),"Wrong upgrade payer");
            Act(e,ActionKind.SetSettlementEmployees,farm.Id,quantity:2);Advance(e,w.Time!.Rules.TicksPerDay);w=e.Snapshot();
            Check(w.SettlementDays.Single(d=>d.BuildingId==farm.Id).GrossQuantity==2*w.SettlementRules!.DailyOutputPerEmployee,"Level multiplied worker output");
        }),
        ("Public building paid upgrade immediately changes automatic full capacity output",()=>{
            var e=new SimulationEngine(Fixture());var w=e.Snapshot();Advance(e,w.Time!.Rules.TicksPerDay);Act(e,ActionKind.UpgradeSettlementBuilding,"forestry");
            w=e.Snapshot();var s=w.Settlements.Single();var b=s.Buildings.Single(b=>b.Id=="forestry");
            Check(b.Level==2&&b.EmployeeLimit==10&&b.EmployeeCount==0,"Public upgrade required employees");
            Check(w.SettlementBuildingUpgrades.Single().OwnerId==s.OrganizationId,"Wrong public upgrade payer");
            Advance(e,w.Time!.Rules.TicksPerDay);w=e.Snapshot();
            Check(w.SettlementDays.Where(d=>d.BuildingId==b.Id).Last().GrossQuantity==b.EmployeeLimit*w.SettlementRules!.DailyOutputPerEmployee,"Public upgrade effect missing");
        }),
        ("Private shop trades sponsor stock and cash while trade tax stays disabled",()=>{
            var initial=Fixture();initial.Ledger.Add(new(initial.NextSequence++,0,"shop-food-fixture","$genesis","sect","food",1,"Genesis"));
            var e=new SimulationEngine(initial);Act(e,ActionKind.CreateOrganizationSite,"Branch","sect");var w=e.Snapshot();var s=w.Settlements.Single();
            var pos=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;Act(e,ActionKind.BuildSettlement,"Shop","sect",pos);
            var shop=e.Snapshot().Settlements.Single().Buildings.Last();Act(e,ActionKind.SetSettlementEmployees,shop.Id,quantity:1);
            w=e.Snapshot();long cash=Inventory.Balance(w,"sect","spirit-stone");
            var buy=new ActionIntent("private-shop-buy",w.ControlledCharacterId,s.Id,"food",1,0,DecisionSource.Player,ActionKind.SettlementBuy,SettlementShopId:shop.Id);
            e.Submit(buy);Check(e.Step().Single().Accepted,"Private shop trade rejected");w=e.Snapshot();
            Check(Inventory.Balance(w,"sect","food")==0&&Inventory.Balance(w,w.ControlledCharacterId,"food")==1&&
                Inventory.Balance(w,"sect","spirit-stone")==cash+w.SettlementRules!.Prices.Single(p=>p.Item=="food").Sell,"Private shop proceeds owner mismatch");
            Check(!w.Ledger.Any(l=>l.ActionId==buy.Id&&l.Reason=="SettlementProductionTax"),"Invented trade tax");
        }),
        ("Public employment and forged tax remainders or rates are rejected",()=>{
            var e=new SimulationEngine(Fixture());var w=e.Snapshot();
            e.Submit(new("forbidden-public-hire",w.ControlledCharacterId,w.Settlements.Single().Id,"farm",1,0,DecisionSource.Player,ActionKind.SetSettlementEmployees));
            Check(!e.Step().Single().Accepted,"Public building hired employees");
            w=e.Snapshot();w.SettlementTaxRules=w.SettlementTaxRules! with{ProductionRate=1.01m};
            try{WorldIntegrity.Validate(w);throw new Exception("Invalid tax rate accepted");}catch(InvalidDataException){}
            e=new(Fixture());var farm=PrivateFarm(e);Act(e,ActionKind.SetSettlementEmployees,farm.Id,quantity:1);
            Advance(e,e.Snapshot().Time!.Rules.TicksPerDay);w=e.Snapshot();int n=w.Settlements.Single().Buildings.FindIndex(b=>b.Id==farm.Id);
            w.Settlements.Single().Buildings[n]=w.Settlements.Single().Buildings[n] with{TaxRemainder=0.8m};
            try{WorldIntegrity.Validate(w);throw new Exception("Forged tax remainder accepted");}catch(InvalidDataException){}
        }),
        ("Established organization site authorizes its chief to build without directly controlling settlement",()=>{
            var e=new SimulationEngine(Fixture());Act(e,ActionKind.CreateOrganizationSite,"Branch","sect");var w=e.Snapshot();var s=w.Settlements.Single();
            int n=w.Organizations.FindIndex(o=>o.Id==s.OrganizationId);w.Organizations[n]=w.Organizations[n] with{LeaderId="npc:01"};
            e=new(w);var anchor=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
            Act(e,ActionKind.BuildSettlement,"Farm","sect",anchor);
            Check(e.Snapshot().Settlements.Single().Buildings.Last().OwnerOrganizationId=="sect","Organization chief could not build under existing site rights");
        })
    };
}
