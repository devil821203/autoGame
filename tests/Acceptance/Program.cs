using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
static ActionIntent Transfer(string id = "transfer", long quantity = 10, long duration = 0) =>
    new(id, "player", "sect", "spirit-stone", quantity, duration, DecisionSource.Player);
static void Rejects(Action action)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Expected invalid save rejection.");
}

var tests = new (string Name, Action Body)[]
{
    ("Player and NPC share business rules", () =>
    {
        var playerWorld = DemoWorld.Create();
        var npcWorld = WorldCodec.Clone(playerWorld);
        npcWorld.ControlledCharacterId = "npc";
        var player = new SimulationEngine(playerWorld);
        var npc = new SimulationEngine(npcWorld);
        player.Submit(Transfer());
        npc.Submit(Transfer() with { Source = DecisionSource.Npc });
        Check(player.Step().Single().Accepted && npc.Step().Single().Accepted, "Same actor and resources must succeed through both adapters");
        Check(player.Snapshot().Ledger.SequenceEqual(npc.Snapshot().Ledger), "Ledger differs by decision source");
        player.Submit(Transfer("poor", long.MaxValue));
        npc.Submit(Transfer("poor", long.MaxValue) with { Source = DecisionSource.Npc });
        Check(player.Step().Single().Reason == npc.Step().Single().Reason, "Resource validation differs");
    }),
    ("Control authority", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        engine.Submit(Transfer() with { ActorId = "npc" });
        Check(!engine.Step().Single().Accepted, "Player controlled NPC");
        engine.Submit(Transfer("spoof") with { Source = DecisionSource.Npc });
        Check(!engine.Step().Single().Accepted, "AI controlled player");
    }),
    ("No overspend or duplicate transfer", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        var initial = engine.Snapshot();
        long balance = Inventory.Balance(initial, "player", "spirit-stone");
        var action = Transfer("first", balance);
        engine.Submit(action);
        engine.Submit(action);
        engine.Submit(Transfer("second", 1));
        var results = engine.Step();
        Check(results.Count(r => r.Accepted) == 1, "More than one debit accepted");
        var final = engine.Snapshot();
        Check(Inventory.Balance(final, "player", "spirit-stone") == 0, "Wrong balance");
        Check(final.Ledger.Count == initial.Ledger.Count + 1, "Duplicate ledger entry");
    }),
    ("Delayed transfer reserves resources and completes once", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        var before = engine.Snapshot();
        long amount = Inventory.Balance(before, "player", "spirit-stone");
        engine.Submit(Transfer("delay", amount, 2));
        engine.Step();
        Check(Inventory.Balance(engine.Snapshot(), "player", "spirit-stone") == 0, "Not reserved");
        Check(Inventory.Balance(engine.Snapshot(), "sect", "spirit-stone") == Inventory.Balance(before, "sect", "spirit-stone"), "Completed early");
        engine.Submit(Transfer("overspend", 1));
        Check(!engine.Step().Single().Accepted, "Reserved funds reused");
        engine.Step();
        Check(engine.Snapshot().Scheduled.Count == 0, "Schedule not completed");
        Check(Inventory.Balance(engine.Snapshot(), "sect", "spirit-stone") == Inventory.Balance(before, "sect", "spirit-stone") + amount, "Missing delivery");
        int entries = engine.Snapshot().Ledger.Count;
        engine.Step();
        Check(engine.Snapshot().Ledger.Count == entries, "Completed twice");
    }),
    ("Concurrent submissions are serialized without overspend", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        long balance = Inventory.Balance(engine.Snapshot(), "player", "spirit-stone");
        Parallel.For(0, 20, i => engine.Submit(Transfer("parallel:" + i, balance)));
        Check(engine.Step().Count(r => r.Accepted) == 1, "Competing debits accepted");
    }),
    ("Invalid quantity and recipient cannot mutate ledger", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        engine.Submit(Transfer("zero", 0));
        engine.Submit(Transfer("negative", -1));
        engine.Submit(Transfer("target") with { TargetId = "missing" });
        Check(engine.Step().All(r => !r.Accepted), "Invalid action accepted");
        Check(engine.Snapshot().Ledger.Count == 3, "Ledger changed");
        var capacityWorld = DemoWorld.Create();
        int sectEntry = capacityWorld.Ledger.FindIndex(e => e.To == "sect");
        capacityWorld.Ledger[sectEntry] = capacityWorld.Ledger[sectEntry] with { Quantity = long.MaxValue - 5 };
        var capacity = new SimulationEngine(capacityWorld);
        capacity.Submit(Transfer("reserved-capacity", 4, 2));
        capacity.Submit(Transfer("overflow", 2));
        var capacityResults = capacity.Step();
        Check(capacityResults[0].Accepted && !capacityResults[1].Accepted, "Incoming reservation was not counted");
        capacity.Step(); capacity.Step();
        Check(Inventory.Balance(capacity.Snapshot(), "sect", "spirit-stone") == long.MaxValue - 1, "Reserved delivery overflowed");
    }),
    ("Detached snapshots cannot modify simulation", () =>
    {
        var initial = DemoWorld.Create();
        var engine = new SimulationEngine(initial);
        var digest = WorldCodec.Digest(engine.Snapshot());
        initial.Ledger.Clear();
        var snapshot = engine.Snapshot();
        snapshot.Ledger.Clear();
        Check(digest == WorldCodec.Digest(engine.Snapshot()), "External mutation reached engine");
    }),
    ("Frame cadence does not affect fixed ticks", () =>
    {
        var first = new SimulationEngine(DemoWorld.Create());
        var second = new SimulationEngine(DemoWorld.Create());
        first.Submit(Transfer(duration: 2)); second.Submit(Transfer(duration: 2));
        var a = new TickPump(first, TimeSpan.FromMilliseconds(250));
        var b = new TickPump(second, TimeSpan.FromMilliseconds(250));
        for (int i = 0; i < 100; i++) a.Advance(TimeSpan.FromMilliseconds(10));
        for (int i = 0; i < 4; i++) b.Advance(TimeSpan.FromMilliseconds(250));
        Check(WorldCodec.Digest(first.Snapshot()) == WorldCodec.Digest(second.Snapshot()), "Cadence changed world");
    }),
    ("Same seed and ordered inputs reproduce world", () =>
    {
        string Run()
        {
            var engine = new SimulationEngine(DemoWorld.Create(42));
            engine.Submit(Transfer(duration: 3));
            for (int i = 0; i < 8; i++) engine.Step();
            return WorldCodec.Digest(engine.Snapshot());
        }
        Check(Run() == Run(), "Replay differs");
        var random = new SeededRandom(42);
        random.NextUInt32();
        var restored = new SeededRandom(random.State);
        Check(random.NextUInt32() == restored.NextUInt32(), "Random continuation differs");
    }),
    ("Save/load preserves pending, schedule, dedup and continuation", () =>
    {
        var engine = new SimulationEngine(DemoWorld.Create());
        engine.Submit(Transfer("scheduled", 10, 3));
        engine.Step();
        engine.Submit(Transfer("pending", 5));
        var directory = Path.Combine(Path.GetTempPath(), "cultivation-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "world.json");
        try
        {
            var store = new JsonWorldStore();
            store.Save(path, engine.Snapshot());
            bool protectedSave = false;
            try { store.Save(path, DemoWorld.Create(99)); }
            catch (IOException) { protectedSave = true; }
            Check(protectedSave, "Existing save overwritten");
            var restored = new SimulationEngine(store.Load(path));
            engine.Submit(Transfer("scheduled", 10, 3));
            restored.Submit(Transfer("scheduled", 10, 3));
            for (int i = 0; i < 5; i++) { engine.Step(); restored.Step(); }
            Check(WorldCodec.Digest(engine.Snapshot()) == WorldCodec.Digest(restored.Snapshot()), "Continuation differs");
        }
        finally
        {
            // Only the unique directory created by this test is removed.
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }),
    ("Corrupt worlds are rejected", () =>
    {
        var world = DemoWorld.Create();
        world.SchemaVersion = 99;
        Rejects(() => new SimulationEngine(world));
        world = DemoWorld.Create();
        world.Ledger.Add(new(world.NextSequence++, 0, "bad", "player", "sect", "spirit-stone", 999999, "Transfer"));
        Rejects(() => new SimulationEngine(world));
        world = DemoWorld.Create();
        world.Ledger.Add(new(world.NextSequence++, 0, "orphan", "player", "$escrow:orphan", "spirit-stone", 1, "Reserved"));
        Rejects(() => new SimulationEngine(world));
    }),
    ("Probe only returns intents", () =>
    {
        var probe = new TransferProbe("sect");
        Check(probe.Decide(new("npc", 0, 100)) is not null, "No probe intent");
        Check(probe.Decide(new("npc", 0, 0)) is null, "Probe ignored known funds");
        Check(probe.Decide(new("npc", 1, 100)) is null, "Probe repeated");
    })
};

int failed = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + exception); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;
