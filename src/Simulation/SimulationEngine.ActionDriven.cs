using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    public bool WaitsForPlayerAction { get { lock (gate) return state.Time?.ActionDriven is not null && !state.PlayerAutoSchedule; } }
    public bool ActionDrivenTimeEnabled { get { lock (gate) return state.Time?.ActionDriven is not null; } }
    public void SetPlayerAutonomy(bool enabled)
    {
        lock (gate) state.PlayerAutoSchedule = enabled;
    }

    // Called by the interactive adapter. Explicit Step/Headless APIs remain available
    // for waiting, battle rounds and controlled simulation measurements.
    public ActionResult ExecutePlayerActivity(ActionIntent intent)
    {
        lock (gate)
        {
            if (state.Time?.ActionDriven is not { } rules) throw new InvalidOperationException("Action-driven time not enabled");
            if (intent.Source != DecisionSource.Player || intent.ActorId != state.ControlledCharacterId)
                return new(intent.Id, false, "Only the controlled player can advance interactive time");
            if (state.ProcessedIntentIds.Contains(intent.Id)) return new(intent.Id, false, "Duplicate intent");
            var error = ActionValidator.Validate(state, intent);
            if (error is not null) return new(intent.Id, false, error);
            var original = WorldCodec.Clone(state);
            var previousDiagnostics = LastDiagnostics;
            try {
                Submit(intent);
                var results = Step(true);
                var result = results.SingleOrDefault(r => r.IntentId == intent.Id) ?? new(intent.Id, false, "Action was not submitted");
                if (!result.Accepted) { state = original; return result; }
                for (int steps = 1; PlayerActivityBusy(state); steps++) {
                    if (state.BoardBattle is { Phase: BoardBattlePhase.Active } battle && battle.Fighters.Any(f => f.ActorId == state.ControlledCharacterId) && !battle.PlayerAutoCombat) break;
                    if (steps >= rules.MaximumAdvanceSteps) throw new InvalidOperationException("Activity exceeds time/action-driven.json MaximumAdvanceSteps");
                    Step(true);
                }
                return result;
            } catch { state = original; LastDiagnostics = previousDiagnostics; throw; }
        }
    }
    private static bool PlayerActivityBusy(WorldState w)
    {
        string id = w.ControlledCharacterId;
        if (w.Characters.Single(a => a.Id == id).Health <= 0) return false;
        return TimeActions.Budget(w, id).BusyThroughTick > w.Clock.Tick ||
            w.Time!.ApRestingActors.Contains(id) ||
            w.MortalTrainingWork.Any(t => t.Intent.ActorId == id) || w.Scheduled.Any(t => t.Intent.ActorId == id) ||
            w.Time!.Deferred.Any(t => t.Intent.ActorId == id) ||
            w.LifeSystem?.Work.Any(t => t.Intent.ActorId == id || t.Service?.ClientId == id) == true ||
            w.Characters.Single(a => a.Id == id).Stamina?.Resting == true;
    }
}
