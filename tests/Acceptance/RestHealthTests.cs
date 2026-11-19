using System.Text.Json.Nodes;
using Cultivation.Domain;
using Cultivation.Simulation;

internal static class RestHealthTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static WorldState World(int health = 75, int stamina = 100)
    {
        var w = GrowthScenario.Create();
        w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Health = health, Stamina = new(stamina) } : c).ToList();
        return w;
    }
    private static ActionIntent Rest(string actor = "player", DecisionSource source = DecisionSource.Player) =>
        new("health-rest:" + actor, actor, actor, "stamina", 1, 0, source, ActionKind.Rest);
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Rest heals from next tick even at full stamina without consuming pills", () =>
        {
            var w = World(); var e = new SimulationEngine(w); long pills = Inventory.Balance(w, "player", "pill");
            e.Submit(Rest()); Check(e.Step().Single().Accepted, "Full stamina injured rest rejected");
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").Health == 75, "Healed on start tick");
            e.Step(); Check(e.Snapshot().Characters.Single(c => c.Id == "player").Health == 85, "Recovery rate wrong");
            e.Step(); e.Step(); var end = e.Snapshot(); var actor = end.Characters.Single(c => c.Id == "player");
            Check(actor.Health == 100 && actor.Stamina is { Current: 100, Resting: false }, "Rest did not clamp and end");
            Check(Inventory.Balance(end, "player", "pill") == pills && !end.Ledger.Any(l => l.Sequence >= w.NextSequence && l.From == "player"), "Rest charged resources");
            Check(ActionValidator.Validate(end, Rest()) is not null, "Both full still can rest");
        }),
        ("Rest waits for both resources and resumes exactly after save", () =>
        {
            var e = new SimulationEngine(World(40, 70)); e.Submit(Rest()); e.Step(); e.Step();
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").Stamina!.Resting, "Ended when stamina filled before HP");
            var resume = new SimulationEngine(WorldCodec.Clone(e.Snapshot()));
            for (int n = 0; n < 6; n++) { e.Step(); resume.Step(); }
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resume.Snapshot()), "Save duplicated recovery");
            Check(e.Snapshot().Characters.Single(c => c.Id == "player") is { Health: 100, Stamina.Resting: false }, "Rest never completed");
        }),
        ("Rest never heals zero HP or during legacy and board combat", () =>
        {
            var w = World(0); w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Stamina = c.Stamina! with { Resting = true } } : c).ToList();
            var e = new SimulationEngine(w); e.Step(); Check(e.Snapshot().Characters.Single(c => c.Id == "player").Health == 0, "Rest revived actor");
            w = World(50); w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Stamina = c.Stamina! with { Resting = true } } : c).ToList();
            w.Combats.Add(new("rest-combat", "player", "npc:00", 0, 2)); e = new(w); e.Step();
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").Health == 50, "Rest healed during old combat");
            w = BoardBattleScenario.Create(); w.StaminaRules = new(HealthRecoveryPerTick: 10);
            w.Characters = w.Characters.Select(c => c with { Health = c.Id == "player" ? 50 : 100, Stamina = new(100, true) }).ToList();
            e = new(w); e.Step(); Check(e.Snapshot().Characters.Single(c => c.Id == "player").Health == 50, "Rest healed during board combat");
        }),
        ("Injured NPCs rest autonomously and old saves retain no healing", () =>
        {
            var w = World(); w.Characters = w.Characters.Select(c => c.Id == "npc:00" ? c with { Health = 90, Stamina = new(100) } : c).ToList();
            var e = new SimulationEngine(w); e.Step(true);
            Check(e.Snapshot().Characters.Single(c => c.Id == "npc:00").Stamina!.Resting, "Injured NPC did not rest");
            e.Step(true); Check(e.Snapshot().Characters.Single(c => c.Id == "npc:00").Health == 100, "NPC rest did not heal");
            var json = JsonNode.Parse(WorldCodec.Serialize(World()))!;
            json["StaminaRules"]!.AsObject().Remove("HealthRecoveryPerTick");
            var old = WorldCodec.Deserialize(json.ToJsonString());
            Check(old.StaminaRules!.HealthRecoveryPerTick == 0, "Old file changed healing rules");
            Check(ActionValidator.Validate(old, Rest()) is not null, "Legacy full-stamina rest changed");
        })
    };
}

