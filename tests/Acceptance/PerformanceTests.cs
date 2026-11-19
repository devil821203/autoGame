using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Simulation;

internal static class PerformanceTests
{
    private sealed class Counter : IActionPlanner
    {
        public Dictionary<string, int> Counts { get; } = [];
        public IReadOnlyList<ScoredAction> Evaluate(LocalObservation o) { Counts[o.Actor.Id] = Counts.GetValueOrDefault(o.Actor.Id) + 1; return []; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static WorldState Empty()
    {
        var w = AutonomousWorld.Create(); w.Ledger.Clear(); w.NextSequence = 1;
        w.Performance = new(); w.DemoMapEnabled = true;
        CharacterState Actor(string id, string region, string location) => new(id, id) { RegionId = region, DemoLocationId = location, WealthWeight = 0, GrowthWeight = 0, SocialWeight = 0, DesiredCultivation = 0, DesiredStones = 0 };
        w.Characters = [Actor("player", "valley", "town"), Actor("near", "valley", "town"), Actor("regional", "valley", "grove"), Actor("remote", "remote", "grove")];
        return w;
    }
    private static void Steps(SimulationEngine e, int ticks, bool ai = false) { for (int n = 0; n < ticks; n++) e.Step(ai); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("LOD updates nearby every tick regional every four remote every sixteen with persisted cursors", () =>
        {
            var counter = new Counter(); var e = new SimulationEngine(Empty(), counter); Steps(e, 20, true); var w = e.Snapshot();
            Check(counter.Counts["near"] == 20 && counter.Counts["regional"] == 5, "LOD cadence wrong");
            Check(w.AiUpdates.Single(s => s.ActorId == "remote").LastTick == 16 && w.AiUpdates.Single(s => s.ActorId == "remote").Lod == AiLod.Remote, "Remote cadence not persisted");
            Check(e.LastDiagnostics is { Decisions: >= 1, TotalMilliseconds: >= 0 } && !WorldCodec.Serialize(w).Contains("TotalMilliseconds"), "Runtime metrics leaked into save");
        }),
        ("Targeted notification wakes only its recipient before regional interval", () =>
        {
            var e = new SimulationEngine(Empty(), new Counter()); Steps(e, 21, true); var w = e.Snapshot();
            long seq = w.NextSequence++; w.Events.Add(new(seq, w.Clock.Tick, "local-event", "ResourceRegenerated", "grove:herb:+1")); w.Notifications.Add(new(seq, "regional"));
            var counter = new Counter(); e = new(w, counter); e.Step(true);
            Check(counter.Counts.GetValueOrDefault("regional") == 1 && e.Snapshot().AiUpdates.Single(s => s.ActorId == "remote").LastTick == 16, "Targeted wake spread globally or was ignored");
        }),
        ("LOD transition preserves reserved resources completion and save continuation", () =>
        {
            var w = Empty(); w.Characters[3] = w.Characters[3] with { GrowthWeight = 1, DesiredCultivation = 100 };
            w.Ledger.Add(new(w.NextSequence++, 0, "remote-pill", "$genesis", "remote", "pill", 1, "Genesis"));
            var e = new SimulationEngine(w, new Counter()); e.Step(true); w = e.Snapshot();
            Check(w.Scheduled.Single().Intent.ActorId == "remote", "Remote personal action not scheduled");
            w.Characters[3] = w.Characters[3] with { RegionId = "valley" };
            e = new(w, new Counter()); var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w)), new Counter());
            Steps(e, 6, true); Steps(resumed, 6, true); var after = e.Snapshot();
            Check(after.Characters[3].Cultivation == 10 && Inventory.Balance(after, "remote", "pill") == 0 && after.Scheduled.Count == 0, "LOD switch lost or duplicated personal resources");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "LOD save continuation diverged");
        }),
        ("Remote combat batches real rounds and matches isolated detailed damage loot and random state", () =>
        {
            var w = CombatScenario.Create(); w.Performance = new(RemoteCombatBatchTicks: 16);
            w.Characters = w.Characters.Select(c => c.Id is "npc:00" or "bandit:weak" ? c with { RegionId = "remote" } : c).ToList();
            var detailed = WorldCodec.Clone(w); detailed.Performance = null;
            var batch = new SimulationEngine(w); var full = new SimulationEngine(detailed);
            var intent = new ActionIntent("remote-fight", "npc:00", "bandit:weak", "combat", 1, 0, DecisionSource.Npc, ActionKind.Engage);
            batch.Submit(intent); full.Submit(intent); batch.Step(); full.Step(); Steps(batch, 7);
            Check(batch.Snapshot().Combats.Single().Rounds == 0, "Remote combat was not batched");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(batch.Snapshot())));
            var promotedState = batch.Snapshot();
            promotedState.Characters[0] = promotedState.Characters[0] with { RegionId = "remote" };
            var promoted = new SimulationEngine(promotedState); promoted.Step();
            Steps(batch, 24); Steps(resumed, 24); Steps(full, 31);
            var a = batch.Snapshot(); var b = full.Snapshot();
            foreach (string id in new[] { "npc:00", "bandit:weak" })
                Check(a.Characters.Single(c => c.Id == id).Health == b.Characters.Single(c => c.Id == id).Health && Inventory.Balance(a, id, "spirit-stone") == Inventory.Balance(b, id, "spirit-stone"), "Batched combat changed individual damage or loot");
            Check(a.RandomState == b.RandomState && a.Combats.Single().Rounds == b.Combats.Single().Rounds && a.Combats.Single().WinnerId == b.Combats.Single().WinnerId, "Batch changed random outcome");
            Check(promoted.Snapshot().Combats.Single().Rounds == b.Combats.Single().Rounds && promoted.Snapshot().Characters.Single(c => c.Id == "npc:00").Health == b.Characters.Single(c => c.Id == "npc:00").Health, "Promoting far combat lost pending rounds");
            Check(WorldCodec.Digest(a) == WorldCodec.Digest(resumed.Snapshot()), "Batched combat save diverged");
        }),
        ("Diagnostic pruning retains audit events ledger and replay protection", () =>
        {
            var w = SectEconomyScenario.Create(); w.Performance = new(DiagnosticRetentionTicks: 5);
            var e = new SimulationEngine(w); Steps(e, 40, true); w = e.Snapshot();
            Check(w.Events.Where(v => v.Kind is "Decision" or "DecisionBudget" or "PlanInvalidated" or "PartyWaiting").All(v => v.Tick >= w.Clock.Tick - 5), "Expired diagnostics not trimmed");
            Check(w.Ledger.Count > 0 && w.ProcessedIntentIds.Count > 0 && w.Events.Any(v => v.Kind == "PayrollPaid"), "Audit data was pruned");
            var snapshot = WorldCodec.Deserialize(WorldCodec.Serialize(w)); var resumed = new SimulationEngine(snapshot);
            string id = w.ProcessedIntentIds[0]; resumed.Submit(new(id, "player", "sect", "spirit-stone", 1, 0, DecisionSource.Player));
            Check(resumed.Step().Single().Reason == "Duplicate intent", "Pruning lost replay protection");
        }),
        ("Scale worlds preserve all individuals and enforce fixed decision budgets", () =>
        {
            var w = ScaleScenario.Create(100); var e = new SimulationEngine(w); Steps(e, 30, true); w = e.Snapshot();
            Check(Enum.GetValues<Aspiration>().Where(a => a != Aspiration.Balanced).All(a => w.Characters.Count(c => c.Id != "player" && c.Aspiration == a) == 3), "Scale fixture changed confirmed aspiration quotas");
            Check(w.Characters.Count == 101 && w.AiUpdates.Any(s => s.Lod == AiLod.Remote) && e.LastDiagnostics!.Decisions <= w.Rules.DecisionBudget, "Scale fixture lost people or budget");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w))); Steps(e, 20, true); Steps(resumed, 20, true);
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Scale LOD continuation diverged");
        })
    };
}
