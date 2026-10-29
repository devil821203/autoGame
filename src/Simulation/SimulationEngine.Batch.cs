using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    // Headless measurement adapter: one detached transaction per batch; full validation per Tick by default.
    // Observers cannot see partial work; a failed batch restores the previously committed state.
    public int StepValidatedBatch(long throughTick, int maximumSteps, bool validateEveryTick = true)
    {
        lock (gate)
        {
            if (state.Time is null || throughTick < state.Clock.Tick || maximumSteps <= 0) throw new ArgumentException("Invalid timed batch boundary");
            var original = state; var previousDiagnostics = LastDiagnostics;
            state = WorldCodec.Clone(state);
            try
            {
                int steps = 0;
                while (state.Clock.Tick < throughTick && steps < maximumSteps)
                {
                    if (state.BoardBattle is { Phase: BoardBattlePhase.Active, PlayerAutoCombat: false } b && b.Fighters.Any(f => f.ActorId == state.ControlledCharacterId)) break;
                    StepTimed(true, cloneState: false, validate: validateEveryTick); steps++;
                }
                if (!validateEveryTick) WorldIntegrity.Validate(state);
                return steps;
            }
            catch { state = original; LastDiagnostics = previousDiagnostics; throw; }
        }
    }
    public IReadOnlyList<WorldEvent> ArchiveMeasurementEvents(long beforeTick)
    {
        lock (gate)
        {
            var archived = state.Events.Where(e => e.Tick < beforeTick && e.Kind is "MortalTrainingStarted" or "MortalTrainingCompleted" or "MortalAttributeImproved" or "ActionPointsSpent" or "RestStarted" or "RestRecovered" or "RestHealthRecovered").ToArray();
            var sequences = archived.Select(e => e.Sequence).ToHashSet();
            state.Events.RemoveAll(e => sequences.Contains(e.Sequence));
            return archived;
        }
    }
}
