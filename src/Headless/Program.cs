using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

try
{
    ulong seed = 12345;
    int ticks = 10;
    string? load = null, save = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("Each option needs a value.");
        switch (args[i])
        {
            case "--seed": seed = ulong.Parse(args[++i]); break;
            case "--ticks": ticks = int.Parse(args[++i]); break;
            case "--load": load = args[++i]; break;
            case "--save": save = args[++i]; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
    var store = new JsonWorldStore();
    var engine = new SimulationEngine(load is null ? DemoWorld.Create(seed) : store.Load(load));
    if (load is null)
    {
        engine.Submit(new("player-demo", "player", "sect", "spirit-stone", 10, 2, DecisionSource.Player));
        var snapshot = engine.Snapshot();
        var intent = new TransferProbe("sect").Decide(new("npc", snapshot.Clock.Tick,
            Inventory.Balance(snapshot, "npc", "spirit-stone")));
        if (intent is not null) engine.Submit(intent);
    }
    for (int i = 0; i < ticks; i++) engine.Step();
    var final = engine.Snapshot();
    if (save is not null) store.Save(save, final);
    Console.WriteLine($"Tick={final.Clock.Tick} Scheduled={final.Scheduled.Count} Ledger={final.Ledger.Count}");
    Console.WriteLine($"SHA256={WorldCodec.Digest(final)}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
