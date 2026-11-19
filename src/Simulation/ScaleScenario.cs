using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class ScaleScenario
{
    public static WorldState Create(int count = 100, ulong seed = 12345, GameConfiguration? configuration = null)
    {
        if (count is < 50 or > 20000) throw new ArgumentOutOfRangeException(nameof(count));
        var config = configuration ?? GameConfiguration.Find(); var w = AutonomousWorld.Create(seed, 50, config);
        w.Performance = config.Read<PerformanceRules>("world/performance.json"); w.ScaleNpcCount = count; w.DemoMapEnabled = true;
        var templates = w.Characters.Skip(1).ToArray();
        for (int n = 50; n < count; n++)
        {
            string id = $"npc:{n:D5}"; var template = templates[n % templates.Length];
            w.Characters.Add(template with { Id = id, Name = $"均衡者{n - 11:D2}", Aspiration = Aspiration.Balanced });
            foreach (var item in new[] { ("spirit-stone", 60L), ("herb", template.AlchemyChance > 0 ? 4L : 0L) })
                if (item.Item2 > 0) w.Ledger.Add(new(w.NextSequence++, 0, $"scale:{id}:{item.Item1}", "$genesis", id, item.Item1, item.Item2, "Genesis"));
        }
        for (int n = 1; n < w.Characters.Count; n++)
        {
            var a = w.Characters[n];
            w.Characters[n] = a with { RegionId = n % 3 == 0 ? "remote" : "valley", DemoLocationId = n % 3 == 1 ? "town" : "grove" };
            w.Ledger.Add(new(w.NextSequence++, 0, "scale-pill:" + a.Id, "$genesis", a.Id, "pill", 3, "Genesis"));
        }
        // This is a synthetic scale fixture, not a commitment to a formal map or remote market.
        WorldIntegrity.Validate(w); return w;
    }
}
