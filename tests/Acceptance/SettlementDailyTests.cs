using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SettlementDailyTests
{
    private static WorldState LegacyCreate(){var w=SettlementScenario.Create();w.SettlementRules=w.SettlementRules! with{DailyOutputOverrides=null};w.SettlementDailyEconomy=w.SettlementDailyEconomy! with{PopulationGrowth=null,Consumption=w.SettlementDailyEconomy!.Consumption.Select(c=>c with{MaximumUnitsPerResident=null}).ToList()};return w;}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Advance(SimulationEngine e,int ticks){for(int n=0;n<ticks;n++)e.Step();}
    private static void Stock(WorldState w,string item,long amount)
    {w.Ledger.Add(new(w.NextSequence++,0,"daily-fixture:"+item,"$genesis",w.Settlements.Single().OrganizationId,item,amount,"Genesis"));}
    private static ActionIntent Intent(WorldState w,ActionKind kind,string item,long amount=1)=>
        new("daily-action:"+w.NextSequence,w.ControlledCharacterId,w.Settlements.Single().Id,item,amount,0,DecisionSource.Player,kind);
    private static void Reject(WorldState w)
    {try{_ = new SimulationEngine(w);}catch(InvalidDataException){return;}throw new Exception("Invalid daily economy accepted");}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("New public farm and forestry produce five per effective employee without changing mine yield",()=>{
            var w=SettlementScenario.Create();var e=new SimulationEngine(w);Advance(e,w.Time!.Rules.TicksPerDay);var after=e.Snapshot();
            Check(after.SettlementDays.Single(d=>d.BuildingId=="farm").GrossQuantity==25,"Farm override not used");
            Check(after.SettlementDays.Single(d=>d.BuildingId=="forestry").GrossQuantity==25,"Forestry override not used");
            Check(w.SettlementRules!.DailyOutput(SettlementBuildingKind.Mine)==w.SettlementRules.DailyOutputPerEmployee,"Mine yield changed");
        }),
        ("Settlement settles fixed calendar days then converts unreserved food and wood by population",()=>{
            var e=new SimulationEngine(LegacyCreate());var w=e.Snapshot();var s=w.Settlements.Single();int day=w.Time!.Rules.TicksPerDay;
            Advance(e,day-1);Check(e.Snapshot().SettlementDays.Count==0&&e.Snapshot().SettlementConsumption.Count==0,"Early settlement");
            var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            Advance(e,day*2+1);Advance(resumed,day*2+1);w=e.Snapshot();
            Check(w.SettlementDays.All(d=>d.Tick%day==0)&&w.SettlementConsumption.Count==6,"Daily boundary or duplicate receipt");
            Check(Inventory.Balance(w,s.OrganizationId,"food")==0&&Inventory.Balance(w,s.OrganizationId,"wood")==20,"Population consumption or upgrade reserve wrong");
            Check(w.SettlementConsumption.Where(r=>r.ResourceId=="food").Sum(r=>r.Quantity)==30&&w.SettlementConsumption.Where(r=>r.ResourceId=="wood").Sum(r=>r.Quantity)==10,"Consumption quantities wrong");
            Check(Inventory.Balance(w,s.OrganizationId,"spirit-stone")==w.SettlementRules!.InitialStones+40,"Consumption income wrong");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(resumed.Snapshot()),"Daily save continuation diverged");
        }),
        ("Population consumption is independent capped by stock and keeps next upgrade materials",()=>{
            var w=LegacyCreate();Stock(w,"food",7);Stock(w,"wood",100);var e=new SimulationEngine(w);Advance(e,w.Time!.Rules.TicksPerDay);var after=e.Snapshot();
            Check(after.SettlementConsumption.Single(r=>r.ResourceId=="food").Quantity==17,"Partial food stock not used correctly");
            Check(after.SettlementConsumption.Single(r=>r.ResourceId=="wood").Quantity==20,"Wood not capped by population");
            Check(after.SettlementConsumption.Sum(r=>r.Income)==37,"One resource incorrectly requires the other");
            Check(Inventory.Balance(after,w.Settlements.Single().OrganizationId,"wood")==90,"Wrong remaining wood");
        }),
        ("Fixed daily public production reflects upgrades without restarting the day timer",()=>{
            var w=LegacyCreate();Stock(w,"wood",100);var e=new SimulationEngine(w);Advance(e,4);
            e.Submit(Intent(e.Snapshot(),ActionKind.UpgradeSettlementBuilding,"forestry"));Check(e.Step().Single().Accepted,"Upgrade rejected");
            Advance(e,w.Time!.Rules.TicksPerDay-5);var after=e.Snapshot();
            Check(after.SettlementDays.Single(d=>d.BuildingId=="forestry").GrossQuantity==20&&after.SettlementDays.All(d=>d.Tick==30),"Upgrade restarted elapsed period");
        }),
        ("Next settlement upgrade cash and wood are protected from trade and building upgrades",()=>{
            var w=LegacyCreate();Stock(w,"wood",20);var s=w.Settlements.Single();
            Check(SettlementDailyEconomy.Available(w,s,s.OrganizationId,"wood")==0&&SettlementDailyEconomy.Reserved(w,s,"spirit-stone")==100,"Reserve not exposed");
            Check(ActionValidator.Validate(w,Intent(w,ActionKind.SettlementBuy,"wood")) is not null,"Shop sold reserved wood");
            Check(ActionValidator.Validate(w,Intent(w,ActionKind.UpgradeSettlementBuilding,"forestry")) is not null,"Building upgrade spent reserved wood");
            var cashLimited=LegacyCreate();var cashAccount=cashLimited.Settlements.Single().OrganizationId;Stock(cashLimited,"wood",100);
            cashLimited.Ledger.Add(new(cashLimited.NextSequence++,0,"reserve-cash-fixture",cashAccount,"consumed","spirit-stone",900,"FixtureExpense"));
            cashLimited.Ledger.Add(new(cashLimited.NextSequence++,0,"reserve-food-fixture","$genesis",cashLimited.ControlledCharacterId,"food",1,"Genesis"));
            Check(SettlementDailyEconomy.Available(cashLimited,cashLimited.Settlements.Single(),cashAccount,"spirit-stone")==0,"Cash reserve not protected");
            Check(ActionValidator.Validate(cashLimited,Intent(cashLimited,ActionKind.SettlementSell,"food")) is not null,"Shop spent reserved cash");
            Check(ActionValidator.Validate(cashLimited,Intent(cashLimited,ActionKind.UpgradeSettlementBuilding,"forestry")) is not null,"Building upgrade spent reserved cash");
            s.Population=30;s.WorkingPopulation=30;var e=new SimulationEngine(w);
            e.Submit(Intent(w,ActionKind.UpgradeSettlement,"upgrade"));Check(e.Step().Single().Accepted,"Reserved assets unavailable for intended upgrade");
            var after=e.Snapshot();s=after.Settlements.Single();
            Check(s.Level==2&&SettlementDailyEconomy.Reserved(after,s,"wood")==50&&SettlementDailyEconomy.Reserved(after,s,"spirit-stone")==300,"Next reserve did not refresh");
            Check(Inventory.Balance(after,s.OrganizationId,"wood")==0,"Upgrade materials not spent exactly once");
        }),
        ("Invalid daily values forged income and altered consumption histories are rejected",()=>{
            var bad=LegacyCreate();bad.SettlementDailyEconomy=new([new("food",0,1),new("wood",1,1)]);Reject(bad);
            var e=new SimulationEngine(LegacyCreate());Advance(e,30);var w=e.Snapshot();
            w.SettlementConsumption[0]=w.SettlementConsumption[0] with{Income=w.SettlementConsumption[0].Income+1};Reject(w);
            w=e.Snapshot();w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"fake-resident-payment","$settlement-residents",w.Settlements.Single().OrganizationId,"spirit-stone",1,"SettlementConsumptionIncome"));Reject(w);
            w=e.Snapshot();w.SettlementConsumption.Add(w.SettlementConsumption[0]);Reject(w);
        }),
        ("Old settlement snapshots do not activate daily consumption or change rolling periods",()=>{
            var w=LegacyCreate();w.SettlementDailyEconomy=null;w.SettlementOperationRules=w.SettlementOperationRules! with{FixedDailySettlement=false};Stock(w,"wood",100);
            var e=new SimulationEngine(w);Advance(e,4);e.Submit(Intent(e.Snapshot(),ActionKind.UpgradeSettlementBuilding,"forestry"));Check(e.Step().Single().Accepted,"Legacy upgrade rejected");
            Advance(e,25);Check(e.Snapshot().SettlementDays.All(d=>d.BuildingId!="forestry")&&e.Snapshot().SettlementConsumption.Count==0,"Old world changed calendar mode");
            Advance(e,5);Check(e.Snapshot().SettlementDays.Single(d=>d.BuildingId=="forestry").Tick==35,"Legacy rolling period changed");
        })
    };
}
