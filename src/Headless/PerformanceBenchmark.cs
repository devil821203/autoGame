using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Headless;

public static class PerformanceBenchmark
{
    public static void Run(int count, int ticks, string? output)
    {
        if (ticks is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(ticks));
        var creation = Stopwatch.StartNew();
        var engine = new SimulationEngine(MedicineOnlyProfile.ApplyFresh(ScaleScenario.Create(count))); creation.Stop();
        for (int n = 0; n < 5; n++) engine.Step(true);
        var samples = new List<TickDiagnostics>();
        for (int n = 0; n < ticks; n++) { engine.Step(true); samples.Add(engine.LastDiagnostics!); }
        var world = engine.Snapshot(); var process = Process.GetCurrentProcess();
        double[] elapsed = samples.Select(s => s.TotalMilliseconds).Order().ToArray();
        var report = new
        {
            CapturedUtc = DateTimeOffset.UtcNow, Scenario = "synthetic-local-and-remote-owned-resource", Seed = 12345,
            NpcCount = count, WarmupTicks = 5, MeasuredTicks = ticks, FinalTick = world.Clock.Tick,
            Configuration = world.Performance, DecisionBudget = world.Rules.DecisionBudget,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), LogicalCores = Environment.ProcessorCount,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
#if DEBUG
            Build = "Debug",
#else
            Build = "Release",
#endif
            CreationMs = creation.Elapsed.TotalMilliseconds, MeanTickMs = elapsed.Average(),
            P95TickMs = elapsed[Math.Max(0, (int)Math.Ceiling(elapsed.Length * 0.95) - 1)], MaxTickMs = elapsed[^1],
            MeanCloneMs = samples.Average(s => s.CloneMilliseconds), MeanAiMs = samples.Average(s => s.AiMilliseconds),
            MeanValidationMs = samples.Average(s => s.ValidationMilliseconds), MeanAllocatedBytes = samples.Average(s => (double)s.AllocatedBytes),
            ManagedMemoryBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64,
            PeakWorkingSetBytes = process.PeakWorkingSet64, SerializedWorldBytes = Encoding.UTF8.GetByteCount(WorldCodec.Serialize(world)),
            AiEvaluations = samples.Sum(s => s.Decisions), DistinctUpdatedNpcs = world.AiUpdates.Count,
            Nearby = world.Characters.Count(c => c.Id != "player" && c.RegionId == "valley" && c.DemoLocationId == "town"),
            Regional = world.Characters.Count(c => c.Id != "player" && c.RegionId == "valley" && c.DemoLocationId != "town"),
            Remote = world.Characters.Count(c => c.RegionId == "remote"),
            FinalQueueLength = samples[^1].PendingEvents, Trades = samples.Sum(s => s.Trades),
            RetainedDiagnostics = world.Events.Count(e => e.Kind is "Decision" or "DecisionBudget" or "PlanInvalidated" or "PartyWaiting"),
            LedgerRows = world.Ledger.Count, Fps = (double?)null, Note = "Headless measurement; no rendering FPS. Fixed decision budget, not full AI evaluation for every NPC each Tick."
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (output is not null)
        {
            string full = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream); writer.Write(json);
        }
        Console.WriteLine(json);
    }
}
