using Cultivation.Domain;
using Cultivation.Simulation;

internal static class HerbProcurementTests
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static IEnumerable<(string Name,Action Body)> Cases => new (string,Action)[] {
        ("Herb procurement buys only surplus herbs and hides with audited payment",()=>{
            var w=LifeSkillScenario.Create();w.PlayerAutoSchedule=false;var r=w.TestProcurement!;
            Check(r.PrimaryResourceId=="herb"&&r.PrimaryInitialPrice==4,"Wrong new procurement item/price");
            long herbs=Inventory.Balance(w,"sect","herb"),stones=Inventory.Balance(w,"sect","spirit-stone"),pills=Inventory.Balance(w,"sect","pill");
            var e=new SimulationEngine(w);e.Step();var end=e.Snapshot();var bought=end.ProcurementReceipts.Single(p=>p.ResourceId=="herb");
            Check(bought.Quantity==herbs-w.SectFacilities!.ReservedHerbs&&bought.UnitPrice==4&&bought.Payment==bought.Quantity*4,"Wrong reserve or payment");
            Check(Inventory.Balance(end,"sect","herb")==w.SectFacilities.ReservedHerbs&&Inventory.Balance(end,"sect","spirit-stone")==stones+bought.Payment,"Inventory/payment mismatch");
            Check(!end.ProcurementQuotes.Any(q=>q.ResourceId=="pill")&&Inventory.Balance(end,"sect","pill")==pills,"Buyer still purchases pills");
        }),
        ("Alchemy is disabled for new life tests while saved flags remain independent",()=>{
            var w=LifeSkillScenario.Create();Check(!w.AlchemyEnabled,"Alchemy still enabled");
            foreach(var kind in new[]{ActionKind.Brew,ActionKind.SectBrew,ActionKind.AcceptSectWork})
            {
                var i=new ActionIntent("disabled:"+kind,"player",kind==ActionKind.SectBrew?"alchemy":"player",kind==ActionKind.AcceptSectWork?"pill":"herb",1,0,DecisionSource.Player,kind);
                Check(ActionValidator.Validate(w,i)=="Alchemy disabled in this test world","Alchemy entry not blocked: "+kind);
            }
            var legacy=WorldCodec.Clone(w);legacy.AlchemyEnabled=true;
            Check(WorldCodec.Deserialize(WorldCodec.Serialize(legacy)).AlchemyEnabled,"Stored flag silently changed");
            var e=new SimulationEngine(w);for(int n=0;n<20;n++)e.Step(true);
            Check(!e.Snapshot().Events.Any(v=>v.Kind is "BrewSucceeded" or "BrewFailed"),"NPC still brewed pills");
        }),
        ("Herb procurement retains demand adjustment and legacy procurement snapshot",()=>{
            var w=LifeSkillScenario.Create();w.PlayerAutoSchedule=false;w.TestProcurement=w.TestProcurement! with {IntervalTicks=2};
            var e=new SimulationEngine(w);e.Step();e.Step();Check(e.Snapshot().ProcurementQuotes.Single(q=>q.ResourceId=="herb").UnitPrice==3,"Surplus price did not fall");
            e.Step();e.Step();Check(e.Snapshot().ProcurementQuotes.Single(q=>q.ResourceId=="herb").UnitPrice==4,"Shortfall price did not rise");
            var resumed=new SimulationEngine(e.Snapshot());e.Step();resumed.Step();Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Procurement resume changed");
            Check(SectEconomyScenario.Create().TestProcurement!.PrimaryResourceId=="pill","Historical fixture changed before scope confirmation");
        })
    };
}
