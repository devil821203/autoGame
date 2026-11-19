using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SectFacilityTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static WorldState World()
    {
        var w = SectEconomyScenario.Create(); w.TestProcurement = null; w.SectPayroll = null; w.SectDuty = null;
        w.Characters = w.Characters.Select(c => c.Id == "player" || c.Id == "npc:00" ? c with { DemoLocationId = "sect" } : c).ToList();
        w.SectFacilities = w.SectFacilities! with { ReservedHerbs = 0, ReservedPills = 0, BrewContribution = 120 };
        w.Ledger.RemoveAll(l => l.Tick == 0 && l.To == "sect" && l.ResourceId == "herb");
        w.Ledger.Add(new(w.NextSequence++, 0, "sect-materials", "$genesis", "sect", "herb", 10, "Genesis"));
        return w;
    }
    private static ActionIntent Intent(string id, string actor, ActionKind kind, string target, string resource, long quantity = 1) =>
        new(id, actor, target, resource, quantity, 0, actor == "player" ? DecisionSource.Player : DecisionSource.Npc, kind);
    private static bool Run(SimulationEngine e, ActionIntent i) { e.Submit(i); return e.Step().Single().Accepted; }
    private static void Steps(SimulationEngine e, int n) { for (int k = 0; k < n; k++) e.Step(); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Sect stock reserves protect exchange stock and only leader may configure", () =>
        {
            var w = World(); w.TestProcurement = new();
            w.Ledger.Add(new(w.NextSequence++, 0, "reserve-pills", "$genesis", "sect", "pill", 15, "Genesis"));
            var e = new SimulationEngine(w);
            var setting = Intent("reserve", "player", ActionKind.SetSectStockReservation, "sect", "sect-stock") with { StockReservation = new(10, 10) };
            Check(Run(e, setting), "Leader could not set reserves");
            Check(Inventory.Balance(e.Snapshot(), "sect", "pill") == 10 && e.Snapshot().TestProcurement!.DemandPerItem == 10, "Buyer consumed reserves or wrong target");
            Check(!Run(e, setting with { Id = "unauthorized", ActorId = "npc:00", Source = DecisionSource.Npc }), "Member changed reserves");
            var saved = WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot()));
            Check(saved.SectFacilities!.ReservedHerbs == 10 && saved.SectFacilities.ReservedPills == 10, "Reserve settings lost");
            saved.Characters[0] = saved.Characters[0] with { DemoLocationId = "alchemy", AlchemyChance = 1 };
            saved.SectWorkAssignments.Add(new("player", "sect", "pill"));
            Check(!Run(new SimulationEngine(saved), Intent("protected-herbs", "player", ActionKind.SectBrew, "alchemy", "herb")), "Brew consumed reserved herbs");
            var exchange = new SimulationEngine(saved);
            Check(Run(exchange, Intent("fund", "npc:00", ActionKind.BuyContribution, "sect", "spirit-stone", 4)), "Funding failed");
            Check(Run(exchange, Intent("reserved-redemption", "npc:00", ActionKind.RedeemContribution, "sect", "herb")), "Member could not redeem reserved stock");
        }),
        ("NPC autonomously accepts facility work earns contribution and resumes consistently", () =>
        {
            var w = World();
            w.Characters = w.Characters.Select(c => c.Id == "npc:00" ? c with { GrowthWeight = 0, WealthWeight = 10, Health = 100, AlchemyChance = 1 } : c).ToList();
            w.Ledger.RemoveAll(l => l.To == "npc:00" && l.ResourceId == "pill");
            var e = new SimulationEngine(w);
            for (int n = 0; n < 12; n++) e.Step(true);
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            for (int n = 0; n < 20; n++) { e.Step(true); resumed.Step(true); }
            var after = e.Snapshot();
            Check(after.SectWorkAssignments.Any(t => t.ActorId == "npc:00") && after.SectWorkReceipts.Any(t => t.ActorId == "npc:00" && t.Success), "NPC never completed facility work");
            Check(Inventory.Balance(after, "npc:00", "contribution") > 0, "NPC earned no contribution");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Facility AI continuation diverged");
        }),
        ("Sect facilities require membership accepted work and correct location", () =>
        {
            var e = new SimulationEngine(World());
            Check(!Run(e, Intent("private-gather", "player", ActionKind.Gather, "grove", "herb")), "Private facility use allowed");
            Check(!Run(e, Intent("outsider", "npc:04", ActionKind.AcceptSectWork, "sect", "herb")), "Nonmember accepted work");
            Check(!Run(e, Intent("no-task", "player", ActionKind.SectGather, "grove", "herb")), "Worked without acceptance");
            Check(Run(e, Intent("accept", "player", ActionKind.AcceptSectWork, "sect", "herb")), "Gather work rejected");
            Check(!Run(e, Intent("remote", "player", ActionKind.SectGather, "grove", "herb")), "Remote facility work allowed");
        }),
        ("Unlimited sect gathering deposits herbs to sect and rewards each actual output", () =>
        {
            var w = World(); w.Characters[0] = w.Characters[0] with { DemoLocationId = "grove" };
            w.SectWorkAssignments.Add(new("player", "sect", "herb")); var e = new SimulationEngine(w);
            long ownHerbs = Inventory.Balance(w, "player", "herb");
            for (int n = 0; n < 2; n++) { Check(Run(e, Intent("work:" + n, "player", ActionKind.SectGather, "grove", "herb")), "Gather work failed"); Steps(e, (int)w.Rules.GatherTicks); }
            var after = e.Snapshot();
            Check(Inventory.Balance(after, "sect", "herb") == 12 && Inventory.Balance(after, "player", "herb") == ownHerbs, "Herbs not owned by sect");
            Check(Inventory.Balance(after, "player", "contribution") == 8 && after.SectWorkAssignments.Count == 1, "Unlimited per-output reward incorrect");
        }),
        ("Sect brewing reserves sect inputs and successful pills belong to sect", () =>
        {
            var w = World(); w.Characters[0] = w.Characters[0] with { DemoLocationId = "alchemy", AlchemyChance = 1 };
            w.SectWorkAssignments.Add(new("player", "sect", "pill")); var e = new SimulationEngine(w);
            long pills = Inventory.Balance(w, "sect", "pill"); long own = Inventory.Balance(w, "player", "pill");
            Check(Run(e, Intent("brew", "player", ActionKind.SectBrew, "alchemy", "herb")), "Sect brew failed");
            Check(Inventory.Balance(e.Snapshot(), "sect", "herb") == 10 - w.Rules.HerbsPerPill && Inventory.Balance(e.Snapshot(), "player", "contribution") == 0, "Input reservation or early reward incorrect");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            for (int n = 0; n < 3; n++) { e.Step(); resumed.Step(); }
            var after = e.Snapshot();
            Check(Inventory.Balance(after, "sect", "pill") == pills + 1 && Inventory.Balance(after, "player", "pill") == own && Inventory.Balance(after, "player", "contribution") == 120, "Brew ownership or points incorrect");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Sect work continuation diverged");
        }),
        ("Failed sect brewing consumes sect ingredients without contribution", () =>
        {
            var w = World(); w.Characters[0] = w.Characters[0] with { DemoLocationId = "alchemy", AlchemyChance = double.Epsilon };
            w.SectWorkAssignments.Add(new("player", "sect", "pill")); var e = new SimulationEngine(w);
            Check(Run(e, Intent("fail", "player", ActionKind.SectBrew, "alchemy", "herb")), "Brew did not start"); Steps(e, 3);
            var after = e.Snapshot();
            Check(Inventory.Balance(after, "sect", "herb") == 10 - w.Rules.HerbsPerPill && Inventory.Balance(after, "player", "contribution") == 0 && !after.SectWorkReceipts.Single().Success, "Failure cost or reward wrong");
        }),
        ("Contribution purchase redemption and voluntary exit obey one-way accounting", () =>
        {
            var w = World(); var e = new SimulationEngine(w); long stones = Inventory.Balance(w, "npc:00", "spirit-stone");
            var buy = Intent("points", "npc:00", ActionKind.BuyContribution, "sect", "spirit-stone", 10);
            Check(Run(e, buy), "Point purchase failed"); e.Submit(buy); e.Step();
            Check(Inventory.Balance(e.Snapshot(), "npc:00", "contribution") == 10 && Inventory.Balance(e.Snapshot(), "npc:00", "spirit-stone") == stones - 10, "Purchase ratio or dedup wrong");
            Check(Run(e, Intent("redeem", "npc:00", ActionKind.RedeemContribution, "sect", "herb", 2)), "Redemption failed");
            Check(Inventory.Balance(e.Snapshot(), "npc:00", "contribution") == 2 && Inventory.Balance(e.Snapshot(), "sect", "herb") == 8, "Redemption cost wrong");
            Check(!Run(e, Intent("reverse", "npc:00", ActionKind.RedeemContribution, "sect", "spirit-stone")), "Points converted to stones");
            Check(!Run(e, Intent("gift", "npc:00", ActionKind.Transfer, "player", "contribution")), "Contribution transferable");
            Check(Run(e, Intent("quit", "npc:00", ActionKind.LeaveOrganization, "sect", "organization")), "Exit failed");
            Check(Inventory.Balance(e.Snapshot(), "npc:00", "contribution") == 0, "Exit retained points");
        }),
        ("Expelled worker receives no points after in-flight work completes", () =>
        {
            var w = World(); int index = w.Characters.FindIndex(c => c.Id == "npc:00"); w.Characters[index] = w.Characters[index] with { DemoLocationId = "grove" };
            w.SectWorkAssignments.Add(new("npc:00", "sect", "herb")); var e = new SimulationEngine(w);
            Check(Run(e, Intent("work", "npc:00", ActionKind.SectGather, "grove", "herb")), "Work failed");
            Check(Run(e, Intent("expel", "player", ActionKind.ExpelMember, "npc:00", "organization") with { OrganizationId = "sect" }), "Expulsion failed"); Steps(e, (int)w.Rules.GatherTicks - 1);
            Check(Inventory.Balance(e.Snapshot(), "npc:00", "contribution") == 0 && Inventory.Balance(e.Snapshot(), "sect", "herb") == 11 && e.Snapshot().SectWorkAssignments.Count == 0, "Expelled work ownership or reward wrong");
        }),
        ("Successful sect work satisfies duty review without delivering personal goods", () =>
        {
            var w = World(); w.SectDuty = new(MissionIntervalTicks: 1000, ReviewIntervalTicks: 20);
            int index = w.Characters.FindIndex(c => c.Id == "npc:00"); w.Characters[index] = w.Characters[index] with { DemoLocationId = "grove" };
            w.SectWorkAssignments.Add(new("npc:00", "sect", "herb")); w.MemberDuties.Add(new("sect", "npc:00", 0, 0, 1));
            var e = new SimulationEngine(w); Run(e, Intent("work", "npc:00", ActionKind.SectGather, "grove", "herb")); Steps(e, 19);
            Check(e.Snapshot().MemberDuties.Single(d => d.ActorId == "npc:00").Misses == 0, "Work did not satisfy duty");
        }),
        ("Dungeon herb event produces either one audited herb or no herb", () =>
        {
            foreach (double chance in new[] { 0.0, 1.0 })
            {
                var w = World(); w.Dungeon = new(EncounterChance: 0, HerbSuccessChance: chance); w.Characters[0] = w.Characters[0] with { DemoLocationId = "dungeon" };
                var e = new SimulationEngine(w); long before = Inventory.Balance(w, "player", "herb");
                Check(Run(e, Intent("forage", "player", ActionKind.ExploreDungeon, "dungeon", "combat")), "Forage failed");
                Check(Inventory.Balance(e.Snapshot(), "player", "herb") == before + (long)chance && e.Snapshot().Combats.Count == 0, "Forage output wrong");
            }
        })
    };
}


