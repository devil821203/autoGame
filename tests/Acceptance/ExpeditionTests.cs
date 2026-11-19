using Cultivation.Domain;
using Cultivation.Simulation;

internal static class ExpeditionTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static ActionIntent Intent(string id, string actor, ActionKind kind, string target, string item = "party") => new(id, actor, target, item, 1, 0, actor == "player" ? DecisionSource.Player : DecisionSource.Npc, kind);
    private static bool Run(SimulationEngine e, ActionIntent i) { e.Submit(i); return e.Step().Single().Accepted; }
    private static WorldState World(bool team = false, MonsterKind target = MonsterKind.Wolf)
    {
        var w = SectEconomyScenario.Create(); w.TestProcurement = null; w.SectPayroll = null; w.SectDuty = null;
        w.Dungeon = new(1, target == MonsterKind.Wolf ? 1 : 0, 1, 1); w.CombatRules = w.CombatRules! with { DamageVariance = 0 };
        w.Characters = w.Characters.Select(c => c.Id is "player" or "npc:00" or "npc:01" or "npc:02" ? c with { DemoLocationId = "sect", Health = 100, Realm = 0, MethodId = null, MethodExperience = 0, BaseAttack = 10, BaseDefense = 2 } : c).ToList();
        if (team) w.Parties.Add(new("test-party", "player", ["player", "npc:00", "npc:01"]));
        return w;
    }
    private static WorldState Dungeon(WorldState w)
    {
        w.Characters = w.Characters.Select(c => c.Id is "player" or "npc:00" or "npc:01" ? c with { DemoLocationId = "dungeon" } : c).ToList(); return w;
    }
    private static void Finish(SimulationEngine e) { for (int n = 0; n < 30 && e.Snapshot().Combats.Any(c => c.Status == CombatStatus.Active); n++) e.Step(); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Autonomous expedition economy sustains production sales payroll hunts and deterministic resumption", () =>
        {
            var e = new SimulationEngine(SectEconomyScenario.Create());
            for (int n = 0; n < 120; n++) e.Step(true);
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            for (int n = 0; n < 120; n++) { e.Step(true); resumed.Step(true); }
            var w = e.Snapshot();
            Check(w.SectWorkReceipts.Any(r => r.Tick > 120 && r.Kind == ActionKind.SectGather && r.Success) && w.SectWorkReceipts.Any(r => r.Tick > 120 && r.Kind == ActionKind.SectBrew && r.Success), "Late production stalled");
            Check(w.ProcurementReceipts.Any(r => r.Tick > 120) && w.Ledger.Any(l => l.Reason == "ContributionRedemption"), "Exchange or recurring procurement stalled");
            Check(w.Combats.Any(b => b.Allies is { Count: > 0 }) && w.HuntReceipts.Count > 0, "NPCs did not autonomously form expedition and complete hunt");
            Check(!w.Events.Any(v => v.Kind is "DutyExpelled" or "PayrollSkipped"), "Tuned test economy lost payroll or members");
            Check(WorldCodec.Digest(w) == WorldCodec.Digest(resumed.Snapshot()), "Expedition economy resumption diverged");
        }),
        ("Party invitation requires consent same sect location and maximum three members", () =>
        {
            var e = new SimulationEngine(World());
            Check(Run(e, Intent("invite", "player", ActionKind.InviteParty, "npc:00")), "Invitation failed");
            Check(e.Snapshot().Parties.Count == 0, "Invitation forced membership");
            Check(!Run(e, Intent("control", "npc:00", ActionKind.AcceptParty, "invite") with { Source = DecisionSource.Player }), "Player controlled NPC consent");
            Check(Run(e, Intent("accept", "npc:00", ActionKind.AcceptParty, "invite")), "Voluntary acceptance failed");
            Check(Run(e, Intent("third", "player", ActionKind.InviteParty, "npc:01")), "Third invite failed");
            Check(Run(e, Intent("accept-third", "npc:01", ActionKind.AcceptParty, "third")), "Third acceptance failed");
            Check(!Run(e, Intent("fourth", "player", ActionKind.InviteParty, "npc:02")), "Overfilled party");
            Check(!Run(e, Intent("outsider", "player", ActionKind.InviteParty, "npc:20")), "Outsider invited");
            Check(Run(e, Intent("leave", "npc:00", ActionKind.LeaveParty, "invite")) && e.Snapshot().Parties.Single().Members.Count == 2, "Individual exit failed");
        }),
        ("Party invitation expiry and remote acceptance are rejected", () =>
        {
            var w = World(); w.PartyRules = w.PartyRules! with { InvitationTicks = 2 }; var e = new SimulationEngine(w);
            Run(e, Intent("expires", "player", ActionKind.InviteParty, "npc:00")); e.Step(); e.Step();
            Check(!Run(e, Intent("late", "npc:00", ActionKind.AcceptParty, "expires")), "Expired invitation accepted");
            var other = World(); int index = other.Characters.FindIndex(c => c.Id == "npc:00"); other.Characters[index] = other.Characters[index] with { DemoLocationId = "grove" };
            Check(!Run(new SimulationEngine(other), Intent("remote", "player", ActionKind.InviteParty, "npc:00")), "Remote invitation accepted");
        }),
        ("Three-person dungeon combat reduces total injuries and counts one hunt with split rewards", () =>
        {
            var w = World(true); w.Hunts.Add(new("wolf-hunt", "sect", MonsterKind.Wolf, 5, 10, 1000, 0, ["player"]));
            var team = new SimulationEngine(Dungeon(w));
            Check(Run(team, Intent("team-fight", "player", ActionKind.ExploreDungeon, "dungeon", "combat")), "Team exploration failed");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(team.Snapshot())));
            Finish(team); Finish(resumed); var after = team.Snapshot();
            Check(after.Hunts.Single(h => h.Id == "wolf-hunt").Kills == 1 && after.HuntReceipts.Sum(r => r.Contribution) == 10 && after.HuntReceipts.Count == 3, "Shared progress or rewards duplicated");
            Check(after.PartyLoot.Where(l => l.ResourceId == "beast-hide").Sum(l => l.Quantity) == 1, "Wolf hide duplicated");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Mid-team-battle continuation diverged");
            var solo = new SimulationEngine(Dungeon(World())); Run(solo, Intent("solo-fight", "player", ActionKind.ExploreDungeon, "dungeon", "combat")); Finish(solo);
            int teamHarm = new[] { "player", "npc:00", "npc:01" }.Sum(id => 100 - after.Characters.Single(c => c.Id == id).Health);
            Check(teamHarm < 100 - solo.Snapshot().Characters.Single(c => c.Id == "player").Health, "Team combat did not reduce injuries");
        }),
        ("Hunt limits reject extra kills rewards and require acceptance and unexpired deadline", () =>
        {
            foreach (bool expired in new[] { false, true })
            {
                var w = World(); w.Characters[0] = w.Characters[0] with { BaseAttack = 100 };
                w.Hunts.Add(new("bandit-hunt", "sect", MonsterKind.Bandit, 1, 10, expired ? 2 : 1000, 0, ["player"]));
                w.Dungeon = new(1, 0, 1, 1); var e = new SimulationEngine(Dungeon(w));
                Run(e, Intent("first", "player", ActionKind.ExploreDungeon, "dungeon", "combat")); Finish(e);
                Run(e, Intent("second", "player", ActionKind.ExploreDungeon, "dungeon", "combat")); Finish(e);
                Check(e.Snapshot().HuntReceipts.Count == (expired ? 0 : 1), "Expired or excess kill rewarded");
            }
        }),
        ("Party members independently consume skills and can retreat without controlling teammates", () =>
        {
            var w = Dungeon(World(true)); w.CombatRules = w.CombatRules! with { RetreatChance = 1 };
            var e = new SimulationEngine(w); Run(e, Intent("battle", "player", ActionKind.ExploreDungeon, "dungeon", "combat"));
            string enemy = e.Snapshot().Combats.Single().DefenderId;
            Check(!Run(e, Intent("remote-skill", "npc:00", ActionKind.PowerStrike, enemy, "pill") with { Source = DecisionSource.Player }), "Player controlled teammate skill");
            Check(Run(e, Intent("retreat", "npc:00", ActionKind.Retreat, enemy, "combat")), "Personal retreat failed");
            Check(e.Snapshot().Combats.Single().Withdrawn!.SequenceEqual(new[] { "npc:00" }) && e.Snapshot().Combats.Single().Status == CombatStatus.Active, "Teammates were forced to retreat");
        })
    };
}
