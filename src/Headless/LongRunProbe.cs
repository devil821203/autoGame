using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

namespace Cultivation.Headless;

public static class LongRunProbe
{
    public static void Run(int ticks, ulong seed, string? output)
    {
        if (ticks is < 2 or > 100000) throw new ArgumentOutOfRangeException(nameof(ticks));
        var config = GameConfiguration.Find(); var world = MedicineOnlyProfile.ApplyFresh(SectEconomyScenario.Create(seed: seed, configuration: config), config);
        world.Performance = config.Read<PerformanceRules>("world/performance.json");
        var engine = new SimulationEngine(world); SimulationEngine? resumed = null;
        long initialBytes = Encoding.UTF8.GetByteCount(WorldCodec.Serialize(world));
        var timer = Stopwatch.StartNew(); var samples = new List<object>(); int maxDiagnostics = 0;
        string checkpoint = Path.Combine(Path.GetTempPath(), "cultivation-stage5-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            for (int n = 0; n < ticks; n++)
            {
                engine.Step(true); resumed?.Step(true);
                var m = engine.LastDiagnostics!;
                if (n % 100 == 0 || n + 1 == ticks)
                {
                    var w = engine.Snapshot(); int diagnostics = w.Events.Count(e => e.Kind is "Decision" or "DecisionBudget" or "PlanInvalidated" or "PartyWaiting");
                    maxDiagnostics = Math.Max(maxDiagnostics, diagnostics);
                    samples.Add(new { Tick = w.Clock.Tick, m.TotalMilliseconds, m.Decisions, m.PendingEvents, m.ManagedMemoryBytes, LedgerRows = w.Ledger.Count, DiagnosticRows = diagnostics });
                    if (output is not null) Console.WriteLine($"SOAK_PROGRESS tick={w.Clock.Tick}/{ticks} ledger={w.Ledger.Count} diagnostics={diagnostics}");
                }
                if (n + 1 == ticks / 2)
                {
                    new JsonWorldStore().Save(checkpoint, engine.Snapshot());
                    resumed = new(new JsonWorldStore().Load(checkpoint));
                }
            }
            var final = engine.Snapshot();
            bool equal = resumed is not null && WorldCodec.Digest(final) == WorldCodec.Digest(resumed.Snapshot());
            if (!equal) throw new InvalidOperationException("Long-running file save continuation diverged");
            var report = new
            {
                Seed = seed, Ticks = ticks, Scenario = "sect-economy-with-LOD", Configuration = final.Performance,
                FileCheckpointTick = ticks / 2, IdenticalContinuation = equal, TotalRunMs = timer.Elapsed.TotalMilliseconds,
                InitialSaveBytes = initialBytes, FinalSaveBytes = Encoding.UTF8.GetByteCount(WorldCodec.Serialize(final)),
                AuditLedgerRows = final.Ledger.Count, DedupIds = final.ProcessedIntentIds.Count, MaxSampledDiagnosticRows = maxDiagnostics,
                EndMembers = final.Characters.Count(c => c.OrganizationId == "sect"), EndStones = Inventory.Balance(final, "sect", "spirit-stone"),
                ProducedPills = final.SectWorkReceipts.Count(r => r.Kind == ActionKind.SectBrew && r.Success),
                LastQuarterPills = final.SectWorkReceipts.Count(r => r.Tick > ticks * 3L / 4 && r.Kind == ActionKind.SectBrew && r.Success),
                LastQuarterProcurement = final.ProcurementReceipts.Where(r => r.Tick > ticks * 3L / 4).Sum(r => r.Quantity),
                MissedPayroll = final.Events.Count(e => e.Kind == "PayrollSkipped"), Expelled = final.Events.Count(e => e.Kind == "DutyExpelled"),
                Samples = samples, Note = "Every Tick validated. Audit ledger and replay IDs deliberately retained; finite soak is not a guarantee of indefinitely bounded memory or economic equilibrium."
            };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            if (output is not null)
            {
                string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write); using var writer = new StreamWriter(stream); writer.Write(json);
            }
            Console.WriteLine(json);
        }
        finally { if (File.Exists(checkpoint)) File.Delete(checkpoint); }
    }
}
