using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;
using Cultivation.Headless;

try
{
    if (args.Length == 3 && args[0] == "--settlement-organization-report")
    { SettlementGrowthReport.Run(int.Parse(args[1]), args[2], residentOrganization:true); return 0; }
    if (args.Length == 3 && args[0] == "--settlement-growth-report")
    { SettlementGrowthReport.Run(int.Parse(args[1]), args[2]); return 0; }
    if (args.Length >= 3 && args[0] == "--specialist-years-resume")
    { SpecialistLongRunReport.Run(args[1], resumePath: args[2]); return 0; }
    if (args.Length >= 2 && args[0] == "--specialist-years")
    { SpecialistLongRunReport.Run(args[1], args.Length > 2 ? long.Parse(args[2]) : null); return 0; }
    if (args.Length >= 2 && args[0] == "--fighter-economy-report")
    { FighterEconomyReport.Run(int.Parse(args[1]), args.Length > 2 ? ulong.Parse(args[2]) : 12345, args.Length > 3 ? args[3] : "growth"); return 0; }
    if (args.Length >= 3 && args[0] == "--soak")
    { LongRunProbe.Run(int.Parse(args[1]), ulong.Parse(args[2]), args.Length > 3 ? args[3] : null); return 0; }
    if (args.Length >= 3 && args[0] == "--benchmark")
    { PerformanceBenchmark.Run(int.Parse(args[1]), int.Parse(args[2]), args.Length > 3 ? args[3] : null); return 0; }
    if (args.Length >= 2 && args[0] == "--economy-report")
    { EconomyReport.Run(int.Parse(args[1]), args.Length > 2 ? ulong.Parse(args[2]) : 12345, args.Length > 3 && args[3] == "hex"); return 0; }
    ulong seed = 12345;
    int ticks = 10;
    string? load = null, save = null;
    bool autonomous = false;
    bool missions = false;
    bool advancement = false;
    bool combat = false;
    bool social = false;
    for (int i = 0; i < args.Length; i++)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("Each option needs a value.");
        switch (args[i])
        {
            case "--seed": seed = ulong.Parse(args[++i]); break;
            case "--ticks": ticks = int.Parse(args[++i]); break;
            case "--load": load = args[++i]; break;
            case "--save": save = args[++i]; break;
            case "--mode":
                string mode = args[++i];
                if (mode is not ("autonomous" or "missions" or "growth" or "combat" or "social" or "demo")) throw new ArgumentException("Unknown mode: " + mode);
                autonomous = mode != "demo"; missions = mode == "missions"; social = mode == "social"; combat = mode is "combat" or "social"; advancement = mode is "growth" or "combat" or "social"; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
    var store = new JsonWorldStore();
    var engine = new SimulationEngine(load is null ? MedicineOnlyProfile.ApplyFresh(social ? SocialScenario.Create(seed) : combat ? CombatScenario.Create(seed) : advancement ? AdvancementScenario.Create(seed) : missions ? MissionScenario.Create(seed) : autonomous ? AutonomousWorld.Create(seed) : DemoWorld.Create(seed)) : store.Load(load));
    if (load is null && social) engine.Submit(new("social-demo", "player", "npc:04", "social", 1, 0, DecisionSource.Player, ActionKind.InviteRecruit));
    if (load is null && missions) engine.Submit(MissionScenario.Publish(engine.Snapshot()));
    if (load is null && !autonomous)
    {
        engine.Submit(new("player-demo", "player", "sect", "spirit-stone", 10, 2, DecisionSource.Player));
        var snapshot = engine.Snapshot();
        var intent = new TransferProbe("sect").Decide(new("npc", snapshot.Clock.Tick,
            Inventory.Balance(snapshot, "npc", "spirit-stone")));
        if (intent is not null) engine.Submit(intent);
    }
    for (int i = 0; i < ticks; i++)
        if (autonomous) AutonomousRunner.Step(engine); else engine.Step();
    var final = engine.Snapshot();
    if (save is not null) store.Save(save, final);
    Console.WriteLine($"Tick={final.Clock.Tick} Scheduled={final.Scheduled.Count} Ledger={final.Ledger.Count}");
    Console.WriteLine($"SHA256={WorldCodec.Digest(final)}");
    if (autonomous)
        Console.WriteLine($"NPC={final.Characters.Count - 1} Trades={final.Events.Count(e => e.Kind == "Trade")} Brewed={final.Events.Count(e => e.Kind == "BrewSucceeded")} Failed={final.Events.Count(e => e.Kind == "BrewFailed")} Growth={final.Characters.Sum(c => c.Cultivation)}");
    foreach (var mission in final.Missions)
        Console.WriteLine($"Mission={mission.Id} Status={mission.Status} Delivered={mission.Delivered}/{mission.Required} Paid={mission.Delivered * mission.UnitPrice}/{mission.ReservedBudget} Participants={mission.Participants.Count}");
    if (missions)
        Console.WriteLine($"HerbQuote={MarketPricing.Quote(final, "herb").BuyPrice} RecoveryQuote={MarketPricing.Quote(final, final.RecoveryResourceId).BuyPrice} PriceChanges={final.Events.Count(e => e.Kind == "PriceChanged")}");
    if (advancement)
        Console.WriteLine($"Methods={final.Characters.Count(c => c.MethodId is not null)} TotalRealm={final.Characters.Sum(c => c.Realm)} Equipped={final.Characters.Count(c => c.EquippedWeaponId is not null)}");
    if (combat)
        Console.WriteLine($"Combats={final.Combats.Count} Victories={final.Combats.Count(c => c.Status == CombatStatus.Victory)} Retreated={final.Combats.Count(c => c.Status == CombatStatus.Retreated)} LootEntries={final.Loot.Count}");
    if (social) Console.WriteLine($"Relationships={final.Relationships.Count} Offers={final.SocialOffers.Count} Accepted={final.SocialOffers.Count(o => o.Status == SocialOfferStatus.Accepted)} Mentored={final.Characters.Count(c => c.MentorId is not null)}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

