using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    public bool WaitsForPlayerAction { get { lock (gate) return state.Time?.ActionDriven is not null && !state.PlayerAutoSchedule; } }
    public bool ActionDrivenTimeEnabled { get { lock (gate) return state.Time?.ActionDriven is not null; } }
    public void SetPlayerAutonomy(bool enabled)
    {
        lock (gate) {
            if(enabled&&PlayerControl.NeedsSelection(state))throw new InvalidOperationException("請先選擇有資格的角色");
            state.PlayerAutoSchedule = enabled;
            if(enabled)EventDecisions.Request(state,state.ControlledCharacterId,DecisionTrigger.PlayerAutonomy);
            else if(state.Time?.EventDecisions is {} mode){mode.Ready.Remove(state.ControlledCharacterId);new EventScheduler(state.Time.DiscreteEvents!).Cancel(EventDecisions.RetryKey(state.ControlledCharacterId));}
        }
    }

    // Called by the interactive adapter. Explicit Step/Headless APIs remain available
    // for waiting, battle rounds and controlled simulation measurements.
    public ActionResult ExecutePlayerActivity(ActionIntent intent)
    {
        lock (gate)
        {
            if (state.Time?.ActionDriven is not { } rules) throw new InvalidOperationException("Action-driven time not enabled");
            if (intent.Source != DecisionSource.Player || !PlayerControl.IsControlled(state,intent.ActorId)||PlayerControl.NeedsSelection(state))
                return new(intent.Id, false, "Only the controlled player can advance interactive time");
            if (state.ProcessedIntentIds.Contains(intent.Id)) return new(intent.Id, false, "Duplicate intent");
            var error = ActionValidator.Validate(state, intent);
            if (error is not null) return new(intent.Id, false, error);
            var original = WorldCodec.Clone(state);
            var previousDiagnostics = LastDiagnostics;
            try {
                Submit(intent);
                bool preciseLife=!TimeActions.IsBattleCommand(intent.Kind)&&(PreciseScheduledAction(state,intent.Kind)||state.Time.IndependentLifeEvents&&(LifeActions.IsWork(intent.Kind)||intent.Kind==ActionKind.RequestLifeService));
                IReadOnlyList<ActionResult> results;
                if(preciseLife){var accepted=new List<ActionResult>();AdvanceDiscrete(state.Time.DiscreteEvents!.WorldTime,true,accepted);results=accepted;}
                else results=Step(true);
                var result = results.SingleOrDefault(r => r.IntentId == intent.Id) ?? new(intent.Id, false, "Action was not submitted");
                if (!result.Accepted) { state = original; return result; }
                for (int steps = 1; PlayerActivityBusy(state); steps++) {
                    if (state.Time.DiscreteEvents is not null && LastAdvance?.Reason is AdvanceStopReason.ImportantEvent or AdvanceStopReason.EventBudget or AdvanceStopReason.SameTimeBudget) break;
                    if (state.BoardBattle is { Phase: BoardBattlePhase.Active } battle && battle.Fighters.Any(f => f.ActorId == state.ControlledCharacterId) && !battle.PlayerAutoCombat) break;
                    if (steps >= rules.MaximumAdvanceSteps) throw new InvalidOperationException("Activity exceeds time/action-driven.json MaximumAdvanceSteps");
                    bool playerBattleActive=state.BoardBattle is { Phase: BoardBattlePhase.Active } active && active.Fighters.Any(f=>f.ActorId==state.ControlledCharacterId);
                    if(preciseLife&&!playerBattleActive&&NextActivityDeadline(state,state.ControlledCharacterId) is {} due)AdvanceDiscrete(due,true,[]);
                    else Step(true);
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
            WorldPeriodEvents.Enabled(w) && (w.Time!.BattleTime.Any(r=>r.RemainingTicks>0) || w.Time.DiscreteEvents!.BattleReleaseDeadlines.ContainsKey(id)) ||
            EquipmentRepairActions.Busy(w,id)||
              w.Time!.MarketOrders?.Commands.Any(c=>c.Intent.ActorId==id)==true ||
            w.Time!.ApRestingActors.Contains(id) ||
            w.MortalTrainingWork.Any(t => t.Intent.ActorId == id) || w.Scheduled.Any(t => t.Intent.ActorId == id) ||
            w.Time!.Deferred.Any(t => t.Intent.ActorId == id) ||
            w.LifeSystem?.Work.Any(t => t.Intent.ActorId == id || t.Service?.ClientId == id) == true ||
            w.Characters.Single(a => a.Id == id).Stamina?.Resting == true;
    }
}
