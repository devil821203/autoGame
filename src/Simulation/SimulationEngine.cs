using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed class SimulationEngine
{
    private readonly object gate = new();
    private WorldState state;

    public SimulationEngine(WorldState initial)
    {
        state = WorldCodec.Clone(initial);
        WorldIntegrity.Validate(state);
    }

    public WorldState Snapshot() { lock (gate) return WorldCodec.Clone(state); }

    // Source identifies a trusted local adapter, not a network security credential.
    public void Submit(ActionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        lock (gate)
        {
            state.Pending.Add(new(state.NextSequence++, intent));
        }
    }

    public IReadOnlyList<ActionResult> Step()
    {
        lock (gate)
        {
            // A failed tick never leaves partial live mutations.
            var work = WorldCodec.Clone(state);
            work.Clock = work.Clock with { Tick = checked(work.Clock.Tick + 1) };
            foreach (var task in work.Scheduled.OrderBy(t => t.DueTick).ThenBy(t => t.Sequence).ToArray())
            {
                if (task.DueTick > work.Clock.Tick) continue;
                ActionExecutor.Transfer(work, task.Intent, ActionExecutor.Escrow(task.Intent.Id), task.Intent.TargetId, "Completed");
                ActionExecutor.Emit(work, task.Intent.Id, "Completed", "Scheduled transfer completed");
                work.Scheduled.Remove(task);
            }
            var results = new List<ActionResult>();
            var processed = work.ProcessedIntentIds.ToHashSet(StringComparer.Ordinal);
            foreach (var queued in work.Pending.OrderBy(p => p.Sequence))
            {
                var intent = queued.Intent;
                if (!processed.Add(intent.Id))
                {
                    results.Add(new(intent.Id, false, "Duplicate intent"));
                    continue;
                }
                work.ProcessedIntentIds.Add(intent.Id);
                var error = ActionValidator.Validate(work, intent);
                if (error is null) ActionExecutor.Execute(work, intent);
                else ActionExecutor.Emit(work, intent.Id, "Rejected", error);
                results.Add(new(intent.Id, error is null, error ?? "Accepted"));
            }
            work.Pending.Clear();
            WorldIntegrity.Validate(work);
            state = work;
            return results;
        }
    }
}
