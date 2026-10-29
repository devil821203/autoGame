using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SectEconomyTests
{
    private static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
    private static void Steps(SimulationEngine e, int count) { for (int n = 0; n < count; n++) e.Step(); }
    private static WorldState Isolated()
    {
        var w = SectEconomyScenario.Create(); w.SectDuty = null; w.SectPayroll = null; w.TestProcurement = null;
        return w;
    }
    private static bool Submit(SimulationEngine e, ActionIntent i) { e.Submit(i); return e.Step().Single().Accepted; }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Autonomous sect economy delivers missions and resumes all automation deterministically", () =>
        {
            var engine = new SimulationEngine(SectEconomyScenario.Create());
            for (int n = 0; n < 30; n++) engine.Step(true);
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(engine.Snapshot())));
            for (int n = 0; n < 100; n++) { engine.Step(true); resumed.Step(true); }
            var after = engine.Snapshot();
            Check(after.ProcurementReceipts.Count > 0 && (after.Deliveries.Count > 0 || after.SectWorkReceipts.Any(r => r.Success) || after.HuntReceipts.Count > 0) && after.Events.Any(e => e.Kind == "PayrollPaid") && after.Events.Any(e => e.Kind == "DutyReviewed"), "Autonomous economy did not connect");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Integrated automation continuation diverged");
        }),
        ("Wolf loot can be delivered to an automatically published sect mission", () =>
        {
            var w = Isolated(); w.SectDuty = new(); w.Dungeon = new(1, 1);
            w.Characters[0] = w.Characters[0] with { DemoLocationId = "dungeon", BaseAttack = 100 };
            var e = new SimulationEngine(w);
            Check(Submit(e, new("hunt", "player", "dungeon", "combat", 1, 0, DecisionSource.Player, ActionKind.ExploreDungeon)), "Hunt failed");
            Steps(e, 19);
            Check(Inventory.Balance(e.Snapshot(), "player", "beast-hide") == 1, "No hunt output");
            Check(Submit(e, new("return", "player", "sect", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel)), "Return travel failed"); Steps(e, 5);
            var task = e.Snapshot().Missions.Single(m => m.ResourceId == "beast-hide");
            Check(Submit(e, new("accept-hide", "player", "sect", "beast-hide", 1, 0, DecisionSource.Player, ActionKind.AcceptMission, MissionId: task.Id)), "Hide task unavailable");
            Check(Submit(e, new("deliver-hide", "player", "sect", "beast-hide", 1, 0, DecisionSource.Player, ActionKind.DeliverMission, MissionId: task.Id)), "Hide delivery failed");
            Check(Inventory.Balance(e.Snapshot(), "sect", "beast-hide") == 1 && Inventory.Balance(e.Snapshot(), "player", "beast-hide") == 0, "Hide duplicated or lost");
        }),
        ("Sect payroll pays members and elders every ten ticks without paying leader", () =>
        {
            var w = Isolated(); w.SectPayroll = new();
            var members = w.Characters.Where(c => c.OrganizationId == "sect" && c.Id != "player").ToArray();
            var e = new SimulationEngine(w); Steps(e, 9);
            Check(e.Snapshot().Events.All(x => x.Kind != "PayrollPaid"), "Salary paid early");
            var resumed = new SimulationEngine(e.Snapshot()); e.Step(); resumed.Step();
            foreach (var member in members) Check(Inventory.Balance(e.Snapshot(), member.Id, "spirit-stone") == Inventory.Balance(w, member.Id, "spirit-stone") + 30, "Wrong member salary");
            Check(Inventory.Balance(e.Snapshot(), "player", "spirit-stone") == Inventory.Balance(w, "player", "spirit-stone"), "Leader paid salary");
            Check(Inventory.Balance(e.Snapshot(), "sect", "spirit-stone") == Inventory.Balance(w, "sect", "spirit-stone") - members.Length * 30, "Salary fabricated funds");
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Payroll continuation diverged");
        }),
        ("Payroll cannot spend reserved mission funds", () =>
        {
            var w = Isolated(); w.SectPayroll = new();
            w.Characters[0] = w.Characters[0] with { DemoLocationId = "sect" };
            var e = new SimulationEngine(w); long budget = Inventory.Balance(w, "sect", "spirit-stone");
            Check(Submit(e, new("reserve", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission, Offer: new("all-funds", 1, budget, 100, budget))), "Reservation failed");
            Steps(e, 9);
            Check(e.Snapshot().Events.Any(x => x.Kind == "PayrollSkipped") && Inventory.Balance(e.Snapshot(), "$mission:all-funds", "spirit-stone") == budget, "Salary spent reserved funds");
        }),
        ("Test procurement injects audited funding and adjusts prices by period demand", () =>
        {
            foreach (bool excess in new[] { false, true })
            {
                var w = Isolated(); w.Organizations.Add(new("buyer", "Test buyer")); w.TestProcurement = new(BuyerId: "buyer");
                w.SectFacilities = w.SectFacilities! with { ReservedPills = 0 };
                w.Ledger.RemoveAll(l => l.ActionId == "sect-start-pills");
                if (excess) w.Ledger.Add(new(w.NextSequence++, 0, "extra-pills", "$genesis", "sect", "pill", 100, "Genesis"));
                var e = new SimulationEngine(w); Steps(e, 20); var after = e.Snapshot();
                Check(after.ProcurementQuotes.Single(q => q.ResourceId == "pill").UnitPrice == (excess ? 135 : 165), "Period price response wrong");
                long funding = after.Ledger.Where(l => l.From == "$test-procurement").Sum(l => l.Quantity);
                Check(funding == after.ProcurementReceipts.Sum(r => r.Payment) && Inventory.Balance(after, "buyer", "spirit-stone") == 0, "Test funding not auditable");
                Check(Inventory.Balance(after, "sect", "pill") == 0 && Inventory.Balance(after, "buyer", "pill") == Inventory.Balance(w, "sect", "pill"), "Procurement lost goods");
            }
        }),
        ("Dungeon wolf drops only hide and bandit drops bounded stones and pill once", () =>
        {
            foreach (bool wolf in new[] { false, true })
            {
                var w = Isolated(); w.Dungeon = new(EncounterChance: 1, WolfChance: wolf ? 1 : 0, BanditStoneChance: 1, BanditPillChance: 1);
                w.Characters[0] = w.Characters[0] with { DemoLocationId = "dungeon", Health = 100, BaseAttack = 100 };
                var e = new SimulationEngine(w);
                Check(Submit(e, new("explore", "player", "dungeon", "combat", 1, 0, DecisionSource.Player, ActionKind.ExploreDungeon)), "Exploration rejected");
                var resumed = new SimulationEngine(e.Snapshot());
                for (int n = 0; n < 8; n++) { e.Step(); resumed.Step(); }
                var after = e.Snapshot(); var monster = after.Characters.Single(c => c.MonsterKind != MonsterKind.None);
                var loot = after.Loot.Where(l => l.LoserId == monster.Id).ToArray();
                Check(wolf ? loot.Length == 1 && loot[0].ResourceId == "beast-hide" && loot[0].Quantity == 1 :
                    loot.Length == 2 && loot.Any(l => l.ResourceId == "pill" && l.Quantity == 1) && loot.Any(l => l.ResourceId == "spirit-stone" && l.Quantity is >= 1 and <= 5), "Wrong monster loot");
                Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Dungeon RNG continuation diverged");
                e.Submit(new("explore", "player", "dungeon", "combat", 1, 0, DecisionSource.Player, ActionKind.ExploreDungeon)); e.Step();
                Check(e.Snapshot().Characters.Count(c => c.MonsterKind != MonsterKind.None) == 1, "Duplicate exploration spawned loot");
            }
        }),
        ("Dungeon can have no encounter and cannot be explored remotely", () =>
        {
            var w = Isolated(); w.Dungeon = new(EncounterChance: 0, HerbSuccessChance: 0); var e = new SimulationEngine(w);
            var i = new ActionIntent("remote", "player", "dungeon", "combat", 1, 0, DecisionSource.Player, ActionKind.ExploreDungeon);
            Check(!Submit(e, i), "Remote exploration allowed");
            w.Characters[0] = w.Characters[0] with { DemoLocationId = "dungeon" }; e = new(w);
            Check(Submit(e, i), "Empty exploration failed");
            Check(e.Snapshot().Events.Any(x => x.Kind == "DungeonGatherFailed") && e.Snapshot().Combats.Count == 0, "Empty encounter created combat");
        }),
        ("Automatic sect missions reserve budgets for pills and hides", () =>
        {
            var w = Isolated(); w.SectDuty = new(); var e = new SimulationEngine(w); Steps(e, 20);
            var after = e.Snapshot();
            Check(after.Missions.Count == 2 && after.Missions.All(m => m.MembersOnly && m.Participants.Count == 0) && after.Missions.Any(m => m.ResourceId == "beast-hide"), "Auto tasks missing or forcibly assigned");
            Check(after.Missions.Sum(m => m.ReservedBudget) + Inventory.Balance(after, "sect", "spirit-stone") == Inventory.Balance(w, "sect", "spirit-stone"), "Mission budget not conserved");
        }),
        ("Duty reviews count consecutive misses and delivery clears failures", () =>
        {
            var w = Isolated(); w.SectDuty = new(MissionIntervalTicks: 1000);
            w.Characters = w.Characters.Select(c => c.OrganizationId == "sect" ? c with { DemoLocationId = "sect" } : c).ToList();
            w.MemberDuties = w.Characters.Where(c => c.OrganizationId == "sect" && c.Id != "player").Select(c => new MemberDuty("sect", c.Id, 0, 0)).ToList();
            var e = new SimulationEngine(w); Steps(e, 30);
            Check(e.Snapshot().MemberDuties.All(d => d.Misses == 1), "First review wrong");
            Check(Submit(e, new("task", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission, Offer: new("duty-task", 1, 1, 100, 1))), "Publish failed");
            Check(Submit(e, new("accept", "npc:00", "sect", "pill", 1, 0, DecisionSource.Npc, ActionKind.AcceptMission, MissionId: "duty-task")), "Accept failed");
            Check(Submit(e, new("deliver", "npc:00", "sect", "pill", 1, 0, DecisionSource.Npc, ActionKind.DeliverMission, MissionId: "duty-task")), "Delivery failed");
            Steps(e, 27);
            Check(e.Snapshot().MemberDuties.Single(d => d.ActorId == "npc:00").Misses == 0, "Delivery did not reset misses");
            Steps(e, 30);
            Check(e.Snapshot().Characters.Any(c => c.Id != "player" && c.Id != "npc:00" && c.Position == OrganizationPosition.None) && e.Snapshot().Characters.Single(c => c.Id == "npc:00").OrganizationId == "sect", "Three missed periods not enforced");
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").Position == OrganizationPosition.Leader, "Leader assessed");
        })
    };
}


