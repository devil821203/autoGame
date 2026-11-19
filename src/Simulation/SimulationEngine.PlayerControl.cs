using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed partial class SimulationEngine
{
    private bool StopForControlSelection()
    {
        if(!PlayerControl.NeedsSelection(state))return false;
        state.PlayerAutoSchedule=false;LastAdvance=new(state.Time!.DiscreteEvents!.WorldTime,0,0,AdvanceStopReason.ControlRequired,"請選擇有資格的角色；若無人可控，可讀檔或開新局");return true;
    }
    public bool RequiresControlSelection {get{lock(gate)return PlayerControl.NeedsSelection(state);}}
    public ActionResult SwitchControlledCharacter(string target,bool paused)
    {
        lock(gate){var before=state;state=WorldCodec.Clone(state);try{string? error=PlayerControl.Switch(state,target,paused);if(error is not null){state=before;return new("control-switch",false,error);}WorldIntegrity.Validate(state);return new("control-switch",true,"已切換角色；世界仍暫停，自主排程已關閉");}catch{state=before;throw;}}
    }
    // Trusted simulation/test integration, not a player action or an arbitrary-NPC grant button.
    public bool RevokeControlGrant(string actorId,string reason)
    {
        lock(gate){var before=state;state=WorldCodec.Clone(state);try{bool changed=PlayerControl.Revoke(state,actorId,reason);WorldIntegrity.Validate(state);return changed;}catch{state=before;throw;}}
    }
}
