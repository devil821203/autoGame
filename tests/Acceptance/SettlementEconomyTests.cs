using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SettlementEconomyTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static WorldState LegacyCreate() { var w=SettlementScenario.Create(); w.SettlementDailyEconomy=null; w.SettlementRules=w.SettlementRules! with{DailyOutputOverrides=null}; w.OrganizationLaborRules=null; w.MemberRecruitmentRules=null; w.PopulationInvestmentRules=null; w.SettlementOperationRules=null; w.SettlementTaxRules=null; return w; }
    private static ActionIntent Order(WorldState w, ActionKind kind, string resource, long quantity = 1, string? donor = null) =>
        new("settlement-test:" + w.NextSequence, w.ControlledCharacterId, w.Settlements.Single().Id,
            resource, quantity, 0, DecisionSource.Player, kind, OrganizationId: donor);
    private static void Act(SimulationEngine e, ActionKind kind, string resource, long quantity = 1, string? donor = null)
    {
        var w = e.Snapshot(); var order = Order(w, kind, resource, quantity, donor); e.Submit(order);
        var result = e.Step().Single(r => r.IntentId == order.Id);
        Check(result.Accepted && !e.Snapshot().Events.Any(v => v.ActionId == order.Id && v.Kind == "TimedCompletionRejected"), result.Reason);
    }
    private static void Advance(SimulationEngine e, int ticks) { for (int n = 0; n < ticks; n++) e.Step(); }
    private static void Grant(WorldState w, string owner, string item, long quantity) =>
        w.Ledger.Add(new(w.NextSequence++, 0, "fixture:" + w.NextSequence, "$genesis", owner, item, quantity, "Genesis"));
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("Settlement demo loads confirmed initial values and preserves fresh game mode", () => {
            var w = LegacyCreate(); var s = w.Settlements.Single(); var r = w.SettlementRules!;
            Check(s.Population == r.InitialPopulation && s.Happiness == r.InitialHappiness && s.PopulationCapacity == r.HallCapacity, "Initial demographics");
            Check(Inventory.Balance(w, s.OrganizationId, "spirit-stone") == r.InitialStones && s.Buildings.Count == 4, "Initial funds/buildings");
            var e = new SimulationEngine(w); Advance(e, 90);
            Check(e.Snapshot().SettlementDays.Count == 0 && Inventory.Balance(e.Snapshot(), s.OrganizationId, "food") == 0, "Unstaffed free production");
            Check(NewGameFactory.CreateLike(e.Snapshot(), "新聚落").SettlementRules is not null, "New game lost mode");
        }),
        ("Settlement employees produce only after full paid day and reload never doubles wages", () => {
            var e = new SimulationEngine(LegacyCreate()); Act(e, ActionKind.SetSettlementEmployees, "farm", 2);
            Advance(e, 29); Check(e.Snapshot().SettlementDays.Count == 0, "Early production");
            e = new SimulationEngine(WorldCodec.Clone(e.Snapshot())); Advance(e, 1);
            var w = e.Snapshot(); var s = w.Settlements.Single(); var r = w.SettlementRules!;
            Check(w.SettlementDays.Count == 1 && Inventory.Balance(w,s.OrganizationId,"food") == 2 * r.DailyOutputPerEmployee, "Missing output");
            Check(Inventory.Balance(w,s.ResidentsAccountId,"spirit-stone") == 2 * r.DailyWage, "Missing wages");
            Advance(e, 30); Check(e.Snapshot().SettlementDays.Count == 2, "Duplicate or lost payroll");
        }),
        ("Settlement cannot pay wages stops production and does not borrow", () => {
            var w = LegacyCreate(); var s = w.Settlements.Single();
            w.Ledger.RemoveAll(l => l.To == s.OrganizationId); var e = new SimulationEngine(w);
            Act(e, ActionKind.SetSettlementEmployees, "farm", 2); Advance(e, 30);
            w = e.Snapshot(); Check(w.SettlementDays.Count == 0 && w.Events.Any(v => v.Kind == "SettlementStopped"), "Unpaid building produced");
            Check(Inventory.Balance(w,s.OrganizationId,"food") == 0 && Inventory.Balance(w,s.ResidentsAccountId,"spirit-stone") == 0, "Unpaid assets changed");
        }),
        ("Settlement shop uses finite stock cash and configured bid ask spread", () => {
            var e = new SimulationEngine(LegacyCreate());
            Act(e, ActionKind.SetSettlementEmployees,"farm",1); Act(e,ActionKind.SetSettlementEmployees,"shop",1); Advance(e,30);
            var before=e.Snapshot(); var s=before.Settlements.Single(); long actor=Inventory.Balance(before,before.ControlledCharacterId,"spirit-stone");
            Act(e,ActionKind.SettlementBuy,"food"); Act(e,ActionKind.SettlementSell,"food");
            var w=e.Snapshot(); var p=w.SettlementRules!.Prices.Single(p=>p.Item=="food");
            Check(Inventory.Balance(w,w.ControlledCharacterId,"spirit-stone")==actor-p.Sell+p.Buy,"Incorrect spread");
            var bad=Order(w,ActionKind.SettlementBuy,"food",long.MaxValue); e.Submit(bad); Check(!e.Step().Single().Accepted,"Oversized buy accepted");
            w=e.Snapshot(); var q=Order(w,ActionKind.SettlementSell,"wood"); e.Submit(q); Check(!e.Step().Single().Accepted,"Sold unavailable wood");
        }),
        ("Settlement aid transfers real assets once and records only material influence", () => {
            var w=LegacyCreate(); var s=w.Settlements.Single(); Grant(w,"sect","wood",20); var e=new SimulationEngine(w);
            var order=Order(e.Snapshot(),ActionKind.SupportSettlement,"wood",10,"sect"); e.Submit(order); e.Step(); e.Submit(order); Check(!e.Step().Single().Accepted,"Duplicate aid");
            w=e.Snapshot(); Check(Inventory.Balance(w,"sect","wood")==10 && Inventory.Balance(w,s.OrganizationId,"wood")==10,"Aid duplicated or lost");
            Check(w.Settlements.Single().Influence["sect"]==10*w.SettlementRules!.MaterialInfluence,"Influence incorrect");
            Act(e,ActionKind.SupportSettlement,"spirit-stone",1,"sect"); Check(e.Snapshot().SettlementInfluence.Count==1,"Cash incorrectly awards points");
            var forged=Order(e.Snapshot(),ActionKind.SupportSettlement,"wood",1,"gang"); e.Submit(forged); Check(!e.Step().Single().Accepted,"Spent other organization funds");
        }),
        ("Settlement upgrade requires population and consumes real assets without map expansion", () => {
            var w=LegacyCreate(); var s=w.Settlements.Single(); var level=w.SettlementRules!.Levels.First();
            Grant(w,s.OrganizationId,"wood",level.Wood); var e=new SimulationEngine(w);
            var invalid=Order(e.Snapshot(),ActionKind.UpgradeSettlement,"upgrade"); e.Submit(invalid); Check(!e.Step().Single().Accepted,"Low population upgrade");
            w=LegacyCreate(); s=w.Settlements.Single(); s.Population=level.RequiredPopulation; Grant(w,s.OrganizationId,"wood",level.Wood);
            long stones=Inventory.Balance(w,s.OrganizationId,"spirit-stone"); var territory=s.Territory.ToArray(); e=new SimulationEngine(w);
            Act(e,ActionKind.UpgradeSettlement,"upgrade"); w=e.Snapshot(); var upgraded=w.Settlements.Single();
            Check(upgraded.Level==level.Level && upgraded.PopulationCapacity==s.PopulationCapacity+w.SettlementRules!.HallCapacityPerLevel,"Upgrade effects");
            Check(Inventory.Balance(w,s.OrganizationId,"wood")==0 && Inventory.Balance(w,s.OrganizationId,"spirit-stone")==stones-level.Stones,"Upgrade did not consume");
            Check(upgraded.Territory.SequenceEqual(territory),"Upgrade expanded map");
        }),
        ("Settlement resource buildings pay daily for twelve days with independent output and no free cash", () => {
            var w=LegacyCreate(); var s=w.Settlements.Single(); var r=w.SettlementRules!;
            var mountain=w.HexMap!.Tiles.Single(t=>s.Territory.Contains(t.Coord)&&t.Mountain);
            s.Buildings.Add(new("mine-fixture",SettlementBuildingKind.Mine,mountain.Coord,2,r.EmployeeLimit));
            int n=s.Buildings.FindIndex(b=>b.Id=="farm"); s.Buildings[n]=s.Buildings[n] with { EmployeeCount=3 };
            n=s.Buildings.FindIndex(b=>b.Id=="forestry"); s.Buildings[n]=s.Buildings[n] with { EmployeeCount=4 };
            var e=new SimulationEngine(w); Advance(e,12*w.Time!.Rules.TicksPerDay); w=e.Snapshot();
            Check(w.SettlementDays.Count==36,"Missing daily receipts");
            Check(Inventory.Balance(w,s.OrganizationId,"food")==12*3*r.DailyOutputPerEmployee &&
                Inventory.Balance(w,s.OrganizationId,"wood")==12*4*r.DailyOutputPerEmployee &&
                Inventory.Balance(w,s.OrganizationId,"mineral")==12*2*r.DailyOutputPerEmployee,"Resource output mismatch");
            Check(Inventory.Balance(w,s.OrganizationId,"spirit-stone")+Inventory.Balance(w,s.ResidentsAccountId,"spirit-stone")==r.InitialStones,
                "Payroll created or destroyed cash");
            Check(Inventory.Balance(w,s.ResidentsAccountId,"spirit-stone")==12*9*r.DailyWage,"Payroll total mismatch");
        }),
        ("Settlement completion rejects intervening resource changes and remote management", () => {
            var w=LegacyCreate(); var s=w.Settlements.Single(); var actor=w.ControlledCharacterId;
            int n=w.Characters.FindIndex(c=>c.Id==actor); var town=HexNavigation.Resolve(w.HexMap!,"town")!.Value;
            w.Characters[n]=w.Characters[n] with { HexPosition=town,DemoLocationId="town" };
            var e=new SimulationEngine(w); var remote=Order(w,ActionKind.SetSettlementEmployees,"farm"); e.Submit(remote);
            Check(!e.Step().Single().Accepted,"Remote management accepted");
            w=LegacyCreate(); s=w.Settlements.Single(); Grant(w,"sect","wood",1); e=new SimulationEngine(w);
            var one=Order(w,ActionKind.SupportSettlement,"wood",1,"sect"); e.Submit(one);
            e.Submit(one with { Id=one.Id+"-second" }); var results=e.Step();
            Check(results.Count(r=>r.Accepted)==1 && Inventory.Balance(e.Snapshot(),s.OrganizationId,"wood")==1,"Double spent donor assets/AP");
        }),
        ("Settlement runtime rejects invalid settings and forged output or influence", () => {
            var w=LegacyCreate();
            try { SettlementEconomyIntegrity.ValidateRules(w.SettlementRules! with { DailyWage=0 }); throw new Exception("Invalid wage accepted"); } catch(InvalidDataException) { }
            w.Ledger.Add(new(w.NextSequence++,0,"forged","$settlement-production",w.Settlements.Single().OrganizationId,"wood",1,"SettlementProduced"));
            try { WorldIntegrity.Validate(w); throw new Exception("Forged production accepted"); } catch(InvalidDataException) { }
            w=LegacyCreate(); w.Settlements.Single().Influence["sect"]=100;
            try { WorldIntegrity.Validate(w); throw new Exception("Forged influence accepted"); } catch(InvalidDataException) { }
        })
    };
}
