using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SettlementPopulationTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Advance(SimulationEngine e,int ticks){for(int n=0;n<ticks;n++)e.Step();}
    // Remove production only in this fixture so consumption examples use exact available stock.
    private static WorldState World(long food,long wood,long progress=0,int population=20,long threshold=100)
    {
        var w=SettlementScenario.Create();var s=w.Settlements.Single();
        s.Buildings.RemoveAll(b=>b.Kind is SettlementBuildingKind.Farm or SettlementBuildingKind.Forestry);
        s.Population=population;s.WorkingPopulation=population;s.GrowthProgress=progress;
        w.SettlementDailyEconomy=w.SettlementDailyEconomy! with{PopulationGrowth=w.SettlementDailyEconomy!.PopulationGrowth! with{InitialValue=progress,Threshold=threshold}};
        foreach(var(item,amount)in new[]{("food",food),("wood",wood+SettlementDailyEconomy.Reserved(w,s,"wood"))})
            if(amount>0)w.Ledger.Add(new(w.NextSequence++,0,"population-fixture:"+item,"$genesis",s.OrganizationId,item,amount,"Genesis"));
        return w;
    }
    private static void Reject(WorldState w){try{_ = new SimulationEngine(w);}catch(InvalidDataException){return;}throw new Exception("Invalid population state accepted");}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Twenty residents consume twenty one complete groups and gain one growth point",()=>{
            var e=new SimulationEngine(World(21,21));Advance(e,30);var w=e.Snapshot();var s=w.Settlements.Single();
            Check(s.GrowthProgress==1&&s.Population==20&&w.SettlementConsumption.All(c=>c.Quantity==21),"21 group example wrong");
            Check(w.SettlementConsumption.Sum(c=>c.Income)==42&&Inventory.Balance(w,s.OrganizationId,"wood")==20,"Income or reserve changed");
        }),
        ("Forty one available groups are capped at forty and grant twenty growth points",()=>{
            var e=new SimulationEngine(World(41,41));Advance(e,30);var w=e.Snapshot();
            Check(w.SettlementConsumption.All(c=>c.Quantity==40)&&w.Settlements.Single().GrowthProgress==20,"Upper limit wrong");
            Check(Inventory.Balance(w,w.Settlements.Single().OrganizationId,"food")==1,"Excess resources discarded");
        }),
        ("Shortages subtract growth by missing complete groups and never make progress negative",()=>{
            foreach(var(food,wood,initial,expected)in new[]{(0L,0L,50L,30L),(0L,0L,5L,0L),(40L,19L,50L,49L)})
            {var e=new SimulationEngine(World(food,wood,initial));Advance(e,30);var w=e.Snapshot();Check(w.Settlements.Single().GrowthProgress==expected,"Shortage delta wrong");
             Check(w.SettlementPopulationGrowth.Single().ConsumedGroups==Math.Min(food,wood),"Unpaired goods counted as complete groups");}
        }),
        ("Threshold converts progress into residents and workers with configurable overflow",()=>{
            var e=new SimulationEngine(World(21,21,99));Advance(e,30);var s=e.Snapshot().Settlements.Single();
            Check(s.Population==21&&s.WorkingPopulation==21&&s.GrowthProgress==0,"Threshold population award wrong");
            e=new(World(40,40,2,threshold:3));Advance(e,30);s=e.Snapshot().Settlements.Single();
            Check(s.Population==27&&s.GrowthProgress==1,"Configured threshold or surplus lost");
        }),
        ("Full housing retains growth until a paid settlement upgrade makes room",()=>{
            var w=World(200,200,99,population:30);var e=new SimulationEngine(w);Advance(e,30);var s=e.Snapshot().Settlements.Single();
            Check(s.Population==30&&s.GrowthProgress==129,"Housing cap discarded progress");
            e.Submit(new("growth-housing-upgrade",w.ControlledCharacterId,s.Id,"upgrade",1,0,DecisionSource.Player,ActionKind.UpgradeSettlement));
            Check(e.Step().Single().Accepted,"Paid upgrade failed");Advance(e,29);s=e.Snapshot().Settlements.Single();
            Check(s.Population==31&&s.PopulationCapacity==50&&s.GrowthProgress==59,"Retained growth not used after capacity expands");
        }),
        ("Fresh settlement grows over thirty days and save continuation preserves exact progress",()=>{
            var e=new SimulationEngine(SettlementScenario.Create());Advance(e,29);var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            Advance(e,871);Advance(resumed,871);var w=e.Snapshot();var s=w.Settlements.Single();
            Check(s.Population==21&&s.GrowthProgress==36&&s.Level==1&&s.Buildings.All(b=>b.Level==1),"Natural growth or manual-only upgrades changed");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(resumed.Snapshot()),"Growth continuation diverged");
        }),
        ("Invalid consumption ranges growth rules and forged progress histories are rejected",()=>{
            var w=World(21,21);w.SettlementDailyEconomy=w.SettlementDailyEconomy! with{PopulationGrowth=w.SettlementDailyEconomy!.PopulationGrowth! with{Threshold=0}};Reject(w);
            w=World(21,21);w.SettlementDailyEconomy=w.SettlementDailyEconomy! with{Consumption=w.SettlementDailyEconomy!.Consumption.Select(c=>c with{MaximumUnitsPerResident=0}).ToList()};Reject(w);
            var e=new SimulationEngine(World(21,21));Advance(e,30);w=e.Snapshot();w.Settlements.Single().GrowthProgress++;Reject(w);
            w=e.Snapshot();w.SettlementPopulationGrowth.Add(w.SettlementPopulationGrowth[0]);Reject(w);
            w=e.Snapshot();w.SettlementPopulationGrowth.Clear();Reject(w);
        })
    };
}
