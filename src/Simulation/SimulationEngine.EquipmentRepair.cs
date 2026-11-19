using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    // Trusted interruption hook for combat/system integration and reproducible tests, not remote character control.
    public void InterruptEquipmentRepair(string actorId)
    {
        lock(gate){var original=state;state=WorldCodec.Clone(state);try{EquipmentRepairActions.InterruptActor(state,actorId);WorldIntegrity.Validate(state);}catch{state=original;throw;}}
    }
}
