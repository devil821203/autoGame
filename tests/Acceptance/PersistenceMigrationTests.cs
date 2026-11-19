using System.Text.Json.Nodes;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class PersistenceMigrationTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Schema one migration preserves active travel and source file", () =>
        {
            var engine = new SimulationEngine(PlayerDemoScenario.Create("舊檔修士"));
            engine.Submit(new("trip", "player", "grove", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel)); engine.Step();
            var expected = engine.Snapshot();
            var node = JsonNode.Parse(WorldCodec.Serialize(expected))!; node["SchemaVersion"] = 1;
            string source = node.ToJsonString();
            WithFile(source, path =>
            {
                var loaded = new JsonWorldStore().Load(path);
                if (loaded.SchemaVersion != WorldState.CurrentSchemaVersion || WorldCodec.Digest(loaded) != WorldCodec.Digest(expected) || File.ReadAllText(path) != source) throw new Exception("Migration lost data or changed source");
                var resumed = new SimulationEngine(loaded);
                for (int n = 0; n < 4; n++) { engine.Step(true); resumed.Step(true); }
                if (WorldCodec.Digest(engine.Snapshot()) != WorldCodec.Digest(resumed.Snapshot())) throw new Exception("Migrated continuation diverged");
            });
        }),
        ("Legacy missing demo fields migrate and unsupported versions fail", () =>
        {
            var node = JsonNode.Parse(WorldCodec.Serialize(SocialScenario.Create()))!.AsObject();
            node["SchemaVersion"] = 1; node.Remove("DemoMapEnabled"); node.Remove("HerbRegeneration"); node.Remove("Notifications");
            foreach (var actor in node["Characters"]!.AsArray()) actor!.AsObject().Remove("DemoLocationId");
            WithFile(node.ToJsonString(), path =>
            {
                var loaded = new JsonWorldStore().Load(path);
                if (loaded.DemoMapEnabled || loaded.Notifications.Count != 0 || loaded.Characters.Any(c => c.DemoLocationId != "town")) throw new Exception("Legacy defaults incorrect");
            });
            foreach (var version in new[] { 0, WorldState.CurrentSchemaVersion + 1, 99 })
            {
                node["SchemaVersion"] = version;
                WithFile(node.ToJsonString(), path =>
                {
                    bool rejected = false; try { _ = new JsonWorldStore().Load(path); } catch (InvalidDataException) { rejected = true; }
                    if (!rejected) throw new Exception("Unsupported version loaded");
                });
            }
        })
    };
    private static void WithFile(string json, Action<string> run)
    {
        string path = Path.Combine(Path.GetTempPath(), "cultivation-migration-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(path, json); run(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
