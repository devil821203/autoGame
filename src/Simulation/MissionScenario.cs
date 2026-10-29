using Cultivation.Domain;

namespace Cultivation.Simulation;

// Reproducible fixture; behavior is chosen by the regular utility planner.
public static class MissionScenario
{
    public static WorldState Create(ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = AutonomousWorld.Create(seed, configuration: config);
        w.Rules = config.Patch("economy/mission-production.json", w.Rules);
        w.Organizations = w.Organizations.Select(o => o.Id == "sect" ? o with { LeaderId = "player" } : o).ToList();
        config.ApplyCharacters(w, "mission");
        w.Ledger.Clear(); w.NextSequence = 1;
        config.AddStock(w, "mission");
        w.MarketListings = config.Read<List<MarketListing>>("economy/market.json");
        WorldIntegrity.Validate(w);
        return w;
    }

    public static ActionIntent Publish(WorldState w, string id = "rejuvenation")
    {
        var r = w.MissionDefaults ?? new MissionTemplate();
        return new("publish:" + id, "player", "sect", w.MedicineOnly ? "herb" : "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission,
            Offer: new(id, r.Quantity, r.UnitPrice, checked(w.Clock.Tick + (w.Time is { } time ? r.DeadlineDays * time.Rules.TicksPerDay :
                (r.DeadlineDays * 86400 + w.Clock.SimulatedSecondsPerTick - 1) / w.Clock.SimulatedSecondsPerTick)), r.MaximumBudget));
    }
}
